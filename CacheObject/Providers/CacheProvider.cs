using CacheObject.Caches;
using Microsoft.Extensions.DependencyInjection;

namespace CacheObject.Providers
{
    /// <summary>
    /// CacheProvider is a generic class that implements the ICacheProvider interface.
    /// </summary>
    /// <remarks>
    /// This class may be used with a variety of caching implementations and real providers.
    /// </remarks>
    /// <typeparam name="T"></typeparam>
    public class CacheProvider<T> : ICacheProvider<T>
    {
        private readonly IServiceProvider? _serviceProvider;
        private readonly IRealProvider<T> _realProvider;
        private readonly ICache<T> _cache;

        /// <summary>
        /// Primary constructor for the CacheProvider class that takes an IServiceProvider as a parameter.
        /// </summary>
        /// <remarks>
        /// Intended use is for the dependency injection pattern.
        /// </remarks>
        /// <param name="serviceProvider"></param>
        /// <exception cref="InvalidOperationException"></exception>
        public CacheProvider(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
            _realProvider = _serviceProvider.GetService<IRealProvider<T>>() ?? throw new InvalidOperationException($"Could not retrieve service of type {typeof(IRealProvider<T>)}");
            _cache = _serviceProvider.GetService<ICache<T>>() ?? throw new InvalidOperationException($"Could not retrieve service of type {typeof(ICache<T>)}");
        }

        /// <summary>
        /// Alternative constructor for the CacheProvider class that takes an ICache and IRealProvider as parameters.
        /// </summary>
        /// <remarks>
        /// Generally used for testing purposes.
        /// </remarks>
        /// <param name="cache"></param>
        /// <param name="realProvider"></param>
        public CacheProvider(ICache<T> cache, IRealProvider<T> realProvider)
        {
            _realProvider = realProvider;
            _cache = cache;
        }

        /// <summary>
        /// Gets the service provider.
        /// </summary>
        public IServiceProvider ServiceProvider { get => _serviceProvider!; }

        /// <summary>
        /// Gets the real provider.
        /// </summary>
        public IRealProvider<T> RealProvider { get => _realProvider; }

        /// <summary>
        /// Gets the cache.
        /// </summary>
        public ICache<T> Cache { get => _cache; }

        /// <summary>
        /// Asynchronously caches an object with a specified key. If the object is already in the cache, it returns the cached object.
        /// If the object is not in the cache, it retrieves the object from the real provider, adds it to the cache, and then returns the object.
        /// </summary>
        /// <param name="obj">The object to cache.</param>
        /// <param name="key">The key to use for caching the object.</param>
        /// <returns>The cached object.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the object or key is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the key is an empty string or contains only white-space characters.</exception>
        public async Task<T> CacheObjectAsync(T obj, string key)
        {
            // Null checks
            ArgumentNullException.ThrowIfNull(obj);
            ArgumentException.ThrowIfNullOrWhiteSpace(key);

            // Check if the item is in the cache
            var cachedItem = await _cache.GetItemAsync(key);
            if (cachedItem != null) 
            {
                return cachedItem;
            }

            // If not, get the item from the real provider and set it in the cache
            cachedItem = await _realProvider.GetObjectAsync(obj);
            await _cache.SetItemAsync(key, cachedItem);
            return cachedItem;
        }
    }
}
