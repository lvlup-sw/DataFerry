using lvlup.DataFerry.Orchestrators.Abstractions;
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
    public class TtlMemCache<TKey, TValue> : IMemCache<TKey, TValue> where TKey : notnull
    {
        private readonly ConcurrentDictionary<TKey, TtlValue> _dict = new();
        private readonly ITaskOrchestrator _taskOrchestrator;
        private readonly Timer _cleanUpTimer;

        /// <summary>
        /// Initializes a new instance of <see cref="TtlMemCache{TKey, TValue}"/>
        /// </summary>
        /// <param name="cleanupJobInterval">Cleanup interval in milliseconds; default is 10000</param>
        public TtlMemCache(ITaskOrchestrator taskOrchestrator, int cleanupJobInterval = 10000)
        {
            _taskOrchestrator = taskOrchestrator;
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

                // Identify the keys of expired entries
                // Less contention at the cost of an
                // intermediate list allocation.
                var expiredKeys = _dict
                    .Where(pair => currTime > pair.Value._expirationTicks)
                    .Select(pair => pair.Key)
                    .ToList();

                foreach (var key in expiredKeys)
                {
                    _dict.TryRemove(key, out _);
                }
            }
            finally
            {
                Monitor.Exit(_cleanUpTimer);
            }
        }

        /// <inheritdoc/>
        // No lock count: https://arbel.net/2013/02/03/best-practices-for-using-concurrentdictionary/ 
        public int Count => _dict.Skip(0).Count();

        public int MaxSize { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }

        /// <inheritdoc/>
        public void Clear() => _dict.Clear();

        /// <inheritdoc/>
        public void AddOrUpdate(TKey key, TValue value, TimeSpan ttl)
        {   // ConcurrentDictionary is thread-safe
            var ttlValue = new TtlValue(value, ttl);
            _dict.AddOrUpdate(key, ttlValue, (existingKey, existingValue) => ttlValue);
        }

        /// <inheritdoc/>
        public bool TryGet(TKey key, out TValue value)
        {
            value = default!;

            // Not found or expired
            if (!_dict.TryGetValue(key, out TtlValue ttlValue) || ttlValue.IsExpired())
            {
                // Utilizes atomic conditional removal to eliminate the need for locks, 
                // ensuring only items matching both key and value are removed.
                // See: https://devblogs.microsoft.com/pfxteam/little-known-gems-atomic-conditional-removals-from-concurrentdictionary/
                if (ttlValue.HasValue)
                {
                    _dict.TryRemove(new KeyValuePair<TKey, TtlValue>(key, ttlValue));
                }

                return false;
            }

            // Found and not expired
            value = ttlValue.Value;
            return true;
        }

        /// <inheritdoc/>
        public TValue GetOrAdd(TKey key, Func<TKey, TValue> valueFactory, TimeSpan ttl)
        {
            if (TryGet(key, out var value)) return value;

            var ttlValue = new TtlValue(valueFactory(key), ttl);
            return _dict.GetOrAdd(key, ttlValue).Value;
        }

        /// <inheritdoc/>
        public TValue GetOrAdd<TArg>(TKey key, Func<TKey, TArg, TValue> valueFactory, TimeSpan ttl, TArg factoryArgument)
        {
            if (TryGet(key, out var value)) return value;

            var ttlValue = new TtlValue(valueFactory(key, factoryArgument), ttl);
            return _dict.GetOrAdd(key, ttlValue).Value;
        }

        /// <inheritdoc/>
        public TValue GetOrAdd(TKey key, TValue value, TimeSpan ttl)
        {
            if (TryGet(key, out var existingValue)) return existingValue;

            var ttlValue = new TtlValue(value, ttl);
            return _dict.GetOrAdd(key, ttlValue).Value;
        }

        /// <inheritdoc/>
        public void Remove(TKey key) => _dict.TryRemove(key, out _);

        /// <inheritdoc/>
        public void Refresh(TKey key, TimeSpan ttl)
        {
            if (_dict.TryGetValue(key, out var value))
            {
                var ttlValue = new TtlValue(value.Value, ttl);
                _dict[key] = ttlValue;
            }
        }

        /// <inheritdoc/>
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

        /// <inheritdoc/>
        public void CheckBackplane() { }

        /// <inheritdoc/>
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