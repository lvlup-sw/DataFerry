using System.Collections.Concurrent;
using System.Diagnostics;

namespace lvlup.DataFerry.Buffers.Contracts;

/// <summary>
/// Represents a bounded, multi-producer, single-consumer (MPSC) thread-safe buffer.
/// </summary>
/// <typeparam name="T">Specifies the type of elements in the buffer. Must be a struct.</typeparam>
public interface IBoundedBuffer<T> where T : struct
{
    /// <summary>
    /// A ManualResetEventSlim used to signal the consumer when the buffer is not empty.
    /// </summary>
    ManualResetEventSlim NotEmptyEvent { get; }

    /// <summary>
    /// Gets the current number of items in the buffer.
    /// </summary>
    int Count { get; }    
    
    /// <summary>
    /// Attempts to add an item to the buffer.
    /// </summary>
    /// <param name="item">The item to add.</param>
    /// <returns>True if the item was added successfully; false if the buffer is full.</returns>
    bool TryAdd(T item);

    /// <summary>
    /// Attempts to remove and return an item from the buffer.
    /// </summary>
    /// <param name="item">When this method returns, contains the item removed from the buffer, if successful; otherwise, the default value of type T.</param>
    /// <returns>True if an item was removed successfully; false if the buffer is empty.</returns>
    bool TryTake(out T item);

    /// <summary>
    /// Removes all items from the buffer and returns them as an IEnumerable.
    /// </summary>
    /// <returns>An IEnumerable containing all items removed from the buffer.</returns>
    IEnumerable<T> FlushItems();

    /// <summary>
    /// Clears the contents of the buffer without returning them.
    /// </summary>
    void Clear();
}