using System.Collections;
using System.Collections.Concurrent;

namespace lvlup.DataFerry.Caches
{
    /// <summary>
    /// FastMemCache is an in-memory caching implementation based on FastCache. 
    /// It includes resource eviction based on associated Time-To-Live (TTL) values.
    /// </summary>
    /// <remarks>
    /// This class utilizes a <see cref="ConcurrentDictionary{TKey, TValue}"/> behind the scenes.
    /// </remarks> 
    public class FastMemCache<TKey, TValue> : IFastMemCache<TKey, TValue> where TKey : notnull
    {
        private readonly ConcurrentDictionary<TKey, TtlValue> _dict = new();

        private readonly Timer _cleanUpTimer;

        /// <summary>
        /// Initializes a new instance of <see cref="FastMemCache{TKey, TValue}"/>
        /// </summary>
        /// <param name="cleanupJobInterval">Cleanup interval in milliseconds; default is 10000</param>
        public FastMemCache(int cleanupJobInterval = 10000)
        {
            _cleanUpTimer = new Timer(
                async s => await EvictExpiredJob(),
                default,
                TimeSpan.FromMilliseconds(cleanupJobInterval),
                TimeSpan.FromMilliseconds(cleanupJobInterval));
        }

        private static readonly SemaphoreSlim _globalEvictionSemaphore = new(1, 1);
        private async Task EvictExpiredJob()
        {
            // In scenarios with numerous FastMemCache instances, prevent concurrent timer-based cleanup jobs.
            // Cleanup involves CPU-intensive collection iteration and computations.
            // Employ a Semaphore to serialize cleanup execution, avoiding resource wastage.
            // Explicit user-initiated eviction remains permissible.
            // Opt for a Semaphore over a traditional lock to mitigate thread starvation risks.

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

                // Use ToList() to avoid modifying the _dict directly
                foreach (var pair in _dict.ToList())
                {
                    if (currTime > pair.Value._expirationTicks)
                    {
                        _dict.TryRemove(pair.Key, out _);
                    }
                }
            }
            finally
            {
                Monitor.Exit(_cleanUpTimer);
            }
        }

        /// <summary>
        /// Returns the total number of items in the cache, 
        /// including expired items that have not yet been removed by the cleanup process.
        /// </summary>
        public int Count => _dict.Count;

        /// <summary>
        /// Clears all items from the cache.
        /// </summary>
        public void Clear() => _dict.Clear();

        /// <summary>
        /// Adds or updates an item in the cache. If the item already exists, its value and TTL are updated.
        /// </summary>
        /// <param name="key">The unique identifier for the item.</param>
        /// <param name="value">The data associated with the key.</param>
        /// <param name="ttl">The time-to-live (TTL) for the item, after which it will expire.</param>
        public void AddOrUpdate(TKey key, TValue value, TimeSpan ttl)
        {   // ConcurrentDictionary is thread-safe
            var ttlValue = new TtlValue(value, ttl);
            _dict.AddOrUpdate(key, ttlValue, (existingKey, existingValue) => ttlValue);
        }

        /// <summary>
        /// Attempts to retrieve the value associated with the given key.
        /// </summary>
        /// <param name="key">The key of the item to retrieve.</param>
        /// <param name="value">
        /// If the key is found, this output parameter will contain its corresponding value; otherwise, it will contain the default value for the type.
        /// </param>
        /// <returns>True if the key was found and its value was retrieved; otherwise, false.</returns>
        public bool TryGet(TKey key, out TValue value)
        {
            value = default!;

            if (!_dict.TryGetValue(key, out TtlValue? ttlValue) || ttlValue.IsExpired())
            {
                // Utilizes atomic conditional removal to eliminate the need for locks, 
                // ensuring only items matching both key and value are removed.
                // See: https://devblogs.microsoft.com/pfxteam/little-known-gems-atomic-conditional-removals-from-concurrentdictionary/
                if (ttlValue is not null)
                {
                    _dict.TryRemove(new KeyValuePair<TKey, TtlValue>(key, ttlValue));
                }

                /* EXPLANATION:
                 * When an item is found but expired, it should be treated as "not found" and removed.
                 * To ensure atomicity (preventing another thread from adding a new item with the same key while we're evicting the expired one), 
                 * we could use a lock. However, this introduces performance overhead.
                 * 
                 * Instead, we opt for a lock-free approach:
                 * 1. Check if the key exists and retrieve its associated value.
                 * 2. If the value is expired, remove the item by both key AND value. 
                 * 
                 * This strategy prevents accidental removal of newly added items with the same key, as their values would differ.
                 * 
                 * Avoiding locks significantly improves performance, making this approach preferable.
                 */

                return false;
            }

            // Found and not expired
            value = ttlValue.Value;
            return true;
        }

        /// <summary>
        /// Adds a key-value pair to the cache if the key does not already exist. 
        /// If the key exists, returns the existing value without updating it.
        /// </summary>
        /// <param name="key">The key of the item to add or retrieve.</param>
        /// <param name="valueFactory">A function to generate the value if the key is not found.</param>
        /// <param name="ttl">The time-to-live (TTL) for the item, after which it will expire.</param>
        public TValue GetOrAdd(TKey key, Func<TKey, TValue> valueFactory, TimeSpan ttl)
        {
            if (TryGet(key, out var value)) return value;

            var ttlValue = new TtlValue(valueFactory(key), ttl);
            return _dict.GetOrAdd(key, ttlValue).Value;
        }

        /// <summary>
        /// Adds a key-value pair to the cache if the key does not already exist, 
        /// using the provided factory function to generate the value. 
        /// If the key exists, returns the existing value without updating it.
        /// </summary>
        /// <param name="key">The key of the item to add or retrieve.</param>
        /// <param name="valueFactory">A function to generate the value if the key is not found.</param>
        /// <param name="ttl">The time-to-live (TTL) for the item, after which it will expire.</param>
        /// <param name="factoryArgument">An argument to pass to the `valueFactory` function.</param>
        public TValue GetOrAdd<TArg>(TKey key, Func<TKey, TArg, TValue> valueFactory, TimeSpan ttl, TArg factoryArgument)
        {
            if (TryGet(key, out var value)) return value;

            var ttlValue = new TtlValue(valueFactory(key, factoryArgument), ttl);
            return _dict.GetOrAdd(key, ttlValue).Value;
        }

        /// <summary>
        /// Adds a key-value pair to the cache if the key does not already exist. 
        /// If the key exists, its value is returned without any updates.
        /// </summary>
        /// <param name="key">The key of the item to add or retrieve.</param>
        /// <param name="value">The value to add if the key is not found.</param>
        /// <param name="ttl">The time-to-live (TTL) for the item, after which it will expire.</param>
        public TValue GetOrAdd(TKey key, TValue value, TimeSpan ttl)
        {
            if (TryGet(key, out var existingValue)) return existingValue;

            var ttlValue = new TtlValue(value, ttl);
            return _dict.GetOrAdd(key, ttlValue).Value;
        }

        /// <summary>
        /// Attempts to remove the item associated with the specified key from the cache.
        /// </summary>
        /// <param name="key">The key of the item to remove.</param>
        public void Remove(TKey key) => _dict.TryRemove(key, out _);

        /// <summary>
        /// Returns an enumerator that iterates through the non-expired key-value pairs in the cache.
        /// </summary>
        public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
        {
            var validEntries = _dict
                .Where(kvp => !kvp.Value.IsExpired())
                .Select(kvp => new KeyValuePair<TKey, TValue>(kvp.Key, kvp.Value.Value));

            foreach (var entry in validEntries)
            {
                yield return entry;
            }
        }

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

        //IDisposable members
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

        public void CheckBackplane() { }
    }
}