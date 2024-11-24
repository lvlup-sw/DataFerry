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
    private readonly ConcurrentLinkedList<TKey> _window;
    private readonly ConcurrentLinkedList<TKey> _probation;
    private readonly ConcurrentLinkedList<TKey> _protected;

    // Frequency queue tracking LFU candidates
    private readonly ConcurrentPriorityQueue<int, TKey> _frequencyQueue;

    // Frequency histogram
    private readonly CountMinSketch<TKey> _cms;

    // Background processes
    private readonly ThreadBuffer<BufferItem> _writeBuffer;
    private readonly ITaskOrchestrator _taskOrchestrator;
    private readonly Timer _cleanUpTimer;

    // Signalers and calcs
    private CancellationTokenSource? _evictionCancellationToken;
    private long _evictionJobStarted = 0;
    private const int BufferSize = 8;
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
        _window = new();
        _probation = new();
        _protected = new();

        // Helper data structures
        _cms = new(MaxSize);
        _writeBuffer = new(BufferSize);
        _taskOrchestrator = taskOrchestrator;
        // We create a concurrent priority queue to hold LFU candidates
        // This comparer gives lower frequency items higher priority
        _frequencyQueue = new(
            _taskOrchestrator,
            Comparer<int>.Create((x, y) => x.CompareTo(y)), 
            maxSize: MaxSize);

        // Timer to trigger TTL-based eviction
        _cleanUpTimer = new Timer(
            _ => _taskOrchestrator.Run(EvictExpired),
            default,
            TimeSpan.FromMilliseconds(cleanupJobInterval),
            TimeSpan.FromMilliseconds(cleanupJobInterval));

        // Subscribe to the ThresholdReached event in the constructor
        _writeBuffer.ThresholdReached += (sender, args) =>
        {
            // Get and process items
            foreach (var item in _writeBuffer.ExtractItems())
            {
                // Process the item
                Console.WriteLine($"Key: {item.Key}, Value: {item.Value}");
            }
        };
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
                int evictionBatch = (int) Math.Sqrt(_frequencyQueue.GetCount());
                for (int i = 0; i < evictionBatch; i++)
                {
                    _frequencyQueue.TryDeleteMin(out var lfuItem);
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
    public int Count => _cache.Skip(0).Count();

    /// <inheritdoc/>
    public void Clear()
    {
        _clearSemaphore.Wait();
        try
        {
            _cache.Clear();
            //_window.Clear();
            //_probation.Clear();
            //_protected.Clear();
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
        // Update frequencies
        _cms.Increment(key);
        _frequencyQueue.TryAdd(_cms.EstimateFrequency(key), key);
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
        //bool isInWindow = _window.ContainsKey(key);
        //var targetCache = isInWindow || _cache.ContainsKey(key) ? _cache : _window;

        // Add a new key-value pair or update an existing one
        _cache.AddOrUpdate(key, new TtlValue(value, ttl), (k, oldValue) => new TtlValue(value, ttl));
        //if (isInWindow) _window.TryRemove(key, out _);

        // Update the frequency of the key
        _cms.Increment(key);
        Interlocked.Increment(ref _currentSize);
        _frequencyQueue.TryAdd(_cms.EstimateFrequency(key), key);

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
            var ttlValue = new TtlValue(value.Value, ttl);
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

    private readonly struct BufferItem
    {
        public readonly TKey Key;
        public readonly TtlValue Value;

        public BufferItem(TKey key, TtlValue value)
        {
            Key = key;
            Value = value;
        }

        public readonly bool HasValue => !IsNullOrEmpty;

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