using System.Diagnostics.CodeAnalysis;
using lvlup.DataFerry.Caching.Abstractions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Primitives;

namespace lvlup.DataFerry.Caching.Adapters;

/// <summary>
/// Adapts a <see cref="ReplacementPolicyCache{TKey, TValue}"/> instance
/// to the standard IMemoryCache interface, providing only synchronous operations.
/// </summary>
/// <remarks>
/// Note: While this adapter implements IMemoryCache, standard async extension
/// methods like GetOrCreateAsync may not function as expected or efficiently
/// as the underlying cache provides no asynchronous methods.
/// </remarks>
public class MemoryCacheAdapter : IMemoryCache
{
    #region Global Variables

    private readonly ReplacementPolicyCache<object, object> _cache;

    #endregion
    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="MemoryCacheAdapter"/> class.
    /// </summary>
    /// <param name="replacementPolicyCache">The underlying synchronous cache implementation to wrap.</param>
    /// <exception cref="ArgumentNullException">Thrown if replacementPolicyCache is null.</exception>
    public MemoryCacheAdapter(ReplacementPolicyCache<object, object> replacementPolicyCache)
    {
        ArgumentNullException.ThrowIfNull(replacementPolicyCache, nameof(replacementPolicyCache));

        _cache = replacementPolicyCache;
    }

    #endregion
    #region Core Operations

    /// <inheritdoc />
    public bool TryGetValue(object key, [MaybeNullWhen(false)] out object value)
    {
        if (_cache.TryGet(key, out object? underlyingValue))
        {
            value = underlyingValue!;
            return true;
        }
        else
        {
            value = default;
            return false;
        }
    }

    /// <inheritdoc />
    public ICacheEntry CreateEntry(object key)
    {
        // The logic remains the same: return an ICacheEntry adapter
        // which will call the synchronous _cache.Set upon disposal.
        return new CacheEntryAdapter(key, this);
    }

    /// <inheritdoc />
    public void Remove(object key)
    {
        // Use the synchronous Remove from the wrapped cache
        _cache.Remove(key);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Dispose underlying cache resources.
    /// </summary>
    protected virtual void Dispose(bool disposing)
    {
        if (disposing)
        {

        }
    }

    #endregion
    #region Internal Data Structures

    /// <summary>
    /// Internal implementation of ICacheEntry used by MemoryCacheAdapter.
    /// Collects configuration and commits to the underlying cache upon disposal.
    /// </summary>
    internal sealed class CacheEntryAdapter : ICacheEntry
    {
        private readonly MemoryCacheAdapter _adapter;

        private bool _disposed = false;

        public object Key { get; }

        public object? Value { get; set; }

        public DateTimeOffset? AbsoluteExpiration { get; set; }

        public TimeSpan? AbsoluteExpirationRelativeToNow { get; set; }

        public TimeSpan? SlidingExpiration { get; set; }

        public IList<IChangeToken> ExpirationTokens { get; } = new List<IChangeToken>();

        public IList<PostEvictionCallbackRegistration> PostEvictionCallbacks { get; } = new List<PostEvictionCallbackRegistration>();

        public CacheItemPriority Priority { get; set; } = CacheItemPriority.Normal;

        public long? Size { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="CacheEntryAdapter"/> class.
        /// </summary>
        internal CacheEntryAdapter(object key, MemoryCacheAdapter adapter)
        {
            Key = key ?? throw new ArgumentNullException(nameof(key));
            _adapter = adapter;
        }

        /// <summary>
        /// Commits the entry to the underlying cache when disposed.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // Don't cache null values explicitly? Or handle based on TValue?
            if (Value == null)
            {
                // Or perhaps remove if value is set back to null? Depends on desired semantics
                return;
            }

            // Determine expiration
            TimeSpan? effectiveExpiration = AbsoluteExpirationRelativeToNow;
            if (!effectiveExpiration.HasValue && AbsoluteExpiration.HasValue)
            {
                 effectiveExpiration = AbsoluteExpiration.Value - DateTimeOffset.UtcNow;
                 if (effectiveExpiration <= TimeSpan.Zero) return;
            }

            if (effectiveExpiration.HasValue)
            {
                 if (effectiveExpiration.Value <= TimeSpan.Zero) return;

                 // Call synchronous Set with expiration
                 _adapter._cache.Set(Key, Value, effectiveExpiration.Value);
            }
            else
            {
                 // Call synchronous Set with default expiration
                 _adapter._cache.Set(Key, Value);
            }

            // TODO: Handle other ICacheEntry features if possible/desired
            // R: Leaning towards simply throwing a 'NotSupportedException' so we are explicit
            // - Implement logic for SlidingExpiration (maybe using ExtendTimeToEvictAfterHit?)
            // - Handle ExpirationTokens (would require significant changes to underlying cache)
            // - Handle PostEvictionCallbacks (would require significant changes)
            // - Handle Priority/Size (would require significant changes)
        }
    }

    #endregion
}