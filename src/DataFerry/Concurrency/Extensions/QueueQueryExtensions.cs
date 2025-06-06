// ===========================================================================
// <copyright file="QueueQueryExtensions.cs" company="Level Up Software">
// Copyright (c) Level Up Software. All rights reserved.
// </copyright>
// ===========================================================================

using System.Runtime.CompilerServices;

using lvlup.DataFerry.Concurrency.Contracts;
using lvlup.DataFerry.Concurrency.Enumerators;

namespace lvlup.DataFerry.Concurrency.Extensions;

/// <summary>
/// Provides LINQ-style query extension methods for concurrent priority queues.
/// </summary>
/// <remarks>
/// <para>
/// These extension methods enable functional-style queries over concurrent priority queues
/// while maintaining thread-safety and performance characteristics. All methods use
/// lock-free enumeration and provide "read committed" consistency.
/// </para>
/// <para>
/// The methods are designed to work efficiently with the concurrent nature of the queue,
/// using lazy evaluation where possible and providing options for both streaming and
/// snapshot-based operations.
/// </para>
/// </remarks>
public static class QueueQueryExtensions
{
    #region Filtering Extensions

    /// <summary>
    /// Filters elements based on their priority values.
    /// </summary>
    /// <typeparam name="TPriority">The type used for priority values.</typeparam>
    /// <typeparam name="TElement">The type of the elements stored in the queue.</typeparam>
    /// <param name="queue">The queue to filter.</param>
    /// <param name="predicate">A function to test each priority for a condition.</param>
    /// <returns>An enumerable sequence containing only elements whose priorities satisfy the condition.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="queue"/> or <paramref name="predicate"/> is null.</exception>
    /// <remarks>
    /// This method uses deferred execution and lock-free enumeration. The predicate is
    /// evaluated as the sequence is enumerated, allowing early termination when combined
    /// with methods like <c>Take</c> or <c>First</c>.
    /// </remarks>
    public static IEnumerable<TElement> Where<TPriority, TElement>(
        this IConcurrentPriorityQueue<TPriority, TElement> queue,
        Func<TPriority, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(queue, nameof(queue));
        ArgumentNullException.ThrowIfNull(predicate, nameof(predicate));

        return WhereIterator(queue, predicate);
    }

    /// <summary>
    /// Filters elements based on both priority and element values.
    /// </summary>
    /// <typeparam name="TPriority">The type used for priority values.</typeparam>
    /// <typeparam name="TElement">The type of the elements stored in the queue.</typeparam>
    /// <param name="queue">The queue to filter.</param>
    /// <param name="predicate">A function to test each priority-element pair for a condition.</param>
    /// <returns>An enumerable sequence containing only elements that satisfy the condition.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="queue"/> or <paramref name="predicate"/> is null.</exception>
    public static IEnumerable<(TPriority Priority, TElement Element)> WhereWithPriority<TPriority, TElement>(
        this IConcurrentPriorityQueue<TPriority, TElement> queue,
        Func<TPriority, TElement, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(queue, nameof(queue));
        ArgumentNullException.ThrowIfNull(predicate, nameof(predicate));

        return WhereWithPriorityIterator(queue, predicate);
    }

    #endregion

    #region Projection Extensions

    /// <summary>
    /// Projects each element of the queue into a new form based on its priority and element.
    /// </summary>
    /// <typeparam name="TPriority">The type used for priority values.</typeparam>
    /// <typeparam name="TElement">The type of the elements stored in the queue.</typeparam>
    /// <typeparam name="TResult">The type of the value returned by the selector.</typeparam>
    /// <param name="queue">The queue to project.</param>
    /// <param name="selector">A transform function to apply to each priority-element pair.</param>
    /// <returns>An enumerable sequence whose elements are the result of invoking the transform function on each element of the queue.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="queue"/> or <paramref name="selector"/> is null.</exception>
    public static IEnumerable<TResult> Select<TPriority, TElement, TResult>(
        this IConcurrentPriorityQueue<TPriority, TElement> queue,
        Func<TPriority, TElement, TResult> selector)
    {
        ArgumentNullException.ThrowIfNull(queue, nameof(queue));
        ArgumentNullException.ThrowIfNull(selector, nameof(selector));

        return SelectIterator(queue, selector);
    }

    #endregion

    #region Async Extensions

    /// <summary>
    /// Converts the queue to an async enumerable sequence.
    /// </summary>
    /// <typeparam name="TPriority">The type used for priority values.</typeparam>
    /// <typeparam name="TElement">The type of the elements stored in the queue.</typeparam>
    /// <param name="queue">The queue to convert.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the enumeration.</param>
    /// <returns>An async enumerable sequence of priority-element pairs.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="queue"/> is null.</exception>
    /// <remarks>
    /// This method enables async LINQ operations over the queue. The enumeration
    /// periodically yields control to allow other async operations to proceed,
    /// making it suitable for use in async contexts.
    /// </remarks>
    public static async IAsyncEnumerable<(TPriority Priority, TElement Element)> ToAsyncEnumerable<TPriority, TElement>(
        this IConcurrentPriorityQueue<TPriority, TElement> queue,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(queue, nameof(queue));

        if (queue is ConcurrentPriorityQueue<TPriority, TElement> concreteQueue)
        {
            await using var enumerator = concreteQueue.GetAsyncEnumerator(cancellationToken);
            while (await enumerator.MoveNextAsync().ConfigureAwait(false))
            {
                yield return enumerator.Current;
            }
        }
        else
        {
            // Fallback for other implementations
            var yieldCounter = 0;
            foreach (var item in GetFullEnumerable(queue))
            {
                if (cancellationToken.IsCancellationRequested)
                    yield break;

                yield return item;

                // Yield periodically for better async behavior
                if (++yieldCounter % 100 == 0)
                {
#pragma warning disable VSTHRD111 // Use ConfigureAwait - Task.Yield() doesn't support ConfigureAwait
                    await Task.Yield();
#pragma warning restore VSTHRD111 // Use ConfigureAwait
                }
            }
        }
    }

    #endregion

    #region Snapshot Extensions

    /// <summary>
    /// Takes a snapshot of the queue's current state.
    /// </summary>
    /// <typeparam name="TPriority">The type used for priority values.</typeparam>
    /// <typeparam name="TElement">The type of the elements stored in the queue.</typeparam>
    /// <param name="queue">The queue to snapshot.</param>
    /// <param name="maxItems">The maximum number of items to include in the snapshot.</param>
    /// <returns>A list containing a snapshot of the queue's items in priority order.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="queue"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if <paramref name="maxItems"/> is negative.</exception>
    /// <remarks>
    /// This method materializes the queue state into a list, providing a stable view
    /// that won't change even if the queue is modified. Use this when you need a
    /// consistent view for operations like serialization or reporting.
    /// </remarks>
    public static List<(TPriority Priority, TElement Element)> TakeSnapshot<TPriority, TElement>(
        this IConcurrentPriorityQueue<TPriority, TElement> queue,
        int maxItems = int.MaxValue)
    {
        ArgumentNullException.ThrowIfNull(queue, nameof(queue));
        ArgumentOutOfRangeException.ThrowIfNegative(maxItems, nameof(maxItems));

        var snapshot = new List<(TPriority, TElement)>(Math.Min(maxItems, 1000));
        
        foreach (var item in GetFullEnumerable(queue))
        {
            snapshot.Add(item);
            if (snapshot.Count >= maxItems)
                break;
        }
        
        return snapshot;
    }

    /// <summary>
    /// Creates a snapshot enumerator for advanced snapshot-based operations.
    /// </summary>
    /// <typeparam name="TPriority">The type used for priority values.</typeparam>
    /// <typeparam name="TElement">The type of the elements stored in the queue.</typeparam>
    /// <param name="queue">The queue to create a snapshot from.</param>
    /// <returns>A <see cref="SnapshotEnumerator{TPriority, TElement}"/> containing the queue's current state.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="queue"/> is null.</exception>
    public static SnapshotEnumerator<TPriority, TElement> CreateSnapshot<TPriority, TElement>(
        this IConcurrentPriorityQueue<TPriority, TElement> queue)
    {
        ArgumentNullException.ThrowIfNull(queue, nameof(queue));
        return new SnapshotEnumerator<TPriority, TElement>(GetFullEnumerable(queue));
    }

    #endregion

    #region Aggregation Extensions

    /// <summary>
    /// Counts the number of elements in the queue that satisfy a condition.
    /// </summary>
    /// <typeparam name="TPriority">The type used for priority values.</typeparam>
    /// <typeparam name="TElement">The type of the elements stored in the queue.</typeparam>
    /// <param name="queue">The queue to count elements from.</param>
    /// <param name="predicate">A function to test each element for a condition.</param>
    /// <returns>The number of elements that satisfy the condition.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="queue"/> or <paramref name="predicate"/> is null.</exception>
    public static int Count<TPriority, TElement>(
        this IConcurrentPriorityQueue<TPriority, TElement> queue,
        Func<TPriority, TElement, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(queue, nameof(queue));
        ArgumentNullException.ThrowIfNull(predicate, nameof(predicate));

        var count = 0;
        foreach (var (priority, element) in GetFullEnumerable(queue))
        {
            if (predicate(priority, element))
                count++;
        }
        return count;
    }

    /// <summary>
    /// Determines whether any element in the queue satisfies a condition.
    /// </summary>
    /// <typeparam name="TPriority">The type used for priority values.</typeparam>
    /// <typeparam name="TElement">The type of the elements stored in the queue.</typeparam>
    /// <param name="queue">The queue to check.</param>
    /// <param name="predicate">A function to test each element for a condition.</param>
    /// <returns><c>true</c> if any element satisfies the condition; otherwise, <c>false</c>.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="queue"/> or <paramref name="predicate"/> is null.</exception>
    public static bool Any<TPriority, TElement>(
        this IConcurrentPriorityQueue<TPriority, TElement> queue,
        Func<TPriority, TElement, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(queue, nameof(queue));
        ArgumentNullException.ThrowIfNull(predicate, nameof(predicate));

        foreach (var (priority, element) in GetFullEnumerable(queue))
        {
            if (predicate(priority, element))
                return true;
        }
        return false;
    }

    #endregion

    #region Private Iterator Methods

    /// <summary>
    /// Iterator implementation for Where extension.
    /// </summary>
    private static IEnumerable<TElement> WhereIterator<TPriority, TElement>(
        IConcurrentPriorityQueue<TPriority, TElement> queue,
        Func<TPriority, bool> predicate)
    {
        foreach (var (priority, element) in GetFullEnumerable(queue))
        {
            if (predicate(priority))
                yield return element;
        }
    }

    /// <summary>
    /// Iterator implementation for WhereWithPriority extension.
    /// </summary>
    private static IEnumerable<(TPriority Priority, TElement Element)> WhereWithPriorityIterator<TPriority, TElement>(
        IConcurrentPriorityQueue<TPriority, TElement> queue,
        Func<TPriority, TElement, bool> predicate)
    {
        foreach (var (priority, element) in GetFullEnumerable(queue))
        {
            if (predicate(priority, element))
                yield return (priority, element);
        }
    }

    /// <summary>
    /// Iterator implementation for Select extension.
    /// </summary>
    private static IEnumerable<TResult> SelectIterator<TPriority, TElement, TResult>(
        IConcurrentPriorityQueue<TPriority, TElement> queue,
        Func<TPriority, TElement, TResult> selector)
    {
        foreach (var (priority, element) in GetFullEnumerable(queue))
        {
            yield return selector(priority, element);
        }
    }

    /// <summary>
    /// Gets a full enumerable of priority-element pairs from the queue.
    /// </summary>
    private static IEnumerable<(TPriority Priority, TElement Element)> GetFullEnumerable<TPriority, TElement>(
        IConcurrentPriorityQueue<TPriority, TElement> queue)
    {
        // If the queue is our concrete implementation, use its full enumerator
        if (queue is ConcurrentPriorityQueue<TPriority, TElement> concreteQueue)
        {
            return concreteQueue.GetFullEnumerator();
        }

        // Otherwise, we need to build pairs manually using the priority enumerator
        // This is less efficient but maintains compatibility
        var priorities = new List<TPriority>();
        using (var enumerator = queue.GetEnumerator())
        {
            while (enumerator.MoveNext())
            {
                priorities.Add(enumerator.Current);
            }
        }

        // Note: This approach doesn't guarantee element values match priorities
        // For full compatibility, IConcurrentPriorityQueue should expose a full enumerator
        throw new NotSupportedException(
            "Full enumeration requires ConcurrentPriorityQueue implementation. " +
            "Consider updating IConcurrentPriorityQueue to include GetFullEnumerator().");
    }

    #endregion
}