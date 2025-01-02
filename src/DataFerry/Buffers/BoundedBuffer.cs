using lvlup.DataFerry.Buffers.Contracts;

namespace lvlup.DataFerry.Buffers;

/// <inheritdoc/>
public sealed class BoundedBuffer<T> : IBoundedBuffer<T> where T : struct
{
    // Backing structures
    /// <summary>
    /// The underlying array used as a circular buffer to store the items.
    /// </summary>
    private readonly T[] _buffer;

    /// <summary>
    /// The maximum number of items the buffer can hold.
    /// </summary>
    private readonly int _capacity;

    /// <summary>
    /// Lock object used to synchronize access between multiple producers.
    /// </summary>
    private readonly Lock @producerLock = new();
    
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
    
    /// <inheritdoc/>
    public ManualResetEventSlim NotEmptyEvent { get; } = new(false);

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
    
    /// <inheritdoc/>
    public int Count => Interlocked.CompareExchange(ref _count, 0, 0);

    /// <inheritdoc/>
    public void Add(T item)
    {
        lock (@producerLock)
        {
            // Wait if the buffer is full
            while (_count == _capacity)
            {   // Release the lock
                Monitor.Wait(@producerLock);
            }
            
            _buffer[_head] = item;
            _head = (_head + 1) % _capacity;
            _count++;
            
            // Signal that the buffer is not empty
            Monitor.PulseAll(@producerLock);
            // If buffer has items, signal the consumer
            if (_count == 1) NotEmptyEvent.Set();
        }    
    }
    
    /// <inheritdoc/>
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
            if (_count == 1) NotEmptyEvent.Set();
            return true;
        }
    }

    /// <inheritdoc/>
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
                NotEmptyEvent.Wait();
            }
        }

        item = _buffer[_tail];
        _tail = (_tail + 1) % _capacity;

        // Decrement _count
        Interlocked.Decrement(ref _count);

        // Reset if buffer is empty
        if (_count == 0) NotEmptyEvent.Reset();
        return true;
    }

    /// <inheritdoc/>
    public IEnumerable<T> FlushItems()
    {
        NotEmptyEvent.Wait();

        // No other consumers, so reading is safe without a lock
        while (_count > 0)
        {
            yield return _buffer[_tail];
            _tail = (_tail + 1) % _capacity;

            // Decrement _count
            Interlocked.Decrement(ref _count);
        }

        NotEmptyEvent.Reset();
    }

    /// <inheritdoc/>
    public void Clear()
    {
        NotEmptyEvent.Wait();

        // No other consumers, so reading is safe without a lock
        // Effectively resets the buffer
        _tail = _head;

        // Update count
        Interlocked.Exchange(ref _count, 0);

        NotEmptyEvent.Reset();
    }
}