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
        void GetFromCache(string key, IBufferWriter<byte> destination);

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
        bool SetInCache(string key, ReadOnlySequence<byte> value, DistributedCacheEntryOptions? options);

        /// <summary>
        /// Refreshes a value in the cache synchronously based on its key, resetting its sliding expiration timeout (if any).
        /// </summary>
        /// <param name="key">A string identifying the requested value.</param>
        /// <param name="options">The cache options for the entry.</param>
        /// <returns><c>true</c> if the cache item is refreshed successfully, <c>false</c> otherwise.</returns>
        /// <remarks>This method is functionally similar to <see cref="IDistributedCache.Refresh(string)"/>, 
        /// but returns a <see cref="bool"/> indicating the success of the operation.</remarks>
        bool RefreshInCache(string key, DistributedCacheEntryOptions options);

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
        /// <remarks>
        /// This method is functionally similar to <see cref="IDistributedCache.GetAsync(string, CancellationToken)"/>, 
        /// but avoids unnecessary array allocations by utilizing an <see cref="IBufferWriter{byte}"/>. 
        /// </remarks>
        ValueTask GetFromCacheAsync(string key, IBufferWriter<byte> destination, CancellationToken token = default);

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
        Task<bool> SetInCacheAsync(string key, ReadOnlySequence<byte> value, DistributedCacheEntryOptions? options, CancellationToken token = default);

        /// <summary>
        /// Asynchronously refreshes a value in the cache based on its key, resetting its sliding expiration timeout (if any).
        /// </summary>
        /// <param name="key">A string identifying the requested value.</param>
        /// <param name="options">The cache options for the value.</param> 
        /// <param name="token">The <see cref="CancellationToken"/> used to propagate notifications that the operation should be canceled.</param>
        /// <remarks>This method is functionally similar to <see cref="IDistributedCache.RefreshAsync(string, CancellationToken)"/>, 
        /// but returns a <see cref="bool"/> indicating the success of the operation.</remarks>
        /// <returns><c>true</c>  
        /// if the cache item is refreshed successfully, <c>false</c> otherwise.</returns>
        Task<bool> RefreshInCacheAsync(string key, DistributedCacheEntryOptions options, CancellationToken token = default);

        /// <summary>
        /// Asynchronously removes the value with the given key from the cache.
        /// </summary>
        /// <param name="key">A string identifying the requested value.</param>
        /// <param name="token">The  
        /// <see cref = "CancellationToken"/> used to propagate notifications that the operation should be canceled.</param>
        /// <remarks>This method is functionally similar to <see cref="IDistributedCache.RemoveAsync(string, CancellationToken)"/>, 
        /// but returns a <see cref="bool"/> indicating the success of the operation.</remarks>
        /// <returns><c>true</c>
        /// if the cache item is removed successfully, <c>false</c> otherwise.</returns>
        Task<bool> RemoveFromCacheAsync(string key, CancellationToken token = default);

        /// <summary>
        /// Asynchronously retrieves a batch of values from the cache.
        /// </summary>
        /// <param name="keys">The keys of the values to retrieve.</param>
        /// <param name="destination">The <see cref="IBufferWriter{T}"/> to which the retrieved values will be written.</param>
        /// <param name="token">The  
        /// <see cref = "CancellationToken"/> used to propagate notifications that the operation should be canceled.</param>
        /// <returns>An <see cref="IAsyncEnumerable{T}"/> that represents the asynchronous stream of results, which contains a tuple of the key and properties of the value written to the buffer.</returns>
        IAsyncEnumerable<(string Key, int Index, int Length)> GetBatchFromCacheAsync(IEnumerable<string> keys, RentedBufferWriter<byte> destination, CancellationToken token = default);

        /// <summary>
        /// Asynchronously sets a batch of values in the cache.
        /// </summary>
        /// <param name="data">A dictionary containing the key-value pairs to set in the cache.</param>
        /// <param name="options">Optional <see cref="DistributedCacheEntryOptions"/> to configure the cache entries.</param>
        /// <param name="token">The  
        /// <see cref = "CancellationToken"/> used to propagate notifications that the operation should be canceled.</param>
        /// <returns>An <see cref="IAsyncEnumerable{KeyValuePair{string, bool}}"/> that represents the asynchronous stream of results, indicating the success or failure of setting each key-value pair in the cache.</returns>
        IAsyncEnumerable<KeyValuePair<string, bool>> SetBatchInCacheAsync(IDictionary<string, ReadOnlySequence<byte>> data, DistributedCacheEntryOptions? options, CancellationToken token = default);

        /// <summary>
        /// Asynchronously refreshes a batch of cache entries.
        /// </summary>
        /// <param name="keys">The keys of the cache entries to refresh.</param>
        /// <param name="options">Optional <see cref="DistributedCacheEntryOptions"/> to configure the cache entries.</param>
        /// <param name="token">The  
        /// <see cref = "CancellationToken"/> used to propagate notifications that the operation should be canceled.</param>
        /// <returns>An <see cref="IAsyncEnumerable{KeyValuePair{string, bool}}"/> that represents the asynchronous stream of results, indicating the success or failure of refreshing each cache entry.</returns>
        IAsyncEnumerable<KeyValuePair<string, bool>> RefreshBatchFromCacheAsync(IEnumerable<string> keys, DistributedCacheEntryOptions options, CancellationToken token = default);

        /// <summary>
        /// Asynchronously removes a batch of entries from the cache.
        /// </summary>
        /// <param name="keys">The keys of the cache entries to remove.</param>
        /// <param name="token">The  
        /// <see cref = "CancellationToken"/> used to propagate notifications that the operation should be canceled.</param>
        /// <returns>An <see cref="IAsyncEnumerable{KeyValuePair{string, bool}}"/> that represents the asynchronous stream of results, indicating the success or failure of removing each cache entry.</returns>
        IAsyncEnumerable<KeyValuePair<string, bool>> RemoveBatchFromCacheAsync(IEnumerable<string> keys, CancellationToken token = default);

        /// <summary>
        /// Get the configured synchronous Polly policy.
        /// </summary>
        PolicyWrap<object> GetSyncPollyPolicy();

        /// <summary>
        /// Get the configured asynchronous Polly policy.
        /// </summary>
        AsyncPolicyWrap<object> GetAsyncPollyPolicy();

        /// <summary>
        /// Set the fallback value for the polly retry policy.
        /// </summary>
        /// <remarks>Policy will return <see cref="RedisValue.Null"/> if not set.</remarks>
        /// <param name="value"></param>
        void SetFallbackValue(object value);
    }
}
