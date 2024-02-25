using CacheObject.Caches;
using System;
using System.Threading.Tasks;

namespace CacheObject.Providers
{
    public interface ICacheProvider<T> : IDisposable where T : class
    {
        /// <summary>
        /// Retrieve object from Cache
        /// </summary>
        /// <remarks>
        /// If the object is not in the cache, it will be retrieved from the real provider and set in the cache
        /// </remarks>
        Task<T> CacheObjectAsync(T obj, string key);

        /// <summary>
        /// Get the cache object
        /// </summary>
        ICache<T> Cache { get; }

        /// <summary>
        /// Get the service provider
        /// </summary>
        IServiceProvider ServiceProvider { get; }

        /// <summary>
        /// Get the real provider
        /// </summary>
        IRealProvider<T> RealProvider { get; }
    }
}