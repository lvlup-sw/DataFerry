using lvlup.DataFerry.Caching.Results;

namespace lvlup.DataFerry.Caching.Abstractions;

/// <summary>
/// An asynchronous, potentially distributed, object cache, extending the base synchronous cache functionality.
/// Useful for implementations that might involve I/O or wish to provide an async-friendly API aligned with patterns like HybridCache.
/// </summary>
/// <typeparam name="TKey">Type of keys stored in the cache.</typeparam>
/// <typeparam name="TValue">Type of values stored in the cache.</typeparam>
public abstract class DistributedReplacementPolicyCache<TKey, TValue> : ReplacementPolicyCache<TKey, TValue>
     where TKey : notnull
{
    /// <summary>
    /// Asynchronously tries to get a value from the cache.
    /// </summary>
    /// <param name="key">Key identifying the requested value.</param>
    /// <param name="token">Optional <see cref="CancellationToken"/>.</param>
    /// <returns>
    /// A <see cref="ValueTask{TResult}"/> representing the asynchronous operation.
    /// The task result contains a <see cref="TryGetResult{TValue}"/> indicating success and the retrieved value.
    /// </returns>
    // Renamed return type from CacheTryGetResult to TryGetResult
    public abstract ValueTask<TryGetResult<TValue>> TryGetValueAsync(TKey key, CancellationToken token = default);

    /// <summary>
    /// Asynchronously sets a value in the cache.
    /// </summary>
    /// <remarks>
    /// The value's time to expire is set to the global default value defined for this cache instance.
    /// </remarks>
    /// <param name="key">Key identifying the value.</param>
    /// <param name="value">Value to associate with the key.</param>
    /// <param name="token">Optional <see cref="CancellationToken"/>.</param>
    /// <returns>A <see cref="ValueTask"/> representing the asynchronous operation.</returns>
    public abstract ValueTask SetAsync(TKey key, TValue value, CancellationToken token = default);

    /// <summary>
    /// Asynchronously sets a value in the cache.
    /// </summary>
    /// <param name="key">Key identifying the value.</param>
    /// <param name="value">Value to cache.</param>
    /// <param name="timeToExpire">Amount of time the value is valid.</param>
    /// <param name="token">Optional <see cref="CancellationToken"/>.</param>
    /// <returns>A <see cref="ValueTask"/> representing the asynchronous operation.</returns>
    /// <exception cref="ArgumentOutOfRangeException">If <paramref name="timeToExpire"/> is less than 1 millisecond.</exception>
    public abstract ValueTask SetAsync(TKey key, TValue value, TimeSpan timeToExpire, CancellationToken token = default);

    /// <summary>
    /// Asynchronously gets a value or sets it if it doesn't exist. Avoids closure allocation by accepting explicit state.
    /// </summary>
    /// <remarks>
    /// The value's time to expire is set to the global default value defined for this cache instance.
    /// </remarks>
    /// <typeparam name="TState">Type of the state passed to the function.</typeparam>
    /// <param name="key">Key identifying the value.</param>
    /// <param name="state">State passed to the factory function.</param>
    /// <param name="factory">An asynchronous function used to create a new value if the key is not found.</param>
    /// <param name="token">Optional <see cref="CancellationToken"/>.</param>
    /// <returns>
    /// A <see cref="ValueTask{TResult}"/> representing the asynchronous operation.
    /// The task result contains the data retrieved from the cache or created by the factory function.
    /// </returns>
    public abstract ValueTask<TValue> GetOrSetAsync<TState>(TKey key, TState state, Func<TKey, TState, CancellationToken, Task<TValue>> factory, CancellationToken token = default);

    /// <summary>
    /// Asynchronously gets a value or sets it if it doesn't exist. Avoids closure allocation by accepting explicit state.
    /// </summary>
    /// <typeparam name="TState">The type of the state object passed to the factory function.</typeparam>
    /// <param name="key">The key identifying the cached value.</param>
    /// <param name="state">An additional state object passed to the factory function.</param>
    /// <param name="factory">
    /// An asynchronous function used to create a new value if the key is not found. The function returns a task resulting in a tuple
    /// containing the value and its time to expire.
    /// </param>
    /// <param name="token">Optional <see cref="CancellationToken"/>.</param>
    /// <returns>
    /// A <see cref="ValueTask{TResult}"/> representing the asynchronous operation.
    /// The task result contains the value associated with the key.
    /// </returns>
    public abstract ValueTask<TValue> GetOrSetAsync<TState>(TKey key, TState state, Func<TKey, TState, CancellationToken, Task<(TValue value, TimeSpan timeToExpire)>> factory, CancellationToken token = default);

    /// <summary>
    /// Asynchronously gets a value or sets it if it doesn't exist. May allocate a closure.
    /// </summary>
    /// <remarks>
    /// The value's time to expire is set to the global default value defined for this cache instance.
    /// </remarks>
    /// <param name="key">Key identifying the value.</param>
    /// <param name="factory">An asynchronous function used to create a new value if the key is not found.</param>
    /// <param name="token">Optional <see cref="CancellationToken"/>.</param>
    /// <returns>
    /// A <see cref="ValueTask{TResult}"/> representing the asynchronous operation.
    /// The task result contains the data retrieved from the cache or created by the factory function.
    /// </returns>
    public abstract ValueTask<TValue> GetOrSetAsync(TKey key, Func<TKey, CancellationToken, Task<TValue>> factory, CancellationToken token = default);

    /// <summary>
    /// Asynchronously gets a value or sets it if it doesn't exist. May allocate a closure.
    /// </summary>
    /// <param name="key">Key identifying the value.</param>
    /// <param name="factory">
    /// An asynchronous function used to create a new value if the key is not found. The function returns a task resulting in a tuple
    /// containing the value and its time to expire.
    /// </param>
    /// <param name="token">Optional <see cref="CancellationToken"/>.</param>
    /// <returns>
    /// A <see cref="ValueTask{TResult}"/> representing the asynchronous operation.
    /// The task result contains the data retrieved from the cache or created by the factory function.
    /// </returns>
    public abstract ValueTask<TValue> GetOrSetAsync(TKey key, Func<TKey, CancellationToken, Task<(TValue value, TimeSpan timeToExpire)>> factory, CancellationToken token = default);

    /// <summary>
    /// Asynchronously attempts to remove the value that has the specified key.
    /// </summary>
    /// <param name="key">Key identifying the value to remove.</param>
    /// <param name="token">Optional <see cref="CancellationToken"/>.</param>
    /// <returns>
    /// A <see cref="ValueTask{TResult}"/> representing the asynchronous operation.
    /// The task result is <see langword="true"/> if the value was removed, and <see langword="false"/> otherwise.
    /// </returns>
    public abstract ValueTask<bool> RemoveAsync(TKey key, CancellationToken token = default);

    /// <summary>
    /// Asynchronously attempts to remove the specified key-value pair from the cache.
    /// </summary>
    /// <param name="key">Key identifying the value to remove.</param>
    /// <param name="value">The value associated with the key to remove.</param>
    /// <param name="token">Optional <see cref="CancellationToken"/>.</param>
    /// <returns>
    /// A <see cref="ValueTask{TResult}"/> representing the asynchronous operation.
    /// The task result is <see langword="true"/> if the pair was found and removed; otherwise, <see langword="false"/>.
    /// </returns>
    public abstract ValueTask<bool> RemoveAsync(TKey key, TValue value, CancellationToken token = default);

    /// <summary>
    /// Asynchronously removes all expired entries from the cache.
    /// </summary>
    /// <param name="token">Optional <see cref="CancellationToken"/>.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
    public abstract Task RemoveExpiredEntriesAsync(CancellationToken token = default); // Task likely sufficient here
}