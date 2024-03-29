using StackExchange.Redis;

namespace CacheProvider.Caches.Interfaces
{
    /// <summary>
    /// A cache interface for caching arbitrary objects.
    /// </summary>
    public interface IDistributedCache
    {
        /// <summary>
        /// Asynchronously retrieves an  from the cache using a key.
        /// </summary>
        /// <remarks>
        /// Returns the  if it exists in the cache, null otherwise.
        /// </remarks>
        /// <param name="key">The key of the  to retrieve.</param>
        Task<T?> GetAsync<T>(string key);

        /// <summary>
        /// Asynchronously adds an  to the cache with a specified key.
        /// </summary>
        /// <remarks>
        /// Returns true if the  was added to the cache, false otherwise.
        /// </remarks>
        /// <param name="key">The key to use for the record.</param>
        /// <param name="data">The  to add to the cache.</param>
        /// <param name="expiration">The expiration time for the record.</param>
        Task<bool> SetAsync<T>(string key, T data, TimeSpan? expiration);

        /// <summary>
        /// Asynchronously removes an  from the cache using a key.
        /// </summary>
        /// <remarks>
        /// Returns true if the  was removed from the cache, false otherwise.
        /// </remarks>
        /// <param name="key">The key of the  to remove.</param>
        Task<bool> RemoveAsync(string key);

        /// <summary>
        /// Retrieves multiple records from the cache.
        /// </summary>
        /// <param name="keys">The keys of the records to remove.</param>
        /// <returns>A dictionary of the records associated with the keys, if they exist; otherwise, default(<typeparamref name="T"/>).</returns>
        Task<Dictionary<string, T?>> GetBatchAsync<T>(IEnumerable<string> keys, CancellationToken? cancellationToken = null);

        /// <summary>
        /// Sets multiple records in the cache.
        /// </summary>
        /// <param name="data">A dictionary containing the keys and data to store in the cache.</param>
        /// <param name="absoluteExpireTime">The absolute expiration time for the records.</param>
        /// <returns>True if all records were set successfully; otherwise, false.</returns>
        Task<bool> SetBatchAsync<T>(Dictionary<string, T> data, TimeSpan? absoluteExpireTime = null, CancellationToken? cancellationToken = null);

        /// <summary>
        /// Removes multiple records from the cache.
        /// </summary>
        /// <param name="keys">The keys of the records to remove.</param>
        /// <returns>True if all records were removed successfully; otherwise, false.</returns>
        Task<bool> RemoveBatchAsync(IEnumerable<string> keys, CancellationToken? cancellationToken = null);

        /// <summary>
        /// Retrieves an <see cref="IDatabase"/> representation of the cache.
        /// </summary>
        IDatabase GetCacheConnection();
    }
}
