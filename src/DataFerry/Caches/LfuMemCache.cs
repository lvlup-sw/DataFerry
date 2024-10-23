using System.Collections;
using System.Collections.Concurrent;

namespace lvlup.DataFerry.Caches
{
    /// <summary>
    /// LfuMemCache is an in-memory caching implementation based on FastCache and BitFaster. 
    /// It includes resource eviction based on associated Time-To-Live (TTL) values and an LFU eviction strategy.
    /// </summary>
    /// <remarks>
    /// This class utilizes a <see cref="ConcurrentDictionary{TKey, TValue}"/> and <see cref="CountMinSketch{TKey}"/> behind the scenes.
    /// </remarks> 
    public class LfuMemCache<TKey, TValue> : ILfuMemCache<TKey, TValue> where TKey : notnull
    {
        private readonly ConcurrentDictionary<TKey, TtlValue> _cache;
        private readonly ConcurrentDictionary<TKey, TtlValue> _window;
        private readonly ConcurrentQueue<TKey> _recentKeys;
        private readonly CountMinSketch<TKey> _cms;
        private readonly Timer _cleanUpTimer;
        private readonly int _sampleSize;

        /// <inheritdoc/>
        public int MaxSize { get; set; }

        /// <summary>
        /// Initializes a new instance of <see cref="LfuMemCache{TKey, TValue}"/>
        /// </summary>
        /// <param name="maxSize">The maximum number of items allowed in the cache.</param>        
        /// <param name="cleanupJobInterval">Cleanup interval in milliseconds; default is 10000</param>
        public LfuMemCache(int maxSize = 10000, int cleanupJobInterval = 10000)
        {
            MaxSize = maxSize;
            _sampleSize = maxSize / 10;
            _cache = new();
            _window = new();
            _recentKeys = new();
            _cms = new(maxSize);
            _cleanUpTimer = new Timer(
                async s => await EvictExpiredJob(),
                default,
                TimeSpan.FromMilliseconds(cleanupJobInterval),
                TimeSpan.FromMilliseconds(cleanupJobInterval));
        }

        private static readonly SemaphoreSlim _globalEvictionSemaphore = new(1, 1);
        private async Task EvictExpiredJob()
        {
            // To avoid resource wastage from concurrent cleanup jobs in multiple LfuMemCache instances, 
            // we use a Semaphore to serialize cleanup execution. This mitigates CPU-intensive operations.
            // User-initiated eviction is still allowed.
            // A Semaphore is preferred over a traditional lock to prevent thread starvation.

            await _globalEvictionSemaphore.WaitAsync().ConfigureAwait(false);
            try
            {
                EvictExpired();
            }
            finally
            {
                _globalEvictionSemaphore.Release();
            }
        }

        /// <summary>
        /// Immediately removes expired items from the cache.
        /// Typically unnecessary, as item retrieval already handles expiration checks.
        /// </summary>
        public void EvictExpired()
        {
            if (!Monitor.TryEnter(_cleanUpTimer))
                return;

            try
            {
                // Cache the current tick count to avoid redundant calls within the "IsExpired()" loop.
                // This optimization yields significant performance gains, especially for larger caches:
                // - 10,000 items: 30 microseconds faster (330 vs 360), a 10% improvement
                // - 50,000 items: 760 microseconds faster (2.057ms vs 2.817ms), a 35% improvement
                // The larger the cache, the greater the benefit from this optimization.

                var currTime = Environment.TickCount64;

                foreach (var pair in _cache)
                {
                    if (currTime > pair.Value._expirationTicks)
                    {
                        _cache.TryRemove(pair.Key, out _);
                    }
                }
            }
            finally
            {
                Monitor.Exit(_cleanUpTimer);
            }
        }

        /// <inheritdoc/>
        public int Count => _window.Count + _cache.Count;

        /// <inheritdoc/>
        public void Clear()
        {
            _window.Clear();
            _cache.Clear();
            _recentKeys.Clear();
            _cms.Clear();
        }

        /// <inheritdoc/>
        public bool TryGet(TKey key, out TValue value)
        {
            value = default!;

            // Not found or expired
            if ((!_window.TryGetValue(key, out TtlValue? ttlValue) && !_cache.TryGetValue(key, out ttlValue)) || ttlValue.IsExpired())
            {
                // Utilizes atomic conditional removal to eliminate the need for locks, 
                // ensuring only items matching both key and value are removed.
                // See: https://devblogs.microsoft.com/pfxteam/little-known-gems-atomic-conditional-removals-from-concurrentdictionary/
                if (ttlValue is not null)
                {
                    _window.TryRemove(new KeyValuePair<TKey, TtlValue>(key, ttlValue));
                    _cache.TryRemove(new KeyValuePair<TKey, TtlValue>(key, ttlValue));
                }

                // When an item is found but expired, it should be treated as "not found" and removed.
                // To ensure atomicity (preventing another thread from adding a new item with the same key while we're evicting the expired one), 
                // we could use a lock. However, this introduces performance overhead.
                // 
                // Instead, we opt for a lock-free approach:
                // 1. Check if the key exists and retrieve its associated value.
                // 2. If the value is expired, remove the item by both key AND value. 
                // 
                // This strategy prevents accidental removal of newly added items with the same key, as their values would differ.
                // 
                // Avoiding locks significantly improves performance, making this approach preferable.

                return false;
            }

            // Found and not expired
            value = ttlValue.Value;
            _cms.Insert(key);
            _recentKeys.Enqueue(key);
            if (_recentKeys.Count > _sampleSize) _recentKeys.TryDequeue(out _);
            return true;
        }

        /// <inheritdoc/>
        public void AddOrUpdate(TKey key, TValue value, TimeSpan ttl)
        {
            var ttlValue = new TtlValue(value, ttl);
            _cms.Insert(key);
            EvictLFU();

            bool isWindowKey = _window.ContainsKey(key);
            bool isCacheKey = _cache.ContainsKey(key);

            if (isWindowKey || isCacheKey)
            {
                // When a window item or cache item is accessed, it is moved to the MRU position.
                var targetCache = isWindowKey ? _window : _cache;
                targetCache[key] = ttlValue;
                _recentKeys.Enqueue(key);
            }
            else
            {
                // New items are added to the window.
                if (_window.Count >= _sampleSize)
                {
                    // When the window is full, candidate items are moved to the probation segment in LRU order.
                    while (_recentKeys.TryDequeue(out TKey? oldKey))
                    {
                        if (_window.TryGetValue(oldKey, out TtlValue? ttlVal) && _window.TryRemove(oldKey, out _))
                        {
                            _cache[oldKey] = ttlVal;
                            break;
                        }
                    }
                }
                _window[key] = ttlValue;
                _recentKeys.Enqueue(key);
            }

            if (_recentKeys.Count > _sampleSize) _recentKeys.TryDequeue(out _);
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

        /// <summary>
        /// Checks if the cache requires eviction of items.
        /// Eviction is required when the number of items in the cache is greater than or equal to the maximum size of the cache.
        /// </summary>
        /// <returns>
        /// true if the cache requires eviction; otherwise, false.
        /// </returns>
        private bool RequiresEviction() => Count >= MaxSize;

        /// <summary>
        /// Evicts the least frequently used (LFU) item from the cache.
        /// If the cache does not require eviction, no action is taken.
        /// If the cache is empty, no action is taken.
        /// </summary>
        internal void EvictLFU()
        {
            if (!RequiresEviction()) return;

            TKey? lfuKey = default;
            int minFrequency = int.MaxValue;
            foreach (var key in _recentKeys)
            {
                var frequency = _cms.Query(key);
                if (frequency < minFrequency)
                {
                    minFrequency = frequency;
                    lfuKey = key;
                }
            }

            // When the main space is full, the access frequency of each
            // window candidate is compared to probation victims in LRU order.
            // The item with the lowest frequency is evicted until the cache size is within bounds.
            if (lfuKey is not null) _cache.TryRemove(lfuKey, out _);
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
        private sealed class TtlValue
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
            public bool IsExpired() => Environment.TickCount64 > _expirationTicks;
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