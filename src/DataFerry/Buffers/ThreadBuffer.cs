using System.Collections.Concurrent;
using System.Diagnostics;

namespace lvlup.DataFerry.Buffers;

/// <summary>
/// A concurrent ring buffer with a producer-consumer pattern, enhanced with thread-specific locking and asynchronous task orchestration.
/// </summary>
public class ThreadBuffer<T> where T : struct
{
    /// <summary>
    /// Occurs when the number of items in the buffer reaches or exceeds the capacity.
    /// </summary>
    public event EventHandler<ThresholdReachedEventArgs>? ThresholdReached;

    [ThreadStatic]
    private static ConcurrentStack<T>? _threadBuffer;

    private static readonly Lock @flushLock = new();
    private static readonly Lock @clearLock = new();

    private readonly int _capacity;
    private int _count = 0;

    /// <summary>
    /// Initializes a new instance of the <see cref="ThreadBuffer{T}"/> class with the specified capacity.
    /// </summary>
    /// <param name="capacity">The maximum number of items the buffer can hold.</param>
    public ThreadBuffer(int capacity)
    {
        _capacity = capacity;
    }

    /// <summary>
    /// Adds an item to the buffer.
    /// </summary>
    /// <param name="item">The item to add to the buffer.</param>
    public void Add(T item)
    {
        _threadBuffer ??= [];

        // Check for duplicate on most recent item
        if (_threadBuffer.TryPeek(out T prev) && prev.Equals(item))
        {
            _threadBuffer.TryPop(out _);
        }

        _threadBuffer.Push(item);
        Interlocked.Increment(ref _count);

        // Check threshold and raise event if necessary
        CheckThresholdAndNotify();
    }

    /// <summary>
    /// Flushes all items from the buffer and returns them as an enumerable.
    /// </summary>
    /// <returns>An enumerable containing all items flushed from the buffer.</returns>
    public IEnumerable<T> FlushItems()
    {
        // Get all active threads
        Process.GetCurrentProcess().Refresh();

        lock (@flushLock)
        {
            foreach (var _ in Process.GetCurrentProcess().Threads)
            {
                // Access the thread-local buffer
                var buffer = _threadBuffer;

                if (buffer is not null)
                {
                    while (buffer.TryPop(out var item))
                    {
                        yield return item;
                    }

                    _threadBuffer = default;
                }
            }
        }

        Interlocked.Exchange(ref _count, 0);
    }

    /// <summary>
    /// Clears all items from the buffer.
    /// </summary>
    public void Clear()
    {
        Process.GetCurrentProcess().Refresh();

        lock (@clearLock)
        {
            foreach (var _ in Process.GetCurrentProcess().Threads)
            {
                // Access the thread-local buffer
                var buffer = _threadBuffer;

                if (buffer is not null)
                {
                    while (buffer.TryPop(out var _));

                    _threadBuffer = default;
                }
            }
        }

        Interlocked.Exchange(ref _count, 0);
    }

    /// <summary>
    /// Gets the current number of items in the buffer.
    /// </summary>
    /// <returns>The number of items in the buffer.</returns>
    public int GetCount() => _count;

    /// <summary>
    /// Checks if the threshold has been reached and raises the <see cref="ThresholdReached"/> event if necessary.
    /// </summary>
    private void CheckThresholdAndNotify()
    {
        if (_count >= _capacity)
        {
            // Raise the ThresholdReached event
            ThresholdReached?.Invoke(this, new ThresholdReachedEventArgs(_capacity, _count));
        }
    }
}

/// <summary>
/// Provides data for the <see cref="ThreadBuffer{T}.ThresholdReached"/> event.
/// </summary>
public class ThresholdReachedEventArgs : EventArgs
{
    /// <summary>
    /// Gets the threshold value that was reached.
    /// </summary>
    public double ThresholdValue { get; }

    /// <summary>
    /// Gets the current number of items in the buffer.
    /// </summary>
    public int ItemCount { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="ThresholdReachedEventArgs"/> class.
    /// </summary>
    /// <param name="thresholdValue">The threshold value that was reached.</param>
    /// <param name="itemCount">The current number of items in the buffer.</param>
    public ThresholdReachedEventArgs(double thresholdValue, int itemCount)
    {
        ThresholdValue = thresholdValue;
        ItemCount = itemCount;
    }
}