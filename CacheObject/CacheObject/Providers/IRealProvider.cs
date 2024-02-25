using CacheObject.Caches;
using System;
using System.Threading.Tasks;

namespace CacheObject.Providers
{
    public interface IRealProvider<T>
    {
        /// <summary>
        /// Get object from data source
        /// </summary>
        Task<T> GetObjectAsync(T obj);

        /// <summary>
        /// Get the service provider
        /// </summary>
        IServiceProvider ServiceProvider { get; }
    }
}