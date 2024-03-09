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
        Task<T> GetFromCacheAsync(T data, string key, GetFlags? flags);

        /// <summary>
        /// Batch operation to check the cache for an entries with the specified keys.
        /// </summary>
        Task<IDictionary<string, T>> GetBatchFromCacheAsync(IDictionary<string, T> data, IEnumerable<string> keys, GetFlags? flags);

        /// <summary>
        /// Cache  with a specified key.
        /// </summary>
        T GetFromCache(T data, string key, GetFlags? flags);

        /// <summary>
        /// Gets the cache object representation.
        /// </summary>
        DistributedCache Cache { get; }
    }
}