using System.Collections.Concurrent;
using System.Diagnostics;

namespace lvlup.DataFerry.Buffers
{
    /// <summary>
    /// A concurrent ring buffer with a producer-consumer pattern, enhanced with thread-specific locking and asynchronous task orchestration.
    /// </summary>
    public class ThreadBuffer<T> where T : struct
    {
        public event EventHandler<ThresholdReachedEventArgs>? ThresholdReached;

        [ThreadStatic]
        private static ConcurrentStack<T>? _threadBuffer;
        private static readonly Lock @clearLock = new();
        private readonly int _capacity;
        private int _count = 0;

        public ThreadBuffer(int capacity)
        {
            _capacity = capacity;
        }

        public IEnumerable<T> ExtractItems()
        {
            // Get all active threads
            Process.GetCurrentProcess().Refresh();
            
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
                }
            }

            Interlocked.Exchange(ref _count, 0);
        }

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
                    }
                }
            }

            Interlocked.Exchange(ref _count, 0);
        }

        public int GetCount() => _count;

        private void CheckThresholdAndNotify()
        {
            if (_count >= _capacity)
            {
                // Raise the ThresholdReached event
                ThresholdReached?.Invoke(this, new ThresholdReachedEventArgs(_capacity, _count));
            }
        }
    }

    public class ThresholdReachedEventArgs : EventArgs
    {
        public double ThresholdValue { get; }
        public int ItemCount { get; }

        public ThresholdReachedEventArgs(double thresholdValue, int itemCount)
        {
            ThresholdValue = thresholdValue;
            ItemCount = itemCount;
        }
    }
}