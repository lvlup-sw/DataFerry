using System.Collections.Concurrent;
using System.Collections;
using System.Threading.Channels;
using System.Diagnostics;

namespace lvlup.DataFerry.Collections
{
    public class ConcurrentPriorityQueue<TElement, TPriority> : IProducerConsumerCollection<TElement>
    {
        private readonly PriorityQueue<TElement, TPriority> _queue;
        private readonly ConcurrentQueue<int> _queueLengthHistory;
        private readonly Timer _batchTimer;

        // Primitives
        private readonly double _lockWaitTimeThreshold;
        private readonly int _queueLengthThreshold;
        private readonly int _queueLengthHistoryWindowSize;
        private readonly int _queueCapacity;
        private readonly int _contentionIndicatorThreshold = 10;
        private int _contentionIndicators;
        private int _stripeCount;

        // Channels
        private readonly Channel<bool> _contentionChannel;

        // Locks/Semaphores
        private Lock[] @stripes;
        private readonly Lock @syncLock = new();
        private readonly SemaphoreSlim _batchSemaphore = new(1, 1);

        [ThreadStatic]
        private static ConcurrentBag<TElement>? _threadLocalBuffer;

        public ConcurrentPriorityQueue(
            IComparer<TPriority> comparer, 
            int capacity = 100,
            int initialStripeCount = 10,
            int batchInterval = 100, 
            double contentionThreshold = 10.0, 
            int contentionSize = 1000, 
            int contentionWindowSize = 10)
        {
            _queue = new(comparer);
            _queueLengthHistory = new();
            _queueCapacity = capacity;
            _stripeCount = initialStripeCount;
            _lockWaitTimeThreshold = contentionThreshold;
            _queueLengthThreshold = contentionSize;
            _queueLengthHistoryWindowSize = contentionWindowSize;
            _contentionChannel = Channel.CreateUnbounded<bool>();
            @stripes = new Lock[_stripeCount];
            @stripes = Enumerable.Range(0, _stripeCount)
                .Select(_ => new Lock())
                .ToArray();
            _batchTimer = new Timer(
                async s => await BatchProcessItemsJob(),
                default,
                TimeSpan.FromMilliseconds(batchInterval),
                TimeSpan.FromMilliseconds(batchInterval));

            _ = Task.Run(MonitorContentionAsync);
        }

        private async Task BatchProcessItemsJob()
        {
            // To avoid resource wastage from concurrent jobs in multiple instances, we use a Semaphore to serialize 
            // cleanup execution. This mitigates CPU-intensive operations. User-initiated eviction is still allowed.
            // A Semaphore is preferred over a traditional lock to prevent thread starvation.

            await _batchSemaphore.WaitAsync().ConfigureAwait(false);
            try
            {
                BatchProcessItems();
            }
            finally
            {
                _batchSemaphore.Release();
            }
        }

        private void BatchProcessItems()
        {
            if (!Monitor.TryEnter(_batchTimer)) return;

            try
            {
                // Gather updates from thread-local buffers
                var allUpdates = _threadLocalBuffer?.ToArray();
                _threadLocalBuffer?.Clear();

                if (allUpdates is null || !allUpdates.Any()) return;

                // Process the batch in parallel
                Parallel.ForEach(allUpdates, item => { InternalTryAdd(item); });

                if (_contentionIndicators > _contentionIndicatorThreshold)
                    _contentionChannel.Writer.TryWrite(true);

                if (_contentionIndicators > 0)
                    Interlocked.Decrement(ref _contentionIndicators);
            }
            finally
            {
                Monitor.Exit(_batchTimer);
            }
        }

        private async Task MonitorContentionAsync()
        {
            while (await _contentionChannel.Reader.WaitToReadAsync())
            {
                if (_contentionChannel.Reader.TryRead(out _))
                {
                    if (IsContentionDetected())
                    {
                        ResizeStripeBuffer();
                    }
                }
            }
        }

        private bool IsContentionDetected()
        {
            // Lock wait time (average over multiple attempts across stripes)
            double averageLockWaitTime = Enumerable.Range(0, 3)
                .Select(_ =>
                {
                    int stripeIndex = GetStripeIndexForCurrentThread();
                    var stopwatch = Stopwatch.StartNew();
                    lock (@stripes[stripeIndex]) { }
                    return stopwatch.ElapsedMilliseconds;
                })
                .Average();

            // Queue length (using a simple moving average)
            int queueLength = _queue.Count;
            _queueLengthHistory.Enqueue(queueLength);

            if (_queueLengthHistory.Count > _queueLengthHistoryWindowSize)
            {
                _queueLengthHistory.TryDequeue(out _);
            }

            double averageQueueLength = _queueLengthHistory.Average();

            // Is there contention?
            return averageLockWaitTime > _lockWaitTimeThreshold 
                || averageQueueLength > _queueLengthThreshold;
        }

        private int GetStripeIndexForCurrentThread()
        {
            int threadId = Environment.CurrentManagedThreadId;
            return threadId % _stripeCount;
        }

        private void ResizeStripeBuffer()
        {
            // Determine the new number of stripes
            int newStripeCount = _stripeCount * 2;

            // Create a new array of stripes
            var newStripes = Enumerable.Range(0, newStripeCount)
                .Select(_ => new Lock())
                .ToArray();

            // Update the stripe count and stripes array
            _stripeCount = newStripeCount;
            @stripes = newStripes;
        }

        private TPriority DeterminePriority(TElement item) => default!;

        public int Count => _queue.UnorderedItems.Skip(0).Count();

        public bool IsSynchronized => true;

#pragma warning disable CS9216
        public object SyncRoot => @syncLock;
#pragma warning restore CS9216

        public void CopyTo(TElement[] array, int index)
        {
            throw new NotImplementedException();
        }
        public IEnumerator<TElement> GetEnumerator()
        {
            throw new NotImplementedException();
        }
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public bool TryAdd(TElement item, TPriority priority)
        {
            throw new NotImplementedException();
        }

        public bool TryAdd(TElement item) => throw new NotImplementedException();

        public bool TryTake(out TElement item)
        {
            throw new NotImplementedException();
        }

        public TElement[] ToArray()
        {
            throw new NotImplementedException();
        }

        public void CopyTo(Array array, int index)
        {
            throw new NotImplementedException();
        }

        private void InternalTryAdd(TElement item)
        {
            int stripeIndex = GetStripeIndexForCurrentThread();
            @stripes[stripeIndex].Enter();
            try
            {
                TPriority priority = DeterminePriority(item);
                _queue.Enqueue(item, priority);
            }
            finally
            {
                @stripes[stripeIndex].Exit();
            }
        }
    }
}
