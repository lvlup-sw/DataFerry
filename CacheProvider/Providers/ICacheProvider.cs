namespace CacheProvider.Providers
{
    /// <summary>
    /// An interface for the Cache Provider.
    /// </summary>
    public interface ICacheProvider<T> where T : class
    {
        /// <summary>
        /// Check the cache for an item with a specified key.
        /// </summary>
        Task<T> CheckCacheAsync(T item, string key);

        /// <summary>
        /// Gets the cache object representation.
        /// </summary>
        object Cache { get; }
    }
}