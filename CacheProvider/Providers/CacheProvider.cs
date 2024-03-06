using CacheProvider.Caches;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

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
        private readonly CacheType _cacheType;
        private readonly CacheSettings _settings;
        private readonly ILogger _logger;
        private readonly DistributedCache? _cache;

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
        public CacheProvider(IRealProvider<T> provider, CacheType type, CacheSettings settings, ILogger logger, IConnectionMultiplexer? connection = null)
        {
            // Null checks
            ArgumentNullException.ThrowIfNull(provider);
            ArgumentNullException.ThrowIfNull(type);
            ArgumentNullException.ThrowIfNull(settings);

            // Initializations
            _realProvider = provider;
            _cacheType = type;
            _settings = settings;
            _logger = logger;
            _cache = type is CacheType.Distributed
                ? new DistributedCache(connection!, settings, logger)
                : default;
        }

        /// <summary>
        /// Gets the cache object representation.
        /// </summary>
        public object Cache 
        {
            get => _cacheType is CacheType.Distributed
                ? _cache!.GetCache()
                : LocalCache.GetInstance(_settings, _logger).GetCache();
        }

        /// <summary>
        /// Asynchronously checks the cache for an item with a specified key.
        /// </summary>
        /// <remarks>
        /// If the item is found in the cache, it is returned. If not, the item is retrieved from the real provider and then cached before being returned.
        /// </remarks>
        /// <param name="item">The item to cache.</param>
        /// <param name="key">The key to use for caching the item.</param>
        /// <returns>The cached item.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the item is null or if the cache was not successfully instantiated.</exception>
        /// <exception cref="ArgumentException">Thrown when the key is null, an empty string, or contains only white-space characters.</exception>
        /// <exception cref="NullReferenceException">Thrown when the item is not successfully retrieved.</exception>
        public async Task<T> CheckCacheAsync(T item, string key)
        {
            try
            {
                // Null checks
                ArgumentNullException.ThrowIfNull(_cache);
                ArgumentNullException.ThrowIfNull(item);
                ArgumentException.ThrowIfNullOrWhiteSpace(key);

                // Check if the item is in the cache and return if found
                _logger.LogInformation("Checking cache for item with key {key}.", key);
                var cachedItem = await _cache.GetItemAsync<T>(key);
                if (cachedItem is not null)
                {
                    _logger.LogInformation("Cached item with key {key} found in cache.", key);
                    return cachedItem;
                }

                // If not, get the item from the real provider and set it in the cache
                _logger.LogInformation("Cached item with key {key} not found in cache. Getting item from real provider.", key);
                cachedItem = await _realProvider.GetItemAsync(item);

                if (cachedItem is null)
                {
                    _logger.LogError("Item with key {key} not received from real provider.", key);
                    throw new NullReferenceException(string.Format("Item with key {0} was not successfully retrieved.", key));
                }

                // Attempt to return the item after setting it in the cache
                _logger.LogInformation("Attempting to set item received from real provider with {key} in the cache.", key);
                if (await _cache.SetItemAsync(key, cachedItem))
                {
                    _logger.LogInformation("Item with key {key} received from real provider and set in cache.", key);
                }
                else
                {
                    _logger.LogError("Failed to set item with key {key} in cache.", key);
                }

                return cachedItem;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while checking the cache.");
                throw ex.GetBaseException();
            }
        }


        /// <summary>
        /// Synchronously checks the cache for an item with a specified key.
        /// </summary>
        /// <remarks>
        /// If the item is found in the cache, it is returned. If not, the item is retrieved from the real provider and then cached before being returned.
        /// </remarks>
        /// <param name="item">The item to cache.</param>
        /// <param name="key">The key to use for caching the item.</param>
        /// <returns>The cached item.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the item is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the key is null, an empty string, or contains only white-space characters.</exception>
        /// <exception cref="NullReferenceException">Thrown when the item is not successfully retrieved.</exception>
        public T CheckCache(T item, string key)
        {
            try
            {
                // Null checks
                ArgumentNullException.ThrowIfNull(item);
                ArgumentException.ThrowIfNullOrWhiteSpace(key);

                // Check if the item is in the cache and return if found
                LocalCache localCache = LocalCache.GetInstance(_settings, _logger);
                _logger.LogInformation("Checking local cache for item with key {key}.", key);
                var cachedItem = localCache.GetItem<T>(key);
                if (cachedItem is not null)
                {
                    _logger.LogInformation("Cached item with key {key} found in local cache.", key);
                    return cachedItem;
                }

                // If not, get the item from the real provider and set it in the cache
                _logger.LogInformation("Cached item with key {key} not found in local cache. Getting item from real provider.", key);
                cachedItem = _realProvider.GetItem(item);

                if (cachedItem is null)
                {
                    _logger.LogError("Item with key {key} not received from real provider.", key);
                    throw new NullReferenceException(string.Format("Item with key {0} was not successfully retrieved.", key));
                }

                // Attempt to return the item after setting it in the cache
                _logger.LogInformation("Attempting to set item received from real provider with {key} in the cache.", key);
                if (localCache.SetItem(key, cachedItem))
                {
                    _logger.LogInformation("Item with key {key} received from real provider and set in cache.", key);
                }
                else
                {
                    _logger.LogError("Failed to set item with key {key} in cache.", key);
                }

                return cachedItem;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while checking the cache.");
                throw ex.GetBaseException();
            }
        }
    }
}
