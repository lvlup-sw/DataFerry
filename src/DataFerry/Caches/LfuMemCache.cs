using lvlup.DataFerry.Buffers;
using lvlup.DataFerry.Orchestrators.Contracts;
using System.Collections;
using System.Collections.Concurrent;
using System.ComponentModel;
using Microsoft.Extensions.Logging;

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
    private readonly ConcurrentLinkedList<KeyLocation> _probation;
    private readonly ConcurrentLinkedList<KeyLocation> _protected;

    // Frequency queue tracking LFU candidates
    private readonly ConcurrentPriorityQueue<int, TKey> _evictionWindow;

    // Frequency histogram
    private readonly CountMinSketch<TKey> _cms;

    // Background processes
    private readonly ConcurrentQueue<TKey> _recentEntries;
    private readonly BoundedBuffer<BufferItem> _writeBuffer;
    private readonly ITaskOrchestrator _taskOrchestrator;
    private readonly Timer _cleanUpTimer;
    
    // Logging/Tracing
    private readonly ILogger<LfuMemCache<TKey, TValue>> _logger;

    // Sizes
    private const int BufferSize = 8;
    private const int RecentWindowMaxSize = 5;
    private readonly int _maxWindowSize;
    private readonly int _maxProbationSize;
    private readonly int _maxProtectedSize;

    // Semaphores
    private static readonly SemaphoreSlim @evictionSemaphore = new(1, 1);
    private static readonly SemaphoreSlim @clearSemaphore = new(1, 1);
    
    // CancellationTokens
    private readonly CancellationTokenSource _cts = new();

    /// <inheritdoc/>
    public int MaxSize { get; set; } // Set needs to update the internal objects

    /// <summary>
    /// Initializes a new instance of <see cref="LfuMemCache{TKey, TValue}"/>
    /// </summary>
    /// <param name="taskOrchestrator">The scheduler used to orchestrate background tasks.</param>
    /// <param name="logger">The logger to use within the cache.</param>
    /// <param name="maxSize">The maximum number of items allowed in the cache.</param>        
    /// <param name="cleanupJobInterval">Cleanup interval in milliseconds; default is 10000.</param>
    public LfuMemCache(ITaskOrchestrator taskOrchestrator, ILogger<LfuMemCache<TKey, TValue>> logger, int maxSize = 10000, int cleanupJobInterval = 10000)
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
        _logger = logger;
        // We create a concurrent priority queue to hold LFU candidates
        // This comparer gives lower frequency items higher priority
        _evictionWindow = new(
            _taskOrchestrator,
            Comparer<int>.Create((x, y) => x.CompareTo(y)), 
            maxSize: _maxWindowSize,
            allowDuplicatePriorities: false);

        // Timer to trigger TTL-based eviction
        // Consider replacing with a hierarchical TimerWheel
        _cleanUpTimer = new Timer(
            _ => _taskOrchestrator.Run(EvictExpired),
            default,
            TimeSpan.FromMilliseconds(cleanupJobInterval),
            TimeSpan.FromMilliseconds(cleanupJobInterval));

        // Run the background task for the WriteBuffer
        _taskOrchestrator.Run(() => DrainBufferIntoCache(_cts.Token));
    }

    #region Background Tasks
    
    /// <summary>
    /// Immediately removes expired items from the cache.
    /// Typically unnecessary, as item retrieval already handles expiration checks.
    /// </summary>
    public Task EvictExpired()
    {
        if (!Monitor.TryEnter(_cleanUpTimer)) return Task.CompletedTask;

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
                    if (currTime > pair.Value.ExpirationTicks)
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
        
        return Task.CompletedTask;
    }

    /// <summary>
    /// Evicts the least frequently used (LFU) items from the cache.
    /// </summary>
    internal async Task EvictLFUAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            await @evictionSemaphore.WaitAsync(token).ConfigureAwait(false);
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
                @evictionSemaphore.Release();
            }
        }

        // Reset the eviction task status when the task completes
        //Interlocked.Exchange(ref _evictionJobStarted, 0);
    }

    //private bool TriggerEvictionJob() => _currentSize >= MaxSize && Interlocked.CompareExchange(ref _evictionJobStarted, 1, 0) == 0;
    //private bool TerminateEvictionJob() => _currentSize <= MaxSize && Interlocked.CompareExchange(ref _evictionJobStarted, 0, 1) == 1;

    /// <summary>
    /// Flushes the items from the write buffer into the appropriate cache segments.
    /// </summary>
    private Task DrainBufferIntoCache(CancellationToken cancellationToken)
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                _writeBuffer.NotEmptyEvent.Wait(cancellationToken);

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
                            _protected.TryInsert(new KeyLocation(item.Key, segment));
                            break;
                        case CacheSegment.Probation:
                            _probation.TryInsert(new KeyLocation(item.Key, segment));
                            break;
                        case CacheSegment.Invalid:
                            continue;
                        default:
                            throw new InvalidEnumArgumentException("Could not determine CacheSegment.");
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
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("DrainBufferIntoCache task received cancellation signal.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred in DrainBufferIntoCache task.");
        }
        
        return Task.CompletedTask;
    }

    #endregion
    #region Core Operations
    
    /// <inheritdoc/>
    // No lock count: https://arbel.net/2013/02/03/best-practices-for-using-concurrentdictionary/ 
    public int Count => _cache.Skip(0).Count() + _writeBuffer.Count;

    /// <inheritdoc/>
    public void Clear()
    {
        @clearSemaphore.Wait();
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
            @clearSemaphore.Release();
        }
    }    
    
    /// <inheritdoc/>
    public bool TryGet(TKey key, out TValue value)
    {
        value = default!;

        // Not found or expired
        if (!_cache.TryGetValue(key, out var ttlValue) || ttlValue.IsExpired())
        {
            // Utilizes atomic conditional removal to eliminate the need for locks
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
        if (!Admit()) return;

        // Add to WriteBuffer
        // Need to account for full buffer case
        _writeBuffer.TryAdd(
            new BufferItem(
                key,
                new TtlValue(value, ttl)
            ));

        // Update frequency in sketch
        _cms.Increment(key);
        return;

        // Admit the item to the cache
        bool Admit()
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
        if (_cache.TryRemove(key, out _))
        {
            var keyLocationToRemove = new KeyLocation(key, default);

            RemoveFromSegment(_probation, keyLocationToRemove);
            RemoveFromSegment(_protected, keyLocationToRemove);
        }    
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
    
    #endregion
    #region Helper Methods
    
    private void EnsureAvailableSpaceInSegment(CacheSegment segment, BufferItem item)
    {
        throw new NotImplementedException();
    }

    private CacheSegment DetermineCacheSegment(TtlValue value)
    {
        throw new NotImplementedException();
    }    
    
    /// <summary>
    /// Attempts to find and remove a <see cref="KeyLocation"/> from the specified list.
    /// </summary>
    /// <param name="list">The list to remove the <see cref="KeyLocation"/> from.</param>
    /// <param name="keyLocation">The <see cref="KeyLocation"/> to remove, used to find the corresponding node in the list.</param>
    private static void RemoveFromSegment(ConcurrentLinkedList<KeyLocation> list, KeyLocation keyLocation)
    {
        list.Find(keyLocation)?.Pipe(node => list.TryRemove(node.Key));
    }

    #endregion
    #region Internal Types
    
    /// <summary>
    /// Represents a value with an associated time-to-live (TTL) for expiration.
    /// </summary>
    /// <remarks>
    /// This is the object stored within the physical cache.
    /// </remarks>
    internal readonly struct TtlValue : IEquatable<TtlValue>
    {
        /// <summary>
        /// The stored value.
        /// </summary>
        public readonly TValue Value;

        /// <summary>
        /// The tick count at which this value expires.
        /// </summary>
        public readonly long ExpirationTicks;

        /// <summary>
        /// Initializes a new instance of the <see cref="TtlValue"/> class.
        /// </summary>
        /// <param name="value">The value to store.</param>
        /// <param name="ttl">The time-to-live (TTL) for the value.</param>
        public TtlValue(TValue value, TimeSpan ttl)
        {
            Value = value;
            ExpirationTicks = Environment.TickCount64 + (long)ttl.TotalMilliseconds;
        }

        /// <summary>
        /// Determines if this value has expired.
        /// </summary>
        /// <returns><c>true</c> if the value has expired (i.e., the current tick count is greater than <see cref="ExpirationTicks"/>); otherwise, <c>false</c>.</returns>
        public bool IsExpired() => Environment.TickCount64 > ExpirationTicks;

        /// <summary>
        /// Indicates whether this value is valid and unexpired.
        /// </summary>
        public bool HasValue => !IsNullOrExpired;

        /// <summary>
        /// Gets a value indicating whether the value is null or expired.
        /// </summary>
        private bool IsNullOrExpired => Value is null || IsExpired();

        /// <summary>
        /// Determines whether the specified <see cref="TtlValue"/> is equal to the current <see cref="TtlValue"/>.
        /// </summary>
        /// <param name="other">The <see cref="TtlValue"/> to compare with the current <see cref="TtlValue"/>.</param>
        /// <returns>
        /// <c>true</c> if the specified <see cref="TtlValue"/> is equal to the current <see cref="TtlValue"/>; otherwise, <c>false</c>.
        /// Equality is determined by comparing both the <see cref="Value"/> and <see cref="ExpirationTicks"/> properties.
        /// </returns>
        public bool Equals(TtlValue other)
        {
            return EqualityComparer<TValue>.Default.Equals(Value, other.Value) 
                   && ExpirationTicks == other.ExpirationTicks;
        }

        /// <summary>
        /// Determines whether the specified object is equal to the current object.
        /// </summary>
        /// <param name="obj">The object to compare with the current object.</param>
        /// <returns>
        /// <c>true</c> if the specified object is equal to the current object; otherwise, <c>false</c>.
        /// This method checks if the provided object is a <see cref="TtlValue"/> and then delegates to the strongly-typed <see cref="Equals(TtlValue)"/> method.
        /// </returns>
        public override bool Equals(object? obj)
        {
            return obj is TtlValue other 
                   && Equals(other);
        }

        /// <summary>
        /// Serves as the default hash function.
        /// </summary>
        /// <returns>A hash code for the current object. The hash code is generated by combining the hash codes of the <see cref="Value"/> and <see cref="ExpirationTicks"/> properties.</returns>
        public override int GetHashCode()
        {
            return HashCode.Combine(Value, ExpirationTicks);
        }
    }

    /// <summary>
    /// Represents the location of a key within the cache, specifically indicating the segment (Probation or Protected) to which the key belongs.
    /// </summary>
    /// <remarks>
    /// This class is used to track the logical position of a key in the cache.
    /// </remarks>
    internal class KeyLocation : IEquatable<KeyLocation>
    {
        /// <summary>
        /// The key associated with this location.
        /// </summary>
        public TKey Key { get; }

        /// <summary>
        /// The cache segment where the key is currently located.
        /// </summary>
        public CacheSegment Segment { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="KeyLocation"/> class.
        /// </summary>
        /// <param name="key">The key.</param>
        /// <param name="segment">The cache segment.</param>
        public KeyLocation(TKey key, CacheSegment segment)
        {
            Key = key;
            Segment = segment;
        }

        /// <summary>
        /// Determines whether the specified <see cref="KeyLocation"/> is equal to the current <see cref="KeyLocation"/>.
        /// </summary>
        /// <param name="other">The <see cref="KeyLocation"/> to compare with the current <see cref="KeyLocation"/>.</param>
        /// <returns>
        /// <c>true</c> if the specified <see cref="KeyLocation"/> is equal to the current <see cref="KeyLocation"/>; otherwise, <c>false</c>.
        /// Equality is determined by comparing the <see cref="Key"/> properties.
        /// </returns>
        public bool Equals(KeyLocation? other)
        {
            if (other is null) return false;
            return ReferenceEquals(this, other) 
                   || EqualityComparer<TKey>.Default.Equals(Key, other.Key);
        }

        /// <summary>
        /// Determines whether the specified object is equal to the current object.
        /// </summary>
        /// <param name="obj">The object to compare with the current object.</param>
        /// <returns>
        /// <c>true</c> if the specified object is equal to the current object; otherwise, <c>false</c>.
        /// This method checks if the provided object is a <see cref="KeyLocation"/> and then delegates to the strongly-typed <see cref="Equals(KeyLocation)"/> method.
        /// </returns>
        public override bool Equals(object? obj)
        {
            return obj is KeyLocation other 
                   && Equals(other);
        }

        /// <summary>
        /// Serves as the default hash function.
        /// </summary>
        /// <returns>A hash code for the current object. The hash code is generated from the <see cref="Key"/> property.</returns>
        public override int GetHashCode()
        {
            return EqualityComparer<TKey>.Default.GetHashCode(Key);
        }

        public static bool operator ==(KeyLocation left, KeyLocation right)
        {
            return Equals(left, right);
        }

        public static bool operator !=(KeyLocation left, KeyLocation right)
        {
            return !Equals(left, right);
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
        public bool HasValue => !IsNullOrEmpty;

        /// <summary>
        /// Gets a value indicating whether the key or value is null or empty.
        /// </summary>
        private bool IsNullOrEmpty
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
    internal enum CacheSegment
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
    
    #endregion
    #region IDisposable

    private bool _disposedValue;

    public void Dispose()
    {
        // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        if (_disposedValue) return;

        if (disposing)
        {
            _cts.Cancel();
            // Safe disposal even if _cleanUpTimer is null
            _cleanUpTimer?.Dispose();
            _cts.Dispose();
        }

        _disposedValue = true;
    }

    #endregion
}