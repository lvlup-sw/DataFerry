using Microsoft.Extensions.Caching.Distributed;
using Polly.Wrap;
using StackExchange.Redis;
using System.Buffers;

namespace lvlup.DataFerry.Caches.Abstractions
{
    /// <summary>
    /// A contract for interacting with a distributed cache of serialized values that supports low allocation data transfers.
    /// </summary>
    /// <remarks>Based on <see cref="IBufferDistributedCache"/>.</remarks>
    public interface ISparseDistributedCache
    {
        /// <summary>
        /// Attempts to retrieve an existing cache item synchronously.
        /// </summary>
        /// <param name="key">The unique key for the cache item.</param>
        /// <param name="destination">The target to write the cache contents on success.</param>
        /// <returns><c>true</c> if the cache item is found and successfully written to the <paramref name="destination"/>, <c>false</c> otherwise.</returns>
        /// <remarks>This method is functionally similar to <see cref="IDistributedCache.Get(string)"/>, but avoids unnecessary array allocations by utilizing an <see cref="IBufferWriter{byte}"/>.</remarks>
        bool GetFromCache(string key, IBufferWriter<byte> destination);

        /// <summary>
        /// Sets or overwrites a cache item synchronously.
        /// </summary>
        /// <param name="key">The key of the entry to create.</param>
        /// <param name="value">The value for this cache entry, represented as a <see cref="ReadOnlySequence{byte}"/> of bytes.</param>
        /// <param name="options">The cache options for the entry.</param>
        /// <returns><c>true</c> if the cache item is set successfully, <c>false</c> otherwise.</returns>
        /// <remarks>This method is functionally similar to <see cref="IDistributedCache.Set(string, byte[], DistributedCacheEntryOptions)"/>, 
        /// but avoids unnecessary array allocations by utilizing a <see cref="ReadOnlySequence{byte}"/>. 
        /// It also returns a <see cref="bool"/> indicating the success of the operation.</remarks>
        bool SetInCache(string key, ReadOnlySequence<byte> value, DistributedCacheEntryOptions options);

        /// <summary>
        /// Refreshes a value in the cache synchronously based on its key, resetting its sliding expiration timeout (if any).
        /// </summary>
        /// <param name="key">A string identifying the requested value.</param>  
        /// <returns><c>true</c> if the cache item is refreshed successfully, <c>false</c> otherwise.</returns>
        /// <remarks>This method is functionally similar to <see cref="IDistributedCache.Refresh(string)"/>, 
        /// but returns a <see cref="bool"/> indicating the success of the operation.</remarks>
        bool RefreshInCache(string key);

        /// <summary>
        /// Removes the value with the given key synchronously from the cache.
        /// </summary>
        /// <param name="key">A string identifying the requested value.</param>
        /// <returns><c>true</c> if the cache item is removed successfully, <c>false</c> otherwise.</returns>
        /// <remarks>This method is functionally similar to <see cref="IDistributedCache.Remove(string)"/>, 
        /// but returns a <see cref="bool"/> indicating the success of the operation.</remarks>
        bool RemoveFromCache(string key);

        /// <summary>
        /// Asynchronously attempts to retrieve an existing cache entry.
        /// </summary>
        /// <param name="key">The unique key for the cache entry.</param>
        /// <param name="destination">The target to write the cache contents on success.</param>
        /// <param name="token">The <see cref="CancellationToken"/> used to propagate notifications that the operation should be canceled.</param>
        /// <returns><c>true</c>  
        /// if the cache entry is found and successfully written to the<paramref name="destination"/>, <c>false</c> otherwise.</returns>
        /// <remarks>This method is functionally similar to <see cref="IDistributedCache.GetAsync(string, CancellationToken)"/>, but avoids unnecessary array allocations by utilizing an <see cref="IBufferWriter{byte}"/>.</remarks>
        /// It also returns a <see cref="bool"/> indicating the success of the operation.</remarks>
        ValueTask<bool> GetFromCacheAsync(string key, IBufferWriter<byte> destination, CancellationToken token = default);

        /// <summary>
        /// Asynchronously sets or overwrites a cache entry.
        /// </summary>
        /// <param name="key">The key of the entry to create.</param>
        /// <param name="value">The value for this cache entry, represented as a <see cref="ReadOnlySequence{byte}"/> of bytes.</param>
        /// <param name="options">The cache options for the value.</param>
        /// <param name="token">The <see cref="CancellationToken"/> used to propagate notifications that the operation should be canceled.</param>
        /// <returns><c>true</c>  
        /// if the cache item is set successfully, <c>false</c> otherwise.</returns>
        /// <remarks>This method is functionally similar to <see cref="IDistributedCache.SetAsync(string, byte[], DistributedCacheEntryOptions, CancellationToken)"/>, but avoids unnecessary array allocations by utilizing a <see cref="ReadOnlySequence{T}"/>.
        /// It also returns a <see cref="bool"/> indicating the success of the operation.</remarks>
        ValueTask<bool> SetInCacheAsync(string key, ReadOnlySequence<byte> value, DistributedCacheEntryOptions options, CancellationToken token = default);

        /// <summary>
        /// Asynchronously refreshes a value in the cache based on its key, resetting its sliding expiration timeout (if any).
        /// </summary>
        /// <param name="key">A string identifying the requested value.</param>
        /// <param name="token">The  
	    /// <see cref="CancellationToken"/> used to propagate notifications that the operation should be canceled.</param>
        /// <remarks>This method is functionally similar to <see cref="IDistributedCache.RefreshAsync(string, CancellationToken)"/>, 
        /// but returns a <see cref="bool"/> indicating the success of the operation.</remarks>
        /// <returns><c>true</c>  
        /// if the cache item is refreshed successfully, <c>false</c> otherwise.</returns>
        ValueTask<bool> RefreshInCacheAsync(string key, CancellationToken token = default);

        /// <summary>
        /// Asynchronously removes the value with the given key from the cache.
        /// </summary>
        /// <param name="key">A string identifying the requested value.</param>
        /// <param name="token">The  
        /// <see cref = "CancellationToken" /> used to propagate notifications that the operation should be canceled.</param>
        /// <remarks>This method is functionally similar to <see cref="IDistributedCache.RemoveAsync(string, CancellationToken)"/>, 
        /// but returns a <see cref="bool"/> indicating the success of the operation.</remarks>
        /// <returns><c>true</c>
        /// if the cache item is removed successfully, <c>false</c> otherwise.</returns>
        ValueTask<bool> RemoveFromCacheAsync(string key, CancellationToken token = default);

        /// <summary>
        /// Asynchronously retrieves a batch of values from the cache based on their keys.
        /// </summary>
        /// <typeparam name="T">The type of the values to be retrieved.</typeparam>
        /// <param name="keys">A collection of keys identifying the requested values.</param>
        /// <param name="callback">A callback function to be invoked for each retrieved key-value pair.</param>
        /// <param name="token">The <see cref="CancellationToken"/> used to propagate notifications that the operation should be canceled.</param>
        ValueTask GetBatchFromCacheAsync<T>(IEnumerable<string> keys, Action<string, T?> callback, CancellationToken token = default);

        /// <summary>
        /// Asynchronously sets a batch of values in the cache.
        /// </summary>
        /// <param name="data">A dictionary containing the key-value pairs to be set in the cache, where the values are represented as <see cref="ReadOnlySequence{byte}"/> of bytes.</param>
        /// <param name="options">The cache options for the entries.</param>
        /// <param name="absoluteExpiration">The absolute expiration time for the entries.</param>
        /// <param name="callback">A callback function to be invoked for each key, indicating whether the operation was successful.</param>
        /// <param name="token">The <see cref="CancellationToken"/> used to propagate notifications that the operation should be canceled.</param>
        ValueTask SetBatchInCacheAsync(IDictionary<string, ReadOnlySequence<byte>> data, DistributedCacheEntryOptions options, TimeSpan? absoluteExpiration, Action<string, bool> callback, CancellationToken token = default);

        /// <summary>
        /// Asynchronously refreshes a batch of values in the cache based on their keys, resetting their sliding expiration timeout (if any).
        /// </summary>
        /// <param name="keys">A collection of keys identifying the requested values.</param>
        /// <param name="callback">A callback function to be invoked for each key, indicating whether the refresh operation was successful.</param>
        /// <param name="token">The <see cref="CancellationToken"/> used to propagate notifications that the operation should be canceled.</param>
        ValueTask RefreshBatchFromCacheAsync(IEnumerable<string> keys, Action<string, bool> callback, CancellationToken token = default);

        /// <summary>
        /// Asynchronously removes a batch of values from the cache based on their keys.
        /// </summary>
        /// <param name="keys">A collection of keys identifying the values to be removed.</param>
        /// <param name="callback">A callback function to be invoked for each key, indicating whether the remove operation was successful.</param>
        /// <param name="token">The <see cref="CancellationToken"/> used to propagate notifications that the operation should be canceled.</param>
        ValueTask RemoveBatchFromCacheAsync(IEnumerable<string> keys, Action<string, bool> callback, CancellationToken token = default);

        /// <summary>
        /// Get the configured Polly policy.
        /// </summary>
        AsyncPolicyWrap<object> GetPollyPolicy();

        /// <summary>
        /// Set the fallback value for the polly retry policy.
        /// </summary>
        /// <remarks>Policy will return <see cref="RedisValue.Null"/> if not set.</remarks>
        /// <param name="value"></param>
        void SetFallbackValue(object value);
    }
}
