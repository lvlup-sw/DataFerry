using System.Collections.Concurrent;
using System.Collections;

namespace lvlup.DataFerry.Collections
{
    public class BasicCPQ<TElement, TPriority> : IProducerConsumerCollection<TElement>
    {
        // Queues
        private readonly PriorityQueue<TElement, TPriority> _queue;

        // Locks/Semaphores
        private readonly Lock[] @stripes;
        private readonly Lock @syncLock = new();

        // Settings
        private readonly int _queueCapacity;
        private readonly int _stripeCount = 10;

        public BasicCPQ(IComparer<TPriority> comparer, int capacity = 1000)
        {
            _queue = new(comparer);
            _queueCapacity = capacity;
            @stripes = new Lock[_stripeCount];
            @stripes = Enumerable.Range(0, _stripeCount)
                .Select(_ => new Lock())
                .ToArray();
        }

        public readonly struct ItemWithPriority(TElement item, TPriority priority)
        {
            public TElement Item { get; } = item;
            public TPriority Priority { get; } = priority;
        }

        private int GetStripeIndexForCurrentThread()
        {
            int threadId = Environment.CurrentManagedThreadId;
            var rand = new Random(threadId);
            return rand.Next(_stripeCount);
        }

        public int Count => _queue.UnorderedItems.Skip(0).Count();

        public bool IsSynchronized => true;

#pragma warning disable CS9216
        public object SyncRoot => @syncLock;
#pragma warning restore CS9216

        private bool IsPowerOfTwo(int x) => (x & (x - 1)) == 0;

        private int NextPowerOfTwo(int x)
        {
            x--;
            x |= x >> 1;
            x |= x >> 2;
            x |= x >> 4;
            x |= x >> 8;
            x |= x >> 16;
            return x + 1;
        }

        public bool TryAdd(TElement item, TPriority priority)
        {
            InternalTryAdd(new(item, priority));
            //InternalTryAddWithSingleLock(new(item, priority));
            return true;
        }

        private void EvictItemIfAtCapacity(ItemWithPriority item)
        {
            // If the queue is at capacity, the new item has higher priority, 
            // and the new item is NOT already in the queue, then evict 
            // the current lowest frequency item to make space.
            if (Count >= _queueCapacity &&
                _queue.TryPeek(out var currLfu, out var currPriority) &&
                currLfu is not null &&
                _queue.Comparer.Compare(currPriority, item.Priority) < 0 &&
                !currLfu.Equals(item.Item))
            {
                // Access the thread-specific lock
                int stripeIndex = GetStripeIndexForCurrentThread();
                @stripes[stripeIndex].Enter();
                try
                {
                    _queue.TryDequeue(out _, out _);
                }
                finally
                {
                    @stripes[stripeIndex].Exit();
                }
            }
        }

        private void InternalTryAdd(ItemWithPriority item)
        {
            // If the queue is full, we need to evict an item
            //EvictItemIfAtCapacity(item);

            // Access the thread-specific lock
            int stripeIndex = GetStripeIndexForCurrentThread();
            @stripes[stripeIndex].Enter();
            try
            {
                // Enqueue item
                _queue.Enqueue(item.Item, item.Priority);
            }
            finally
            {
                @stripes[stripeIndex].Exit();
            }
        }

        private void InternalTryAddWithSingleLock(ItemWithPriority item)
        {
            // If the queue is full, we need to evict an item
            //EvictItemIfAtCapacity(item);

            // Access the thread-specific lock
            @syncLock.Enter();
            try
            {
                // Enqueue item
                _queue.Enqueue(item.Item, item.Priority);
            }
            finally
            {
                @syncLock.Exit();
            }
        }

        public bool TryTake(out TElement item)
        {
            // Access the thread-specific lock
            int stripeIndex = GetStripeIndexForCurrentThread();
            @stripes[stripeIndex].Enter();
            try
            {
                _queue.TryDequeue(out var i, out _);
                item = i ?? default!;
            }
            finally
            {
                @stripes[stripeIndex].Exit();
            }

            return false;
        }

        public bool TryTakeWithSingleLock(out TElement item)
        {
            // Access the thread-specific lock
            @syncLock.Enter();
            try
            {
                _queue.TryDequeue(out var i, out _);
                item = i ?? default!;
            }
            finally
            {
                @syncLock.Exit();
            }

            return false;
        }

        public void CopyTo(TElement[] array, int index)
        {
            // Acquire all locks
            Enumerable.Range(0, _stripeCount)
                .ToList()
                .ForEach(i => @stripes[i].Enter());

            try
            {
                _queue.UnorderedItems
                    .OrderBy(tuple => tuple.Priority, _queue.Comparer)
                    .Select(tuple => tuple.Element)
                    .ToArray()
                    .CopyTo(array, index);
            }
            finally
            {
                // Release all locks
                Enumerable.Range(0, _stripeCount)
                    .ToList()
                    .ForEach(i => @stripes[i].Exit());
            }
        }

        public void CopyTo(Array array, int index)
        {
            // Acquire all locks
            Enumerable.Range(0, _stripeCount)
                .ToList()
                .ForEach(i => @stripes[i].Enter());

            try
            {
                _queue.UnorderedItems
                    .OrderBy(tuple => tuple.Priority, _queue.Comparer)
                    .Select(tuple => tuple.Element)
                    .ToArray()
                    .CopyTo(array, index);
            }
            finally
            {
                // Release all locks
                Enumerable.Range(0, _stripeCount)
                    .ToList()
                    .ForEach(i => @stripes[i].Exit());
            }
        }

        public TElement[] ToArray()
        {
            // Acquire all locks
            Enumerable.Range(0, _stripeCount)
                .ToList()
                .ForEach(i => @stripes[i].Enter());

            try
            {
                return _queue.UnorderedItems
                    .Select(tuple => tuple.Element)
                    .ToArray();
            }
            finally
            {
                // Release all locks
                Enumerable.Range(0, _stripeCount)
                    .ToList()
                    .ForEach(i => @stripes[i].Exit());
            }
        }

        public IEnumerator<TElement> GetEnumerator()
        {
            // Acquire all locks
            Enumerable.Range(0, _stripeCount)
                .ToList()
                .ForEach(i => @stripes[i].Enter());

            try
            {
                // Return an enumerator that iterates over the elements in priority order
                return _queue.UnorderedItems
                    .OrderBy(tuple => tuple.Priority, _queue.Comparer)
                    .Select(tuple => tuple.Element)
                    .GetEnumerator();
            }
            finally
            {
                // Release all locks
                Enumerable.Range(0, _stripeCount)
                    .ToList()
                    .ForEach(i => @stripes[i].Exit());
            }
        }

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public bool TryAdd(TElement item) =>
            throw new NotImplementedException("Use `TryAdd(TElement item, TPriority priority)` instead.");
    }
}
