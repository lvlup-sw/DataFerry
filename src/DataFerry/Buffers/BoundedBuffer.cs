using System.Collections.Concurrent;
using System.Diagnostics;

namespace lvlup.DataFerry.Buffers;

/// <summary>
/// Represents a bounded, multi-producer, single-consumer (MPSC) thread-safe buffer.
/// </summary>
/// <typeparam name="T">Specifies the type of elements in the buffer. Must be a struct.</typeparam>
public class BoundedBuffer<T> where T : struct
{
    /// <summary>
    /// The underlying array used as a circular buffer to store the items.
    /// </summary>
    private readonly T[] _buffer;

    /// <summary>
    /// The maximum number of items the buffer can hold.
    /// </summary>
    private readonly int _capacity;

    /// <summary>
    ///  Lock object used to synchronize access between multiple producers.
    /// </summary>
    private readonly Lock @producerLock = new();

    /// <summary>
    /// A ManualResetEventSlim used to signal the consumer when the buffer is not empty.
    /// </summary>
    private readonly ManualResetEventSlim _notEmptyEvent = new(false);

    // Pointers
    /// <summary>
    /// Index of the next position in the buffer to write to (used by producers).
    /// </summary>
    private int _head;

    /// <summary>
    /// Index of the next position in the buffer to read from (used by the consumer).
    /// </summary>
    private int _tail;

    /// <summary>
    /// The current number of items in the buffer.
    /// </summary>
    private int _count;
    
    /// <summary>
    /// The number of times the consumer will spin before waiting on the _notEmptyEvent.
    /// </summary>
    private readonly int _spinCountBeforeWait;

    /// <summary>
    /// Initializes a new instance of the <see cref="BoundedBuffer{T}"/> class.
    /// </summary>
    /// <param name="capacity">The maximum number of items the buffer can hold.</param>
    /// <param name="spinCountBeforeWait">The number of times the consumer will spin before waiting on the _notEmptyEvent. Defaults to 10.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if capacity is less than or equal to 0, or if spinCountBeforeWait is less than 0.</exception>    
    public BoundedBuffer(int capacity, int spinCountBeforeWait = 10)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(capacity, 0, nameof(capacity));
        ArgumentOutOfRangeException.ThrowIfNegative(spinCountBeforeWait, nameof(spinCountBeforeWait));

        _capacity = capacity;
        _spinCountBeforeWait = spinCountBeforeWait;
        _buffer = new T[capacity];
        _head = 0;
        _tail = 0;
        _count = 0;
    }

    /// <summary>
    /// Attempts to add an item to the buffer.
    /// </summary>
    /// <param name="item">The item to add.</param>
    /// <returns>True if the item was added successfully; false if the buffer is full.</returns>
    public bool TryAdd(T item)
    {
        lock (@producerLock)
        {
            // Check if buffer is full
            if (_count == _capacity) return false;

            _buffer[_head] = item;
            _head = (_head + 1) % _capacity;
            _count++;

            // If buffer has items, signal the consumer
            if (_count == 1) _notEmptyEvent.Set();
            return true;
        }
    }

    /// <summary>
    /// Adds an item to the buffer. If the buffer is full, the calling thread will block until space is available.
    /// </summary>
    /// <param name="item">The item to add.</param>
    public void Add(T item)
    {
        lock (@producerLock)
        {
            // Wait if buffer is full
            while (_count == _capacity)
            {
                Monitor.Wait(@producerLock);
            }

            _buffer[_head] = item;
            _head = (_head + 1) % _capacity;
            _count++;

            // If buffer has items, signal the consumer
            if (_count == 1) _notEmptyEvent.Set();
            Monitor.PulseAll(@producerLock);
        }
    }

    /// <summary>
    /// Attempts to remove and return an item from the buffer.
    /// </summary>
    /// <param name="item">When this method returns, contains the item removed from the buffer, if successful; otherwise, the default value of type T.</param>
    /// <returns>True if an item was removed successfully; false if the buffer is empty.</returns>
    public bool TryTake(out T item)
    {
        item = default;

        // Spin for a little while before waiting
        var spinner = new SpinWait();
        while (_count == 0)
        {
            if (spinner.Count < _spinCountBeforeWait)
            {
                spinner.SpinOnce();
            }
            else
            {
                _notEmptyEvent.Wait();
            }
        }

        item = _buffer[_tail];
        _tail = (_tail + 1) % _capacity;

        // Decrement _count and check if we need to signal producers
        if (Interlocked.Decrement(ref _count) == _capacity - 1)
        {
            // Prevent re-ordering
            Thread.MemoryBarrier();
            lock (@producerLock)
            {
                Monitor.PulseAll(@producerLock);
            }
        }

        // Reset if buffer is empty
        if (_count == 0) _notEmptyEvent.Reset();
        return true;
    }

    /// <summary>
    /// Removes all items from the buffer and returns them as an IEnumerable.
    /// </summary>
    /// <returns>An IEnumerable containing all items removed from the buffer.</returns>
    public IEnumerable<T> FlushItems()
    {
        _notEmptyEvent.Wait();

        // No other consumers, so reading is safe without a lock
        while (_count > 0)
        {
            yield return _buffer[_tail];
            _tail = (_tail + 1) % _capacity;

            // Decrement _count and check if we need to signal producers
            if (Interlocked.Decrement(ref _count) == _capacity - 1)
            {
                // Prevent re-ordering
                Thread.MemoryBarrier();
                lock (@producerLock)
                {
                    Monitor.PulseAll(@producerLock);
                }
            }
        }

        _notEmptyEvent.Reset();
    }

    /// <summary>
    /// Clears the contents of the buffer without returning them.
    /// </summary>
    public void Clear()
    {
        _notEmptyEvent.Wait();

        // No other consumers, so reading is safe without a lock
        _tail = _head; // Effectively resets the buffer
        if (Interlocked.Exchange(ref _count, 0) == _capacity - 1)
        {
            // Prevent re-ordering
            Thread.MemoryBarrier();
            lock (@producerLock)
            {
                Monitor.PulseAll(@producerLock);
            }
        }

        _notEmptyEvent.Reset();
    }
}