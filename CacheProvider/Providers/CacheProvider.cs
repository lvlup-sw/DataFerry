using CacheProvider.Caches;
using Microsoft.Extensions.Options;

namespace CacheProvider.Providers
{
    /// <summary>
    /// CacheProvider is a generic class that implements the <see cref="ICacheProvider{T}"/> interface.
    /// </summary>
    /// <remarks>
    /// This class makes use of two types of caches: <see cref="LocalCache"/> and <see cref="DistributedCache"/>.
    /// It uses the <see cref="IRealProvider{T}>"/> interface to retrieve items from the real provider.
    /// </remarks>
    /// <typeparam name="T">The type of object to cache.</typeparam>
    public class CacheProvider<T> : ICacheProvider<T> where T : class
    {
        private readonly IRealProvider<T> _realProvider;
        private readonly ICache _cache;

        /// <summary>
        /// Primary constructor for the CacheProvider class.
        /// </summary>
        /// <remarks>
        /// Takes a real provider, cache type, and cache settings as parameters.
        /// </remarks>
        /// <param name="provider"></param>
        /// <param name="type"></param>
        /// <param name="settings"></param>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="InvalidOperationException"></exception>
        public CacheProvider(IRealProvider<T> provider, CacheType type, IOptions<CacheSettings> settings)
        {
            // Null checks
            ArgumentNullException.ThrowIfNull(provider);
            ArgumentNullException.ThrowIfNull(type);
            ArgumentNullException.ThrowIfNull(settings);

            // Initializations
            _realProvider = provider;
            _cache = type switch
            {
                CacheType.Local => LocalCache.GetInstance(settings),
                CacheType.Distributed => new DistributedCache(settings),
                _ => throw new InvalidOperationException("The CacheType is invalid.")
            };
        }

        /// <summary>
        /// Gets the cache object representation.
        /// </summary>
        public object Cache { get => _cache.GetCache(); }

        /// <summary>
        /// Asynchronously checks the cache for an item with a specified key.
        /// </summary>
        /// <remarks>
        /// If the item is found in the cache, it is returned. If not, the item is retrieved from the real provider and then cached before being returned.
        /// </remarks>
        /// <param name="item">The item to cache.</param>
        /// <param name="key">The key to use for caching the item.</param>
        /// <returns>The cached item.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the item is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the key is null, an empty string, or contains only white-space characters.</exception>
        public async Task<T> CheckCacheAsync(T item, string key)
        {
            // Null checks
            ArgumentNullException.ThrowIfNull(item);
            ArgumentException.ThrowIfNullOrWhiteSpace(key);

            // Check if the item is in the cache
            var cachedItem = await _cache.GetItemAsync<T>(key);
            if (cachedItem != null) 
            {
                return cachedItem;
            }

            // If not, get the item from the real provider and set it in the cache
            cachedItem = await _realProvider.GetItemAsync(item);
            await _cache.SetItemAsync(key, cachedItem);
            return cachedItem;
        }
    }
}
