using System.Diagnostics.CodeAnalysis;

namespace lvlup.DataFerry.Collections.Contracts;

/// <summary>
/// A contract for interacting with a concurrent PriorityQueue implemented as a SkipList.
/// </summary>
public interface IConcurrentPriorityQueue<TPriority, TElement>
{
    /// <summary>
    /// Attempts to add the specified priority and element to the ConcurrentPriorityQueue.
    /// </summary>
    /// <param name="priority">The priority of the element to add.</param>
    /// <param name="element">The element of the element to add.</param>
    /// <remarks>If allowDuplicatePriorities is true, nodes with the same <see cref="TPriority"/> but unique <see cref="TElement"/> will be added.</remarks>
    /// <returns>true if the priority/element pair was added to the ConcurrentPriorityQueue successfully; otherwise, false.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="priority"/> is null.</exception>
    bool TryAdd(TPriority priority, TElement element);

    /// <summary>
    /// Attempts to remove the element with the minimum priority from the ConcurrentPriorityQueue and return its element.
    /// </summary>
    /// <param name="item">When this method returns, contains the element of the removed element, 
    /// if the ConcurrentPriorityQueue is not empty; otherwise, the default element for the type of the <paramref name="item"/> parameter. 
    /// This parameter is passed uninitialized.</param>
    /// <returns>true if an element was removed successfully; otherwise, false.</returns>
    bool TryDeleteMin(out TElement item);

    /// <summary>
    /// Attempts to probabilistically remove the element with the minimum priority from the ConcurrentPriorityQueue and return its element.
    /// </summary>
    /// <remarks>
    /// This method utilizes the SprayList algorithm to emulate a uniform choice among the O(p*log^3(p)) highest priority items.
    /// This is typically useful in high-volume scenarios where throughput is limited by contention on the minimal node.
    /// </remarks>
    /// <param name="item">When this method returns, contains the element of the removed element, 
    /// if the ConcurrentPriorityQueue is not empty; otherwise, the default element for the type of the <paramref name="item"/> parameter. 
    /// This parameter is passed uninitialized.</param>
    /// <param name="retryProbability">The probability a Spray is retried if the initial operation fails.</param>
    /// <returns>true if an element was removed successfully; otherwise, false. <paramref name="item"/> may be null if false.</returns>
    bool TryDeleteMinProbabilistically([MaybeNull] out TElement item, double retryProbability);

    /// <summary>
    /// Attempts to remove the specified priority from the ConcurrentPriorityQueue.
    /// </summary>
    /// <param name="priority">The priority to remove.</param>
    /// <returns>true if the priority was removed successfully; otherwise, false.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="priority"/> is null.</exception>
    bool TryDelete(TPriority priority);

    /// <summary>
    /// Updates the element associated with the specified priority.
    /// </summary>
    /// <param name="priority">The priority of the element to update.</param>
    /// <param name="element">The new element.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="priority"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown if the priority does not exist or is being deleted.</exception>
    void Update(TPriority priority, TElement element);

    /// <summary>
    /// Updates the element associated with the specified priority using the provided update function.
    /// </summary>
    /// <param name="priority">The priority of the element to update.</param>
    /// <param name="updateFunction">The function used to generate the new element.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="priority"/> or <paramref name="updateFunction"/> is null.</exception>
    /// <exception cref="ArgumentException">Thrown if the priority does not exist or is being deleted.</exception>
    void Update(TPriority priority, Func<TPriority, TElement, TElement> updateFunction);

    /// <summary>
    /// Determines whether the ConcurrentPriorityQueue contains the specified priority.
    /// </summary>
    /// <param name="priority">The frequency to locate.</param>
    /// <returns>true if the ConcurrentPriorityQueue contains the specified priority; otherwise, false.</returns>
    bool ContainsPriority(TPriority priority);

    /// <summary>
    /// Gets the number of nodes in the SkipList.
    /// </summary>
    /// <remarks>
    /// Returns a moment-in-time count. Provides a thread-safe, lock-free atomic read.
    /// </remarks>
    int GetCount();

    /// <summary>
    /// Removes all nodes from the SkipList.
    /// </summary>
    void Clear();

    /// <summary>
    /// Provides a lock-free enumeration of the prioritys in the ConcurrentPriorityQueue. 
    /// Note that this enumeration provides READ COMMITTED semantics.
    /// </summary>
    /// <returns>An enumerator that represents the prioritys in the ConcurrentPriorityQueue.</returns>
    IEnumerator<TPriority> GetEnumerator();
}