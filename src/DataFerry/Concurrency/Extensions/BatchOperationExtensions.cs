// ===========================================================================
// <copyright file="BatchOperationExtensions.cs" company="Level Up Software">
// Copyright (c) Level Up Software. All rights reserved.
// </copyright>
// ===========================================================================

using lvlup.DataFerry.Concurrency.Algorithms;
using lvlup.DataFerry.Concurrency.Contracts;

namespace lvlup.DataFerry.Concurrency.Extensions;

/// <summary>
/// Provides extension methods for batch operations on concurrent priority queues.
/// </summary>
public static class BatchOperationExtensions
{
    /// <summary>
    /// Adds a collection of items to the queue as a batch operation.
    /// </summary>
    /// <typeparam name="TPriority">The type of priority values.</typeparam>
    /// <typeparam name="TElement">The type of elements.</typeparam>
    /// <param name="queue">The priority queue to add items to.</param>
    /// <param name="items">The collection of items to add.</param>
    /// <param name="options">Optional batch operation options.</param>
    /// <returns>A result object containing operation statistics.</returns>
    public static async Task<BatchOperationResult> TryAddRangeAsync<TPriority, TElement>(
        this IConcurrentPriorityQueue<TPriority, TElement> queue,
        IEnumerable<(TPriority Priority, TElement Element)> items,
        BatchOperationOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(queue, nameof(queue));
        ArgumentNullException.ThrowIfNull(items, nameof(items));

        // Convert to async enumerable
        var asyncItems = items.ToAsyncEnumerable();
        
        if (queue is ConcurrentPriorityQueue<TPriority, TElement> concreteQueue)
        {
            return await concreteQueue.TryAddRangeAsync(asyncItems, options ?? new BatchOperationOptions()).ConfigureAwait(false);
        }

        // Fallback for other implementations
        var result = new BatchOperationResult();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        foreach (var item in items)
        {
            if (queue.TryAdd(item.Priority, item.Element))
            {
                result.SuccessCount++;
            }
            else
            {
                result.FailureCount++;
            }
        }

        result.Duration = stopwatch.Elapsed;
        return result;
    }

    /// <summary>
    /// Adds items from an async producer to the queue as they become available.
    /// </summary>
    /// <typeparam name="TPriority">The type of priority values.</typeparam>
    /// <typeparam name="TElement">The type of elements.</typeparam>
    /// <param name="queue">The priority queue to add items to.</param>
    /// <param name="producer">An async function that produces items.</param>
    /// <param name="options">Optional batch operation options.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>A result object containing operation statistics.</returns>
    public static async Task<BatchOperationResult> TryAddFromProducerAsync<TPriority, TElement>(
        this IConcurrentPriorityQueue<TPriority, TElement> queue,
        Func<IAsyncEnumerable<(TPriority Priority, TElement Element)>> producer,
        BatchOperationOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(queue, nameof(queue));
        ArgumentNullException.ThrowIfNull(producer, nameof(producer));

        var items = producer();
        
        if (queue is ConcurrentPriorityQueue<TPriority, TElement> concreteQueue)
        {
            return await concreteQueue.TryAddRangeAsync(items, options ?? new BatchOperationOptions(), cancellationToken).ConfigureAwait(false);
        }

        // Fallback implementation
        var result = new BatchOperationResult();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        await foreach (var item in items.WithCancellation(cancellationToken).ConfigureAwait(false))
        {
            if (queue.TryAdd(item.Priority, item.Element))
            {
                result.SuccessCount++;
            }
            else
            {
                result.FailureCount++;
            }
        }

        result.Duration = stopwatch.Elapsed;
        return result;
    }

    /// <summary>
    /// Updates the priority of an element in the queue.
    /// </summary>
    /// <typeparam name="TPriority">The type of priority values.</typeparam>
    /// <typeparam name="TElement">The type of elements.</typeparam>
    /// <param name="queue">The priority queue containing the element.</param>
    /// <param name="oldPriority">The current priority of the element.</param>
    /// <param name="newPriority">The new priority to assign.</param>
    /// <param name="element">The element value (used for verification).</param>
    /// <returns>True if the update was successful; otherwise, false.</returns>
    public static bool TryUpdatePriority<TPriority, TElement>(
        this IConcurrentPriorityQueue<TPriority, TElement> queue,
        TPriority oldPriority,
        TPriority newPriority,
        TElement element)
    {
        ArgumentNullException.ThrowIfNull(queue, nameof(queue));
        ArgumentNullException.ThrowIfNull(oldPriority, nameof(oldPriority));
        ArgumentNullException.ThrowIfNull(newPriority, nameof(newPriority));

        if (queue is ConcurrentPriorityQueue<TPriority, TElement> concreteQueue)
        {
            return concreteQueue.TryUpdatePriority(oldPriority, newPriority, element);
        }

        // Fallback: remove and re-add
        if (queue.TryDelete(oldPriority))
        {
            if (queue.TryAdd(newPriority, element))
            {
                return true;
            }
            // Try to restore on failure
            queue.TryAdd(oldPriority, element);
        }

        return false;
    }

    /// <summary>
    /// Performs a bulk update operation on elements matching a condition.
    /// </summary>
    /// <typeparam name="TPriority">The type of priority values.</typeparam>
    /// <typeparam name="TElement">The type of elements.</typeparam>
    /// <param name="queue">The priority queue to update.</param>
    /// <param name="updateCondition">Predicate to determine which elements to update.</param>
    /// <param name="priorityTransform">Function to compute new priority.</param>
    /// <param name="elementTransform">Optional function to transform the element.</param>
    /// <returns>The number of successfully updated elements.</returns>
    public static async Task<int> BulkUpdateAsync<TPriority, TElement>(
        this IConcurrentPriorityQueue<TPriority, TElement> queue,
        Func<TPriority, TElement, bool> updateCondition,
        Func<TPriority, TElement, TPriority> priorityTransform,
        Func<TElement, TElement>? elementTransform = null)
    {
        ArgumentNullException.ThrowIfNull(queue, nameof(queue));
        ArgumentNullException.ThrowIfNull(updateCondition, nameof(updateCondition));
        ArgumentNullException.ThrowIfNull(priorityTransform, nameof(priorityTransform));

        elementTransform ??= e => e;

        if (queue is ConcurrentPriorityQueue<TPriority, TElement> concreteQueue)
        {
            // Use the optimized bulk update if available
            var updateStrategy = new PriorityUpdateStrategy<TPriority, TElement>(
                concreteQueue,
                concreteQueue.Comparer,
                null);

            return await updateStrategy.BulkUpdatePrioritiesAsync(
                (priority, element) =>
                {
                    if (updateCondition(priority, element))
                    {
                        var newPriority = priorityTransform(priority, element);
                        var newElement = elementTransform(element);
                        return (newPriority, newElement);
                    }
                    return null;
                }).ConfigureAwait(false);
        }

        // Fallback implementation
        var updates = new List<(TPriority OldPriority, TElement Element, TPriority NewPriority)>();

        // Collect items to update
        if (queue is ConcurrentPriorityQueue<TPriority, TElement> queueWithEnumerator)
        {
            foreach (var (priority, element) in queueWithEnumerator.GetFullEnumerator())
            {
                if (updateCondition(priority, element))
                {
                    var newPriority = priorityTransform(priority, element);
                    updates.Add((priority, element, newPriority));
                }
            }
        }

        // Apply updates
        var successCount = 0;
        foreach (var update in updates)
        {
            if (queue.TryDelete(update.OldPriority))
            {
                var newElement = elementTransform(update.Element);
                if (queue.TryAdd(update.NewPriority, newElement))
                {
                    successCount++;
                }
            }
        }

        return successCount;
    }

    /// <summary>
    /// Converts an enumerable to an async enumerable.
    /// </summary>
    private static async IAsyncEnumerable<T> ToAsyncEnumerable<T>(this IEnumerable<T> source)
    {
        foreach (var item in source)
        {
            yield return item;
        }
        await Task.CompletedTask.ConfigureAwait(false);
    }
}