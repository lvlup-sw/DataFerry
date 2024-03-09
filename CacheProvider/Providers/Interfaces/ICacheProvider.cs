namespace CacheProvider.Providers.Interfaces
{
    /// <summary>
    /// An interface for the Cache Provider.
    /// </summary>
    public interface ICacheProvider<T> where T : class
    {
        // Reconsider how interaction with CacheProvider works
        // Should the cache type be set in the constructor or should it be set in the method call?
        // Also need to implement more methods for the cache provider
        // Such as setting the cache, getting the cache, and removing the cache?

        /// <summary>
        /// Check the cache for an  with a specified key.
        /// </summary>
        Task<T> GetFromCacheAsync(T , string key, GetFlags? flags);

        /// <summary>
        /// Cache  with a specified key.
        /// </summary>
        T GetFromCache(T , string key, GetFlags? flags);

        /// <summary>
        /// Gets the cache object representation.
        /// </summary>
        object Cache { get; }
    }
}