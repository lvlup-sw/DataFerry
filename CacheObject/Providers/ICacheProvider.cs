using CacheObject.Caches;

namespace CacheObject.Providers
{
    /// <summary>
    /// An interface for a cache provider.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public interface ICacheProvider<T>
    {
        /// <summary>
        /// Retrieve object from Cache
        /// </summary>
        /// <remarks>
        /// If the object is not in the cache, it will be retrieved from the real provider and set in the cache
        /// </remarks>
        Task<T> CacheObjectAsync(T obj, string key);

        /// <summary>
        /// Get the service provider
        /// </summary>
        IServiceProvider ServiceProvider { get; }

        /// <summary>
        /// Get the real provider
        /// </summary>
        IRealProvider<T> RealProvider { get; }

        /// <summary>
        /// Get the cache object
        /// </summary>
        object Cache { get; }
    }
}