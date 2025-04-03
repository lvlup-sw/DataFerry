// ============================================================================
// <copyright file="IConcurrentPriorityQueue.cs" company="Level Up Software">
// Copyright (c) Level Up Software. All rights reserved.
// </copyright>
// ============================================================================

using System.Diagnostics.CodeAnalysis;

namespace lvlup.DataFerry.Concurrency.Contracts;

/// <summary>
/// Defines a thread-safe priority queue based on a SkipList structure
/// allowing concurrent additions, deletions, and updates.
/// </summary>
/// <typeparam name="TPriority">The type used for priority values.</typeparam>
/// <typeparam name="TElement">The type of the elements stored in the queue.</typeparam>
/// <remarks>
/// Implementations typically use fine-grained locking and techniques like logical deletion
/// for efficient concurrent operations with expected logarithmic time complexity for major operations.
/// Duplicate priorities are generally supported via internal mechanisms (like sequence numbers).
/// </remarks>
public interface IConcurrentPriorityQueue<TPriority, TElement>
{
    /// <summary>
    /// Attempts to add the specified priority and element pair to the queue.
    /// </summary>
    /// <param name="priority">The priority of the element to add.</param>
    /// <param name="element">The element to add.</param>
    /// <remarks>
    /// Duplicate priorities are allowed. The internal ordering for equal priorities
    /// typically depends on insertion order (sequence number).
    /// If the queue has a maximum size and is full, adding a new item may trigger
    /// the removal of a low-priority item.
    /// </remarks>
    /// <returns><c>true</c> if the priority/element pair was added successfully; otherwise, <c>false</c> (which is uncommon for typical SkipList implementations unless specific preconditions fail).</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="priority"/> or <paramref name="element"/> is null (if nulls are disallowed by the implementation).</exception>
    /// <exception cref="ArgumentException">May be thrown if <paramref name="priority"/> is deemed invalid by the comparer.</exception>
    bool TryAdd(TPriority priority, TElement element);

    /// <summary>
    /// Attempts to remove the first valid node found with the specified priority from the queue.
    /// </summary>
    /// <param name="priority">The priority to remove.</param>
    /// <returns><c>true</c> if a node with the specified priority was found and successfully removed; otherwise, <c>false</c>.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="priority"/> is null.</exception>
    bool TryDelete(TPriority priority);

    /// <summary>
    /// Attempts to remove the element with the minimum priority (and lowest sequence number in case of ties)
    /// from the queue and returns its element.
    /// </summary>
    /// <param name="element">When this method returns, contains the element value of the removed item
    /// if the operation was successful; otherwise, the default value for <typeparamref name="TElement"/>.
    /// This parameter is passed uninitialized.</param>
    /// <returns><c>true</c> if an element was successfully removed; otherwise, <c>false</c> (e.g., if the queue was empty).</returns>
    bool TryDeleteAbsoluteMin([MaybeNullWhen(false)] out TElement element);

    /// <summary>
    /// Attempts to probabilistically remove an element with a priority near the minimum from the queue
    /// and returns its element, aiming to reduce contention on the absolute minimum node.
    /// </summary>
    /// <remarks>
    /// This method utilizes the SprayList algorithm, performing a random walk to select a candidate node
    /// typically from among the items with the lowest priorities. This is useful in high-contention scenarios
    /// where <see cref="TryDeleteAbsoluteMin"/> becomes a bottleneck. The exact range of items considered
    /// depends on internal tuning parameters (like OffsetM).
    /// </remarks>
    /// <param name="element">When this method returns, contains the element value of the removed item
    /// if the operation was successful; otherwise, the default value for <typeparamref name="TElement"/>.
    /// This parameter is passed uninitialized.</param>
    /// <param name="retryProbability">Optional. The probability (0.0 to 1.0) that the spray operation is retried recursively if the first attempt fails due to transient conditions (e.g., concurrent modification). Defaults to 1.0 (always retry first failure).</param>
    /// <returns><c>true</c> if an element was successfully removed; otherwise, <c>false</c>.</returns>
    bool TryDeleteMin([MaybeNullWhen(false)] out TElement element, double retryProbability = 1.0);

    /// <summary>
    /// Attempts to retrieve a sample of approximately <paramref name="sampleSize"/> distinct items
    /// with priorities near the minimum using a parallel SprayList algorithm.
    /// </summary>
    /// <param name="sampleSize">The desired number of distinct items (k) in the sample.</param>
    /// <param name="maxAttemptsMultiplier">Optional. Multiplier for <paramref name="sampleSize"/> to determine the maximum
    /// number of parallel spray attempts performed internally to find distinct, valid nodes. Defaults to 3.</param>
    /// <returns>
    /// An <see cref="IEnumerable{T}"/> sequence of (<typeparamref name="TPriority"/> priority, <typeparamref name="TElement"/> element) tuples
    /// representing the sampled items found near the minimum priority. The actual number of items returned may be less than
    /// <paramref name="sampleSize"/> if fewer distinct, valid nodes were found within the internal attempt limit.
    /// </returns>
    /// <remarks>
    /// <para>
    /// This method performs multiple probabilistic 'spray' searches, in parallel using PLINQ,
    /// to find candidate nodes near the minimum priority while aiming to reduce contention.
    /// </para>
    /// <para>
    /// It filters results to include only distinct, valid (inserted and not logically deleted) nodes.
    /// The exact items returned are probabilistic and depend on the spray walk outcomes; they are not
    /// guaranteed to be the absolute lowest priority items in the queue.
    /// </para>
    /// <para>
    /// The results typically represent a snapshot based on the state of the queue during execution.
    /// Implementations commonly materialize this into a List before returning.
    /// </para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if <paramref name="sampleSize"/> or <paramref name="maxAttemptsMultiplier"/> is less than 1.</exception>
    IEnumerable<(TPriority priority, TElement element)> SampleNearMin(int sampleSize, int maxAttemptsMultiplier = 3);

    /// <summary>
    /// Attempts to update the element of the first valid node found with the specified priority.
    /// </summary>
    /// <param name="priority">The priority of the element to find and update.</param>
    /// <param name="element">The new element value.</param>
    /// <returns><c>true</c> if a node with the specified priority was found and successfully updated; <c>false</c> if the node was not found or was deleted concurrently before the update could complete.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="priority"/> or <paramref name="element"/> is null (if nulls are disallowed).</exception>
    bool Update(TPriority priority, TElement element);

    /// <summary>
    /// Attempts to update the element of the first valid node found with the specified priority using the provided update function.
    /// </summary>
    /// <param name="priority">The priority of the element to find and update.</param>
    /// <param name="updateFunction">A function that takes the priority and current element, and returns the new element value.</param>
    /// <returns><c>true</c> if a node with the specified priority was found and successfully updated; <c>false</c> if the node was not found or was deleted concurrently before the update could complete.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="priority"/> or <paramref name="updateFunction"/> is null.</exception>
    bool Update(TPriority priority, Func<TPriority, TElement, TElement> updateFunction);

    /// <summary>
    /// Determines whether the queue contains a valid (inserted and not deleted) node with the specified priority.
    /// </summary>
    /// <param name="priority">The priority to locate.</param>
    /// <returns><c>true</c> if the queue contains a valid node with the specified priority; otherwise, <c>false</c>.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="priority"/> is null.</exception>
    bool ContainsPriority(TPriority priority);

    /// <summary>
    /// Gets the approximate number of elements contained in the queue.
    /// </summary>
    /// <returns>An integer representing the number of elements at a moment in time.</returns>
    /// <remarks>
    /// Returns a moment-in-time count using a thread-safe, lock-free atomic read.
    /// </remarks>
    int GetCount();

    /// <summary>
    /// Returns an enumerator that iterates through the priorities in the queue (typically in ascending priority order).
    /// </summary>
    /// <returns>An enumerator for the priorities in the queue.</returns>
    /// <remarks>
    /// The enumeration typically provides lock-free, "Read Committed" or snapshot semantics, reflecting the state
    /// of the queue at a point in time or observing only committed insertions/deletions.
    /// The exact consistency model depends on the implementation.
    /// </remarks>
    IEnumerator<TPriority> GetEnumerator();
}