namespace lvlup.DataFerry.Collections.Abstractions
{
    /// <summary>
    /// A contract for interacting with a concurrent PriorityQueue implemented as a SkipList.
    /// </summary>
    public interface IConcurrentPriorityQueue<TPriority, TElement>
    {
        /// <summary>
        /// Provides a lock-free enumeration of the prioritys in the ConcurrentPriorityQueue. 
        /// Note that this enumeration provides READ COMMITTED semantics.
        /// </summary>
        /// <returns>An enumerator that represents the prioritys in the ConcurrentPriorityQueue.</returns>
        IEnumerator<TPriority> GetEnumerator();

        /// <summary>
        /// Determines whether the ConcurrentPriorityQueue contains the specified priority.
        /// </summary>
        /// <param name="priority">The frequency to locate.</param>
        /// <returns>true if the ConcurrentPriorityQueue contains the specified priority; otherwise, false.</returns>
        bool ContainsPriority(TPriority priority);

        /// <summary>
        /// Determines whether the ConcurrentPriorityQueue contains the specified element.
        /// </summary>
        /// <param name="element">The frequency to locate.</param>
        /// <returns>true if the ConcurrentPriorityQueue contains the specified element; otherwise, false.</returns>
        bool ContainsElement(TElement element);

        /// <summary>
        /// Attempts to get the element associated with the specified priority from the ConcurrentPriorityQueue.
        /// </summary>
        /// <param name="priority">The priority to locate.</param>
        /// <param name="element">When this method returns, contains the element associated with the specified priority, 
        /// if the priority is found; otherwise, the default element for the type of the <paramref name="element" /> parameter. 
        /// This parameter is passed uninitialized.</param>
        /// <returns>true if the ConcurrentPriorityQueue contains an element with the specified priority; otherwise, false.</returns>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="priority"/> is null.</exception>
        bool TryGetElement(TPriority priority, out TElement element);

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
        /// Attempts to remove the specified priority from the ConcurrentPriorityQueue.
        /// </summary>
        /// <param name="priority">The priority to remove.</param>
        /// <returns>true if the priority was removed successfully; otherwise, false.</returns>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="priority"/> is null.</exception>
        bool TryRemoveItemWithPriority(TPriority priority);

        /// <summary>
        /// Attempts to remove all the items with the specified priorities from the ConcurrentPriorityQueue.
        /// </summary>
        /// <param name="priority">The priority to remove.</param>
        /// <returns>true if all items were removed successfully; otherwise, false.</returns>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="priority"/> is null.</exception>
        bool TryRemoveAllItemsWithPriority(TPriority priority);

        /// <summary>
        /// Attempts to remove the specified element from the ConcurrentPriorityQueue.
        /// </summary>
        /// <param name="priority">The element to remove.</param>
        /// <returns>true if the element was removed successfully; otherwise, false.</returns>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="element"/> is null.</exception>
        bool TryRemoveElement(TElement element);

        /// <summary>
        /// Attempts to remove the element with the minimum priority from the ConcurrentPriorityQueue and return its element.
        /// </summary>
        /// <param name="item">When this method returns, contains the element of the removed element, 
        /// if the ConcurrentPriorityQueue is not empty; otherwise, the default element for the type of the <paramref name="item"/> parameter. 
        /// This parameter is passed uninitialized.</param>
        /// <returns>true if an element was removed successfully; otherwise, false.</returns>
        bool TryRemoveMin(out TElement item);


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
        /// Gets the number of nodes in the SkipList.
        /// </summary>
        int GetCount();
    }
}
