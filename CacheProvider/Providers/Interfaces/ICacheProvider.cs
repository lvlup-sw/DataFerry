using CacheProvider.Caches;

namespace CacheProvider.Providers.Interfaces
{
    /// <summary>
    /// An interface for the Cache Provider.
    /// </summary>
    public interface ICacheProvider<T> where T : class
    {
        /// <summary>
        /// Check the cache for an entry with a specified key.
        /// </summary>
        Task<T?> GetFromCacheAsync(string key, GetFlags? flags = null);

        /// <summary>
        /// Asynchronously adds an entry to the cache with a specified key.
        /// </summary>
        /// <remarks>
        /// Returns true if the entry was added to the cache, false otherwise.
        /// </remarks>
        /// <param name="key">The key to use for the .</param>
        /// <param name="data">The  to add to the cache.</param>
        /// <param name="expiration">The expiration time for the record.</param>
        Task<bool> SetInCacheAsync(string key, T data, TimeSpan? expiration);

        /// <summary>
        /// Asynchronously removes an entry from the cache using a key.
        /// </summary>
        /// <remarks>
        /// Returns true if the entry was removed from the cache, false otherwise.
        /// </remarks>
        /// <param name="key">The key of the  to remove.</param>
        Task<bool> RemoveFromCacheAsync(string key);

        /// <summary>
        /// Batch operation to check the cache for an entries with the specified keys.
        /// </summary>
        Task<IDictionary<string, T?>> GetBatchFromCacheAsync(IEnumerable<string> keys, GetFlags? flags = null, CancellationToken? cancellationToken = null);

        /// <summary>
        /// Sets multiple records in the cache.
        /// </summary>
        /// <param name="data">A dictionary containing the keys and data to store in the cache.</param>
        /// <returns>True if all records were set successfully; otherwise, false.</returns>
        Task<bool> SetBatchInCacheAsync(Dictionary<string, T> data, CancellationToken? cancellationToken = null);

        /// <summary>
        /// Removes multiple records from the cache.
        /// </summary>
        /// <param name="keys">The keys of the records to remove.</param>
        /// <returns>True if all records were removed successfully; otherwise, false.</returns>
        Task<bool> RemoveBatchFromCacheAsync(IEnumerable<string> keys, CancellationToken? cancellationToken = null);

        /// <summary>
        /// Gets the cache object representation.
        /// </summary>
        DistributedCache Cache { get; }
    }
}