using lvlup.DataFerry.Orchestrators;
using lvlup.DataFerry.Orchestrators.Abstractions;
using System.Collections;
using System.Collections.Concurrent;
using System.Reactive.Linq;

namespace lvlup.DataFerry.Caches
{
    /// <summary>
    /// LfuMemCache is an in-memory caching implementation based on FastCache. 
    /// It includes resource eviction based on associated Time-To-Live (TTL) values and a Window TinyLFU eviction strategy.
    /// </summary>
    /// <remarks>
    /// This class utilizes a <see cref="ConcurrentDictionary{TKey, TValue}"/> and <see cref="CountMinSketch{TKey}"/> behind the scenes.
    /// </remarks> 
    public class LfuMemCache<TKey, TValue> : IMemCache<TKey, TValue> where TKey : notnull
    {
        private readonly ConcurrentDictionary<TKey, TtlValue> _cache;
        private readonly ConcurrentDictionary<TKey, TtlValue> _window;
        // Sophisticated optimizations: 13.142 us ???
        private readonly ConcurrentPriorityQueue<int, TKey> _frequencyQueue;
        // Basic synchronization: 8.231 us
        //private readonly BasicCPQ<TKey, int> _frequencyQueue;
        // Baseline with unsynchronized priority queue is 7.834 μs
        //private readonly PriorityQueue<TKey, int> _frequencyQueue;
        private readonly ITaskOrchestrator _taskOrchestrator;
        private readonly CountMinSketch<TKey> _cms;
        private readonly Timer _cleanUpTimer;
        private readonly int _maxWindowSize;
        private int _currentSize;

        // Signalers
        private CancellationTokenSource? _evictionCancellationToken;
        private long _evictionJobStarted = 0;

        // Locks/Semaphores
        private static readonly SemaphoreSlim _evictionSemaphore = new(1, 1);
        private static readonly SemaphoreSlim _clearSemaphore = new(1, 1);

        /// <inheritdoc/>
        public int MaxSize { get; set; }

        /// <summary>
        /// Initializes a new instance of <see cref="LfuMemCache{TKey, TValue}"/>
        /// </summary>
        /// <param name="maxSize">The maximum number of items allowed in the cache.</param>        
        /// <param name="cleanupJobInterval">Cleanup interval in milliseconds; default is 10000</param>
        public LfuMemCache(ITaskOrchestrator taskOrchestrator, int maxSize = 10000, int cleanupJobInterval = 10000)
        {
            // Cache sizes
            MaxSize = maxSize;
            _maxWindowSize = maxSize / 10;

            // Caches
            _cache = new();
            _window = new();

            // Helper data structures
            _cms = new(MaxSize);
            _taskOrchestrator = taskOrchestrator;
            // We create a concurrent priority queue to hold LFU candidates
            // This comparer gives lower frequency items higher priority
            _frequencyQueue = new(
                _taskOrchestrator,
                Comparer<int>.Create((x, y) => y.CompareTo(x)), 
                maxSize: _maxWindowSize);

            // Timer to trigger TTL-based eviction
            _cleanUpTimer = new Timer(
                _ => _taskOrchestrator.Run(EvictExpired),
                default,
                TimeSpan.FromMilliseconds(cleanupJobInterval),
                TimeSpan.FromMilliseconds(cleanupJobInterval));
        }

        /// <summary>
        /// Immediately removes expired items from the cache.
        /// Typically unnecessary, as item retrieval already handles expiration checks.
        /// </summary>
        public void EvictExpired()
        {
            if (!Monitor.TryEnter(_cleanUpTimer)) return;

            try
            {
                // Cache the current tick count to avoid redundant calls within the "IsExpired()" loop.
                // This optimization yields significant performance gains, especially for larger caches:
                // - 10,000 items: 30 microseconds faster (330 vs 360), a 10% improvement
                // - 50,000 items: 760 microseconds faster (2.057ms vs 2.817ms), a 35% improvement
                // The larger the cache, the greater the benefit from this optimization.

                var currTime = Environment.TickCount64;
                RemoveExpiredItems(_cache);
                RemoveExpiredItems(_window);

                // Local function
                void RemoveExpiredItems(ConcurrentDictionary<TKey, TtlValue> dict)
                {
                    foreach (var pair in dict)
                    {
                        if (currTime > pair.Value._expirationTicks)
                        {
                            dict.TryRemove(pair.Key, out _);
                        }
                    }
                }
            }
            finally
            {
                Monitor.Exit(_cleanUpTimer);
            }
        }

        /// <summary>
        /// Evicts the least frequently used (LFU) items from the cache.
        /// </summary>
        internal async Task EvictLFUAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                await _evictionSemaphore.WaitAsync(token).ConfigureAwait(false);
                try
                {
                    int evictionBatch = (int) Math.Sqrt(_frequencyQueue.GetCount());
                    for (int i = 0; i < evictionBatch; i++)
                    {
                        _frequencyQueue.TryRemoveMin(out var lfuItem);
                        Remove(lfuItem);
                        //_frequencyQueue.TryDequeue(out var lfuItem, out _);
                        //if (lfuItem is not null) Remove(lfuItem);
                    }
                }
                finally
                {
                    _evictionSemaphore.Release();
                }
            }

            // Reset the eviction task status when the task completes
            Interlocked.Exchange(ref _evictionJobStarted, 0);
        }

        /// <inheritdoc/>
        // No lock count: https://arbel.net/2013/02/03/best-practices-for-using-concurrentdictionary/ 
        public int Count => _window.Skip(0).Count() + _cache.Skip(0).Count();

        /// <inheritdoc/>
        public void Clear()
        {
            _clearSemaphore.Wait();
            try
            {
                _window.Clear();
                _cache.Clear();
                _cms.Clear();
                //_frequencyQueue.Clear();
            }
            finally
            { 
                _clearSemaphore.Release();
            }
        }

        /// <inheritdoc/>
        public bool TryGet(TKey key, out TValue value)
        {
            value = default!;

            // Not found or expired
            if ((!_window.TryGetValue(key, out TtlValue ttlValue) && !_cache.TryGetValue(key, out ttlValue)) || ttlValue.IsExpired())
            {
                // Utilizes atomic conditional removal to eliminate the need for locks, 
                // ensuring only items matching both key and value are removed.
                // See: https://devblogs.microsoft.com/pfxteam/little-known-gems-atomic-conditional-removals-from-concurrentdictionary/
                if (ttlValue.HasValue)
                {
                    _window.TryRemove(new KeyValuePair<TKey, TtlValue>(key, ttlValue));
                    _cache.TryRemove(new KeyValuePair<TKey, TtlValue>(key, ttlValue));
                }

                return false;
            }

            // Found and not expired
            value = ttlValue.Value;
            // Update frequencies
            _cms.Increment(key);
            _frequencyQueue.TryAdd(_cms.EstimateFrequency(key), key);
            //_frequencyQueue.Enqueue(key, _cms.EstimateFrequency(key));
            return true;
        }

        private bool TriggerEvictionJob() => _currentSize >= MaxSize && Interlocked.CompareExchange(ref _evictionJobStarted, 1, 0) == 0;
        private bool TerminateEvictionJob() => _currentSize <= MaxSize && Interlocked.CompareExchange(ref _evictionJobStarted, 0, 1) == 1;

        /// <inheritdoc/>
        public void AddOrUpdate(TKey key, TValue value, TimeSpan ttl)
        {
            // Determine where the key exists, if anywhere
            // Right now, we are simply promoting if the item has been
            // accessed before. We'll need to change this to have more
            // sophisticated logic, especially regarding access order.
            bool isInWindow = _window.ContainsKey(key);
            var targetCache = isInWindow || _cache.ContainsKey(key) ? _cache : _window;

            // Add a new key-value pair or update an existing one
            targetCache.AddOrUpdate(key, new TtlValue(value, ttl), (k, oldValue) => new TtlValue(value, ttl));
            if (isInWindow) _window.TryRemove(key, out _);

            // Update the frequency of the key
            _cms.Increment(key);
            Interlocked.Increment(ref _currentSize);
            _frequencyQueue.TryAdd(_cms.EstimateFrequency(key), key);
            //_frequencyQueue.Enqueue(key, _cms.EstimateFrequency(key));

            // Check if eviction is necessary
            if (TriggerEvictionJob())
            {
                _evictionCancellationToken = new();
                _ = Task.Run(() => EvictLFUAsync(_evictionCancellationToken.Token));
            }
            else if (TerminateEvictionJob())
            {
                _evictionCancellationToken?.Cancel();
            }
        }

        /// <inheritdoc/>
        public TValue GetOrAdd(TKey key, Func<TKey, TValue> valueFactory, TimeSpan ttl)
        {
            if (TryGet(key, out TValue value)) return value;

            value = valueFactory(key);
            AddOrUpdate(key, value, ttl);
            return value;
        }

        /// <inheritdoc/>
        public TValue GetOrAdd<TArg>(TKey key, Func<TKey, TArg, TValue> valueFactory, TimeSpan ttl, TArg factoryArgument)
        {
            if (TryGet(key, out TValue value)) return value;

            value = valueFactory(key, factoryArgument);
            AddOrUpdate(key, value, ttl);
            return value;
        }

        /// <inheritdoc/>
        public TValue GetOrAdd(TKey key, TValue value, TimeSpan ttl)
        {
            if (TryGet(key, out TValue existingValue)) return existingValue;

            AddOrUpdate(key, value, ttl);
            return value;
        }

        /// <inheritdoc/>
        public void Remove(TKey key)
        {
            _window.TryRemove(key, out _);
            _cache.TryRemove(key, out _);
        }

        /// <summary>
        /// Returns an enumerator that iterates through the non-expired key-value pairs in the cache.
        /// </summary>
        public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
        {
            var validEntries = _window.Concat(_cache)
                .Where(kvp => !kvp.Value.IsExpired())
                .Select(kvp => new KeyValuePair<TKey, TValue>(kvp.Key, kvp.Value.Value));

            foreach (var entry in validEntries)
            {
                yield return entry;
            }
        }

        /// <inheritdoc/>
        public void Refresh(TKey key, TimeSpan ttl)
        {
            if (_window.TryGetValue(key, out var value) || _cache.TryGetValue(key, out value))
            {
                var ttlValue = new TtlValue(value.Value, ttl);
                (_window.ContainsKey(key) ? _window : _cache)[key] = ttlValue;
            }
        }

        /// <inheritdoc/>
        public void CheckBackplane() { }

        /// <summary>
        /// Returns an enumerator that iterates through the non-expired key-value pairs in the cache.
        /// (Explicit implementation of the IEnumerable.GetEnumerator method.)
        /// </summary>
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        /// <summary>
        /// Represents a value with an associated time-to-live (TTL) for expiration.
        /// </summary>
        private readonly struct TtlValue
        {
            /// <summary>
            /// The stored value.
            /// </summary>
            public readonly TValue Value;

            /// <summary>
            /// The tick count at which this value expires.
            /// </summary>
            public readonly long _expirationTicks;

            /// <summary>
            /// Initializes a new instance of the <see cref="TtlValue"/> class.
            /// </summary>
            /// <param name="value">The value to store.</param>
            /// <param name="ttl">The time-to-live (TTL) for the value.</param>
            public TtlValue(TValue value, TimeSpan ttl)
            {
                Value = value;
                _expirationTicks = Environment.TickCount64 + (long)ttl.TotalMilliseconds;
            }

            /// <summary>
            /// Determines if this value has expired.
            /// </summary>
            /// <returns>True if the value has expired; otherwise, false.</returns>
            public readonly bool IsExpired() => Environment.TickCount64 > _expirationTicks;

            public readonly bool HasValue => !IsNullOrEmpty;

            private readonly bool IsNullOrEmpty
            {
                get
                {
                    if (Value != null) return true;
                    if (_expirationTicks > 0) return true;
                    return false;
                }
            }
        }

        #region IDisposable

        private bool _disposedValue;

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)
                {
                    // Safe disposal even if _cleanUpTimer is null
                    _cleanUpTimer?.Dispose();
                }

                _disposedValue = true;
            }
        }

        #endregion
    }
}