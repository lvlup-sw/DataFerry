using lvlup.DataFerry.Buffers;
using lvlup.DataFerry.Orchestrators;
using lvlup.DataFerry.Orchestrators.Contracts;
using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Reactive.Linq;
using System.Threading.Tasks;

namespace lvlup.DataFerry.Caches;

/// <summary>
/// LfuMemCache is an in-memory caching implementation based on the Window TinyLFU policy. 
/// It includes resource eviction based on an approximate Least Frequently Used strategy and associated Time-To-Live (TTL) values.
/// </summary>
public class LfuMemCache<TKey, TValue> : IMemCache<TKey, TValue> where TKey : notnull
{
    // Physical cache
    private readonly ConcurrentDictionary<TKey, TtlValue> _cache;

    // Logical partitions of cache
    private readonly ConcurrentLinkedList<TKey> _probation;
    private readonly ConcurrentLinkedList<TKey> _protected;

    // Frequency queue tracking LFU candidates
    private readonly ConcurrentPriorityQueue<int, TKey> _evictionWindow;

    // Frequency histogram
    private readonly CountMinSketch<TKey> _cms;

    // Background processes
    private readonly ConcurrentQueue<TKey> _recentEntries;
    private readonly ThreadBuffer<BufferItem> _writeBuffer;
    private readonly ITaskOrchestrator _taskOrchestrator;
    private readonly Timer _cleanUpTimer;

    // Signalers and calcs
    private CancellationTokenSource? _evictionCancellationToken;
    private long _evictionJobStarted = 0;
    private const int BufferSize = 8;
    private const int RecentWindowMaxSize = 5;
    private readonly int _maxWindowSize;
    private readonly int _maxProbationSize;
    private readonly int _maxProtectedSize;
    private int _currentSize;

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
        _maxWindowSize = maxSize / 100; // 1% of entire cache
        _maxProbationSize = (maxSize * 20 / 100) - _maxWindowSize; // 20% of main cache
        _maxProtectedSize = (maxSize * 80 / 100) - _maxWindowSize; // 80% of main cache

        // Cache and partitions
        _cache = new();
        _probation = new();
        _protected = new();

        // Helper data structures
        _cms = new(MaxSize);
        _writeBuffer = new(BufferSize);
        _recentEntries = new();
        _taskOrchestrator = taskOrchestrator;
        // We create a concurrent priority queue to hold LFU candidates
        // This comparer gives lower frequency items higher priority
        _evictionWindow = new(
            _taskOrchestrator,
            Comparer<int>.Create((x, y) => x.CompareTo(y)), 
            maxSize: _maxWindowSize,
            allowDuplicatePriorities: false);

        // Timer to trigger TTL-based eviction
        _cleanUpTimer = new Timer(
            _ => _taskOrchestrator.Run(EvictExpired),
            default,
            TimeSpan.FromMilliseconds(cleanupJobInterval),
            TimeSpan.FromMilliseconds(cleanupJobInterval));

        // Setup the buffer threshold eventing
        _writeBuffer.ThresholdReached += DrainBufferIntoCache;
        // We should also trigger based on the timing wheel
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
                int evictionBatch = (int) Math.Sqrt(_evictionWindow.GetCount());
                for (int i = 0; i < evictionBatch; i++)
                {
                    _evictionWindow.TryDeleteMinProbabilistically(out var lfuItem);
                    Remove(lfuItem);
                    //_evictionWindow.TryDequeue(out var lfuItem, out _);
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
    public int Count => _cache.Skip(0).Count() + _writeBuffer.GetCount();

    /// <inheritdoc/>
    public void Clear()
    {
        _clearSemaphore.Wait();
        try
        {
            _evictionWindow.Clear();
            _probation.Clear();
            _protected.Clear();
            _cms.Clear();
            _writeBuffer.Clear();
            _cache.Clear();
        }
        finally
        { 
            _clearSemaphore.Release();
        }
    }

    //private bool TriggerEvictionJob() => _currentSize >= MaxSize && Interlocked.CompareExchange(ref _evictionJobStarted, 1, 0) == 0;
    //private bool TerminateEvictionJob() => _currentSize <= MaxSize && Interlocked.CompareExchange(ref _evictionJobStarted, 0, 1) == 1;

    /// <summary>
    /// Flushes the items from the write buffer into the appropriate cache segments.
    /// </summary>
    internal void DrainBufferIntoCache(object? sender, ThresholdReachedEventArgs args)
    {
        // Process flushed items for segment and promotion
        foreach (var item in _writeBuffer.FlushItems())
        {
            // Determine promotion eligibility
            var segment = DetermineCacheSegment(item.Value);

            // Make sure we have room in the segments
            EnsureAvailableSpaceInSegment(segment, item);

            // Add the item to the corresponding segment list
            switch (segment)
            {
                case CacheSegment.Protected:
                    _protected.TryInsert(item.Key);
                    break;
                case CacheSegment.Probation:
                    _probation.TryInsert(item.Key);
                    break;
                case CacheSegment.Invalid:
                    continue;
            }

            _cache.AddOrUpdate(item.Key, item.Value, (existingKey, existingValue) => item.Value);

            // Add to recent items queue
            _recentEntries.Enqueue(item.Key);
            if (_recentEntries.Count > RecentWindowMaxSize)
            {
                _recentEntries.TryDequeue(out _);
            }
        }
    }

    private void EnsureAvailableSpaceInSegment(CacheSegment segment, BufferItem item)
    {
        throw new NotImplementedException();
    }

    private CacheSegment DetermineCacheSegment(TtlValue value)
    {
        throw new NotImplementedException();
    }

    /// <inheritdoc/>
    public bool TryGet(TKey key, out TValue value)
    {
        value = default!;

        // Not found or expired
        if (!_cache.TryGetValue(key, out var ttlValue) || ttlValue.IsExpired())
        {
            // Utilizes atomic conditional removal to eliminate the need for locks, 
            // ensuring only items matching both key and value are removed.
            // See: https://devblogs.microsoft.com/pfxteam/little-known-gems-atomic-conditional-removals-from-concurrentdictionary/
            if (ttlValue.HasValue)
            {
                _cache.TryRemove(new KeyValuePair<TKey, TtlValue>(key, ttlValue));
            }

            return false;
        }

        // Found and not expired
        value = ttlValue.Value;
        _cms.Increment(key);
        return true;
    }

    /// <inheritdoc/>
    public void AddOrUpdate(TKey key, TValue value, TimeSpan ttl)
    {
        if (!Admit(key)) return;

        // Add to WriteBuffer
        _writeBuffer.Add(
            new BufferItem(
                key,
                new TtlValue(value, CacheSegment.Invalid, ttl)
            ));

        // Update frequency in sketch
        _cms.Increment(key);

        // Admit the item to the cache
        bool Admit(TKey key)
        {
            if (!_evictionWindow.TryDeleteMinProbabilistically(out var curr))
            {
                return false;
            }

            int candidateFreq = _cms.EstimateFrequency(key);
            int currFreq = _cms.EstimateFrequency(curr);

            // Determine which key to add and return the result
            return (candidateFreq >= currFreq) switch
            {
                true  => _evictionWindow.TryAdd(candidateFreq, key),
                false => _evictionWindow.TryAdd(currFreq, curr)
            };
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
        if (!_probation.TryRemove(key))
            _protected.TryRemove(key);

        _cache.TryRemove(key, out _);
    }

    /// <summary>
    /// Returns an enumerator that iterates through the non-expired key-value pairs in the cache.
    /// </summary>
    public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
    {
        var validEntries = _cache
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
        if (_cache.TryGetValue(key, out var value))
        {
            var ttlValue = new TtlValue(value.Value, value.CacheSegment, ttl);
            _cache[key] = ttlValue;
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
        /// The segment of the cache this value is stored in.
        /// </summary>
        public readonly CacheSegment CacheSegment;

        /// <summary>
        /// Initializes a new instance of the <see cref="TtlValue"/> class.
        /// </summary>
        /// <param name="value">The value to store.</param>
        /// <param name="ttl">The time-to-live (TTL) for the value.</param>
        public TtlValue(TValue value, CacheSegment segment, TimeSpan ttl)
        {
            Value = value;
            CacheSegment = segment;
            _expirationTicks = Environment.TickCount64 + (long)ttl.TotalMilliseconds;
        }

        /// <summary>
        /// Determines if this value has expired.
        /// </summary>
        /// <returns>True if the value has expired; otherwise, false.</returns>
        public readonly bool IsExpired() => Environment.TickCount64 > _expirationTicks;

        /// <summary>
        /// Indicates whether this value is valid and unexpired.
        /// </summary>
        public readonly bool HasValue => !IsNullOrExpired;

        /// <summary>
        /// Gets a value indicating whether the value is null or expired.
        /// </summary>
        private readonly bool IsNullOrExpired
        {
            get
            {
                if (Value != null) return true;
                if (CacheSegment == CacheSegment.Invalid) return true;
                if (_expirationTicks > 0) return true;
                return false;
            }
        }
    }

    /// <summary>
    /// Represents an item stored in the write buffer.
    /// </summary>
    private readonly struct BufferItem
    {
        /// <summary>
        /// Gets the key of the item.
        /// </summary>
        public readonly TKey Key;

        /// <summary>
        /// Gets the value of the item, along with its time-to-live (TTL).
        /// </summary>
        public readonly TtlValue Value;

        /// <summary>
        /// Initializes a new instance of the <see cref="BufferItem"/> struct.
        /// </summary>
        /// <param name="key">The key of the item.</param>
        /// <param name="value">The value of the item.</param>
        public BufferItem(TKey key, TtlValue value)
        {
            Key = key;
            Value = value;
        }

        /// <summary>
        /// Gets a value indicating whether this item has a valid key and value.
        /// </summary>
        public readonly bool HasValue => !IsNullOrEmpty;

        /// <summary>
        /// Gets a value indicating whether the key or value is null or empty.
        /// </summary>
        private readonly bool IsNullOrEmpty
        {
            get
            {
                if (Key is null) return true;
                if (!Value.HasValue) return true;
                return false;
            }
        }
    }

    /// <summary>
    /// Represents the logical segments within the cache.
    /// </summary>
    private enum CacheSegment
    {
        /// <summary>
        /// Indicates an invalid or unknown segment.
        /// </summary>
        Invalid = -1,

        /// <summary>
        /// The probationary segment where new items are initially added.
        /// </summary>
        Probation = 0,

        /// <summary>
        /// The protected segment for frequently accessed items.
        /// </summary>
        Protected = 1
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