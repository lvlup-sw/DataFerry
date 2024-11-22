namespace lvlup.DataFerry.Collections.Contracts
{
    /// <summary>
    /// A contract for interacting with a non-blocking doubly linked list.
    /// </summary>
    /// <typeparam name="T">The type of elements in the list.</typeparam>
    public interface IConcurrentLinkedList<T>
    {
        /// <summary>
        /// Tries to insert a node with the specified key into the list.
        /// </summary>
        /// <param name="key">The key of the node to insert.</param>
        /// <returns>true if the node was inserted successfully; otherwise, false.</returns>
        bool TryInsert(T key);

        /// <summary>
        /// Tries to remove the node with the specified key from the list.
        /// </summary>
        /// <param name="key">The key of the node to remove.</param>
        /// <returns>true if the node was removed successfully; otherwise, false.</returns>
        bool TryRemove(T key);

        /// <summary>
        /// Determines whether the list contains a node with the specified key.
        /// </summary>
        /// <param name="key">The key to locate in the list.</param>
        /// <returns>true if the list contains a node with the specified key; otherwise, false.</returns>
        bool Contains(T key);

        /// <summary>
        /// Copies the elements of the list to an array, starting at the specified index.
        /// </summary>
        /// <param name="array">The one-dimensional array that is the destination of the elements copied from the list.</param>
        /// <param name="index">The zero-based index in array at which copying begins.</param>
        /// <exception cref="ArgumentNullException">array is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">index is less than 0 or index is greater than or equal to the length of array.</exception>
        /// <exception cref="ArgumentException">The number of elements in the list is greater than the available space from index to the end of the destination array.</exception>
        void CopyTo(T[] array, int index);

        /// <summary>
        /// Finds the first node that contains the specified value.
        /// </summary>
        /// <param name="value">The value to locate in the list.</param>
        /// <returns>The first node that contains the specified value, if found; otherwise, null.</returns>
        Node<T>? Find(T value);

        /// <summary>
        /// Gets the number of nodes in the list.
        /// </summary>
        int Count { get; }
    }
}
