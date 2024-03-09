using System.Collections.Concurrent;

namespace CacheProvider.Caches.Interfaces
{
    /// <summary>
    /// A cache interface for caching arbitrary objects.
    /// </summary>
    public interface IMemoryCache
    {
        /// <summary>
        /// Retrieves an  from the cache using a key.
        /// </summary>
        /// <remarks>
        /// Returns the  if it exists in the cache, null otherwise.
        /// </remarks>
        /// <param name="key">The key of the  to retrieve.</param>
        T? Get<T>(string key);

        /// <summary>
        /// Asynchronously adds an  to the cache with a specified key.
        /// </summary>
        /// <remarks>
        /// Returns true if the  was added to the cache, false otherwise.
        /// </remarks>
        /// <param name="key">The key to use for the .</param>
        /// <param name="">The  to add to the cache.</param>
        bool Set<T>(string key, T );

        /// <summary>
        /// Asynchronously removes an  from the cache using a key.
        /// </summary>
        /// <remarks>
        /// Returns true if the  was removed from the cache, false otherwise.
        /// </remarks>
        /// <param name="key">The key of the  to remove.</param>
        bool Remove(string key);

        /// <summary>
        /// Retrieves an object representation of the cache. The cache may be a local cache or a distributed cache.
        /// </summary>
        /// <remarks>
        /// In <see cref="MemoryCache"/>, the cache object returned is a <see cref="ConcurrentDictionary{TKey, TValue}"/>.
        /// </remarks>
        object GetCache();
    }
}
