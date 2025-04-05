using System.Collections;
using System.Diagnostics.CodeAnalysis;

using lvlup.DataFerry.Caching.Events;

using Microsoft.Extensions.Caching.Memory;

namespace lvlup.DataFerry.Caching.Abstractions;

/// <summary>
/// A synchronous in-memory object cache.
/// </summary>
/// <typeparam name="TKey">Type of keys stored in the cache.</typeparam>
/// <typeparam name="TValue">Type of values stored in the cache.</typeparam>
public abstract class ReplacementPolicyCache<TKey, TValue> : IEnumerable<KeyValuePair<TKey, TValue>>
    where TKey : notnull
{
    #region Fields

    /// <summary>
    /// Gets the name of the cache instance.
    /// </summary>
    /// <remarks>
    /// This name is used to identity this cache when publishing telemetry.
    /// </remarks>
    public abstract string Name { get; }

    /// <summary>
    /// Gets the capacity of the cache, which represents the maximum number of items maintained by the cache at any one time.
    /// </summary>
    public abstract int Capacity { get; }

    #endregion
    #region Core Operations

    /// <summary>
    /// Tries to get a value from the cache.
    /// </summary>
    /// <param name="key">Key identifying the requested value.</param>
    /// <param name="value">Value associated with the key or default when not found.</param>
    /// <returns>
    /// <see langword="true"/> when the value was found, <see langword="false"/> otherwise.
    /// </returns>
    public abstract bool TryGet(TKey key, [MaybeNullWhen(false)] out TValue value);

    /// <summary>
    /// Sets a value in the cache.
    /// </summary>
    /// <remarks>
    /// The value's time to expire is set to the global default value defined for this cache instance.
    /// </remarks>
    /// <param name="key">Key identifying the value.</param>
    /// <param name="value">Value to associate with the key.</param>
    public abstract void Set(TKey key, TValue value);

    /// <summary>
    /// Sets a value in the cache.
    /// </summary>
    /// <remarks>
    /// After time to expire has passed, a value is not retrievable from the cache.
    /// At the same time cache might keep the root for it for some time.
    /// </remarks>
    /// <param name="key">Key identifying the value.</param>
    /// <param name="value">Value to cache on the heap.</param>
    /// <param name="timeToExpire">Amount of time the value is valid, after which it should be removed from the cache.</param>
    /// <exception cref="ArgumentOutOfRangeException">If <paramref name="timeToExpire"/> is less than 1 millisecond.</exception>
    public abstract void Set(TKey key, TValue value, TimeSpan timeToExpire);

    /// <summary>
    /// Gets a value or sets it if doesn't exist. Avoids closure allocation by accepting explicit state.
    /// </summary>
    /// <remarks>
    /// The value's time to expire is set to the global default value defined for this cache instance.
    /// </remarks>
    /// <typeparam name="TState">Type of the state passed to the function.</typeparam>
    /// <param name="key">Key identifying the value.</param>
    /// <param name="state">State passed to the factory function.</param>
    /// <param name="factory">A function used to create a new value if the key is not found in the cache. It returns the value to be cached.</param>
    /// <returns>
    /// Data retrieved from the cache or created by the passed factory function.
    /// </returns>
    public abstract TValue GetOrSet<TState>(TKey key, TState state, Func<TKey, TState, TValue> factory);

    /// <summary>
    /// Gets a value or sets one if it doesn't exist. Avoids closure allocation by accepting explicit state.
    /// </summary>
    /// <remarks>
    /// After time to expire has passed, a value is not retrievable from the cache.
    /// At the same time cache might keep the root for it for some time.
    /// </remarks>
    /// <typeparam name="TState">The type of the state object passed to the factory function.</typeparam>
    /// <param name="key">The key identifying the cached value.</param>
    /// <param name="state">An additional state object passed to the factory function.</param>
    /// <param name="factory">
    /// A function used to create a new value if the key is not found in the cache. The function returns a tuple where the
    /// first item is the value to cache, and the second item is a <see cref="TimeSpan"/> representing the duration for which
    /// the value remains valid in the cache before expiring.
    /// </param>
    /// <returns>
    /// The value associated with the key, either retrieved from the cache or created by the factory function.
    /// </returns>
    public abstract TValue GetOrSet<TState>(TKey key, TState state, Func<TKey, TState, (TValue value, TimeSpan timeToExpire)> factory);

    /// <summary>
    /// Gets a value or sets one if it doesn't exist. May allocate a closure.
    /// </summary>
    /// <remarks>
    /// The value's time to expire is set to the global default value defined for this cache instance.
    /// </remarks>
    /// <param name="key">Key identifying the value.</param>
    /// <param name="factory">A function used to create a new value if the key is not found in the cache. It returns the value to be cached.</param>
    /// <returns>
    /// Data retrieved from the cache or created by the passed factory function.
    /// </returns>
    public abstract TValue GetOrSet(TKey key, Func<TKey, TValue> factory);

    /// <summary>
    /// Gets a value or sets one if it doesn't exist. May allocate a closure.
    /// </summary>
    /// <remarks>
    /// After time to expire has passed, a value is not retrievable from the cache.
    /// At the same time cache might keep the root for it for some time.
    /// </remarks>
    /// <param name="key">Key identifying the value.</param>
    /// <param name="factory">
    /// A function used to create a new value if the key is not found in the cache. The function returns a tuple where the
    /// first item is the value to cache, and the second item is a <see cref="TimeSpan"/> representing the duration for which
    /// the value remains valid in the cache before expiring.
    /// </param>
    /// <returns>
    /// Data retrieved from the cache or created by the passed factory function.
    /// </returns>
    public abstract TValue GetOrSet(TKey key, Func<TKey, (TValue value, TimeSpan timeToExpire)> factory);

    /// <summary>
    /// Gets the current number of items in the cache.
    /// </summary>
    /// <returns>
    /// This method is inherently imprecise as threads may be asynchronously adding
    /// or removing items. In addition, this method may be relatively costly, so avoid
    /// calling it in hot paths.
    /// </returns>
    public abstract int GetCount();

    /// <summary>
    /// Attempts to remove the value that has the specified key.
    /// </summary>
    /// <param name="key">Key identifying the value to remove.</param>
    /// <returns>
    /// <see langword="true"/> if the value was removed, and <see langword="false"/> if the key was not found.
    /// </returns>
    public abstract bool Remove(TKey key);

    /// <summary>
    /// Attempts to remove the specified key-value pair from the cache.
    /// </summary>
    /// <param name="key">Key identifying the value to remove.</param>
    /// <param name="value">The value associated with the key to remove.</param>
    /// <returns>
    /// <see langword="true"/> if the specified key-value pair was found and removed;
    /// otherwise, <see langword="false"/>.
    /// </returns>
    /// <remarks>
    /// This method checks both the key and the associated value for a match before removal.
    /// If the specified key exists but is associated with a different value, the cache remains unchanged.
    /// </remarks>
    public abstract bool Remove(TKey key, TValue value);

    /// <summary>
    /// Removes all expired entries from the cache.
    /// </summary>
    /// <remarks>
    /// Some implementations perform lazy cleanup of cache resources. This call is a hint to
    /// ask the cache to try and synchronously do some cleanup.
    /// </remarks>
    public abstract void RemoveExpiredEntries();

    /// <summary>
    /// Returns an enumerator that iterates through the items in the cache.
    /// </summary>
    /// <returns>
    /// An enumerator for the cache.
    /// </returns>
    /// <remarks>
    /// This can be a slow API and is intended for use in debugging and diagnostics, avoid using in production scenarios.
    ///
    /// The enumerator returned from the cache is safe to use concurrently with
    /// reads and writes, however it does not represent a moment-in-time snapshot.
    /// The contents exposed through the enumerator may contain modifications
    /// made after <see cref="GetEnumerator"/> was called.
    /// </remarks>
    public abstract IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator();

    /// <summary>
    /// Returns an enumerator that iterates through the items in the cache.
    /// </summary>
    /// <returns>
    /// An enumerator for the cache.
    /// </returns>
    /// <remarks>
    /// This can be a slow API and is intended for use in debugging and diagnostics, avoid using in production scenarios.
    ///
    /// The enumerator returned from the cache is safe to use concurrently with
    /// reads and writes, however it does not represent a moment-in-time snapshot.
    /// The contents exposed through the enumerator may contain modifications
    /// made after <see cref="GetEnumerator"/> was called.
    /// </remarks>
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    #endregion
    #region Events
    /*

    /// <summary>
    /// Reasons for cache item eviction.
    /// </summary>
    public enum ReplacementPolicyEvictionReason
    {
        /// <summary> The item was explicitly removed via the Remove method. </summary>
        Removed,

        /// <summary> The item was automatically removed because it expired. </summary>
        Expired,

        /// <summary> The item was automatically removed to make space due to capacity limits. </summary>
        Capacity,

        /// <summary> The item was overwritten by a new value with the same key. </summary>
        Replaced // Added for clarity
    }

    public event EventHandler<CacheHitEventArgs<TKey>>? CacheHit;

    public event EventHandler<CacheMissEventArgs<TKey>>? CacheMiss;

    public event EventHandler<CacheItemAddedEventArgs<TKey, TValue>>? ItemAdded;

    public event EventHandler<CacheItemRemovedEventArgs<TKey>>? ItemRemoved;

    public event EventHandler<CacheItemEvictedEventArgs<TKey>>? ItemEvicted;

    public event EventHandler<CacheItemExpiredEventArgs<TKey>>? ItemExpired;

    protected virtual void OnCacheHit(TKey key) => CacheHit?.Invoke(this, new CacheHitEventArgs<TKey>(key));
    protected virtual void OnCacheMiss(TKey key) => CacheMiss?.Invoke(this, new CacheMissEventArgs<TKey>(key));
    protected virtual void OnItemAdded(TKey key, TValue value) => ItemAdded?.Invoke(this, new CacheItemAddedEventArgs<TKey, TValue>(key, value));
    protected virtual void OnItemRemoved(TKey key) => ItemRemoved?.Invoke(this, new CacheItemRemovedEventArgs<TKey>(key));
    protected virtual void OnItemEvicted(TKey key, ReplacementPolicyEvictionReason reason)
        => ItemEvicted?.Invoke(this, new CacheItemEvictedEventArgs<TKey>(key, reason));

    */
    #endregion
}