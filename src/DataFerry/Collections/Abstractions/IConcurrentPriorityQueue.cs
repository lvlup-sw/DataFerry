namespace lvlup.DataFerry.Collections.Abstractions
{
    public interface IConcurrentPriorityQueue<TPriority, TElement>
    {
        /// <summary>
        /// Provides a lock-free enumeration of the keys in the ConcurrentPriorityQueue. 
        /// Note that this enumeration provides READ COMMITTED semantics.
        /// </summary>
        /// <returns>An enumerator that represents the keys in the ConcurrentPriorityQueue.</returns>
        IEnumerator<TPriority> GetEnumerator();

        /// <summary>
        /// Determines whether the ConcurrentPriorityQueue contains the specified key.
        /// </summary>
        /// <param name="key">The key to locate.</param>
        /// <returns>true if the ConcurrentPriorityQueue contains the specified key; otherwise, false.</returns>
        bool Contains(TPriority key);

        /// <summary>
        /// Attempts to get the value associated with the specified key from the ConcurrentPriorityQueue.
        /// </summary>
        /// <param name="key">The key to locate.</param>
        /// <param name="value">When this method returns, contains the value associated with the specified key, 
        /// if the key is found; otherwise, the default value for the type of the <paramref name="value" /> parameter. 
        /// This parameter is passed uninitialized.</param>
        /// <returns>true if the ConcurrentPriorityQueue contains an element with the specified key; otherwise, false.</returns>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="key"/> is null.</exception>
        bool TryGetValue(TPriority key, out TElement value);

        /// <summary>
        /// Attempts to add the specified key and value to the ConcurrentPriorityQueue.
        /// </summary>
        /// <param name="key">The key of the element to add.</param>
        /// <param name="value">The value of the element to add.</param>
        /// <returns>true if the key/value pair was added to the ConcurrentPriorityQueue successfully; otherwise, false.</returns>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="key"/> is null.</exception>
        bool TryAdd(TPriority key, TElement value);

        /// <summary>
        /// Updates the value associated with the specified key.
        /// </summary>
        /// <param name="key">The key of the element to update.</param>
        /// <param name="value">The new value.</param>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="key"/> is null.</exception>
        /// <exception cref="ArgumentException">Thrown if the key does not exist or is being deleted.</exception>
        void Update(TPriority key, TElement value);

        /// <summary>
        /// Updates the value associated with the specified key using the provided update function.
        /// </summary>
        /// <param name="key">The key of the element to update.</param>
        /// <param name="updateFunction">The function used to generate the new value.</param>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="key"/> or <paramref name="updateFunction"/> is null.</exception>
        /// <exception cref="ArgumentException">Thrown if the key does not exist or is being deleted.</exception>
        void Update(TPriority key, Func<TPriority, TElement, TElement> updateFunction);

        /// <summary>
        /// Attempts to remove the specified key from the ConcurrentPriorityQueue.
        /// </summary>
        /// <param name="key">The key to remove.</param>
        /// <returns>true if the key was removed successfully; otherwise, false.</returns>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="key"/> is null.</exception>
        bool TryRemove(TPriority key);

        /// <summary>
        /// Attempts to remove the element with the minimum priority from the ConcurrentPriorityQueue and return its value.
        /// </summary>
        /// <param name="element">When this method returns, contains the value of the removed element, 
        /// if the ConcurrentPriorityQueue is not empty; otherwise, the default value for the type of the <paramref name="element"/> parameter. 
        /// This parameter is passed uninitialized.</param>
        /// <returns>true if an element was removed successfully; otherwise, false.</returns>
        bool TryRemoveMin(out TElement element);

        /// <summary>
        /// Gets the number of nodes in the SkipList.
        /// </summary>
        int GetCount();
    }
}
