using CacheProvider.Caches;
using CacheProvider.Providers.Interfaces;
using MemCache = Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace CacheProvider.Providers
{
    /// <summary>
    /// CacheProvider is a generic class that implements the <see cref="ICacheProvider{T}"/> interface.
    /// </summary>
    /// <remarks>
    /// This class makes use of two types of caches: <see cref="MemoryCache"/> and <see cref="DistributedCache"/>.
    /// It uses the <see cref="IRealProvider{T}>"/> interface to retrieve s from the real provider.
    /// </remarks>
    /// <typeparam name="T">The type of object to cache.</typeparam>
    public class CacheProvider<T> : ICacheProvider<T> where T : class
    {
        private readonly IRealProvider<T> _realProvider;
        private readonly CacheSettings _settings;
        private readonly ILogger _logger;
        private readonly DistributedCache _cache;

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
        public CacheProvider(IConnectionMultiplexer connection, IRealProvider<T> provider, CacheSettings settings, ILogger logger)
        {
            // Null checks
            ArgumentNullException.ThrowIfNull(connection);
            ArgumentNullException.ThrowIfNull(provider);
            ArgumentNullException.ThrowIfNull(settings);
            ArgumentNullException.ThrowIfNull(logger);

            // Initializations
            _realProvider = provider;
            _settings = settings;
            _logger = logger;
            _cache = new DistributedCache(connection, new MemCache.MemoryCache(new MemCache.MemoryCacheOptions()), settings, logger);
        }

        /// <summary>
        /// Gets the cache instance.
        /// </summary>
        public DistributedCache Cache => _cache;

        /// <summary>
        /// Asynchronously checks the cache for an  with a specified key.
        /// </summary>
        /// <remarks>
        /// If the  is found in the cache, it is returned. If not, the  is retrieved from the real provider and then cached before being returned.
        /// </remarks>
        /// <param name="">The  to cache.</param>
        /// <param name="key">The key to use for caching the .</param>
        /// <returns>The cached .</returns>
        /// <exception cref="ArgumentNullException">Thrown when the  is null or if the cache was not successfully instantiated.</exception>
        /// <exception cref="ArgumentException">Thrown when the key is null, an empty string, or contains only white-space characters.</exception>
        /// <exception cref="NullReferenceException">Thrown when the  is not successfully retrieved.</exception>
        public async Task<T> GetFromCacheAsync(T data, string key, GetFlags? flag = null)
        {
            try
            {
                // Null checks
                ArgumentNullException.ThrowIfNull(_cache);
                ArgumentNullException.ThrowIfNull(data);
                ArgumentException.ThrowIfNullOrWhiteSpace(key);

                // Check if the  is in the cache and return if found
                _logger.LogInformation("Checking cache for  with key {key}.", key);
                var cached = await _cache.GetAsync<T>(key);
                if (cached is not null)
                {
                    _logger.LogInformation("Cached  with key {key} found in cache.", key);
                    return cached;
                }

                // If not, get the  from the real provider and set it in the cache
                _logger.LogInformation("Cached  with key {key} not found in cache. Getting  from real provider.", key);
                cached = await _realProvider.GetAsync(data);

                if (cached is null)
                {
                    _logger.LogError(" with key {key} not received from real provider.", key);
                    throw new NullReferenceException(string.Format(" with key {0} was not successfully retrieved.", key));
                }

                // Attempt to return the  after setting it in the cache
                _logger.LogInformation("Attempting to set  received from real provider with {key} in the cache.", key);
                if (await _cache.SetAsync(key, cached))
                {
                    _logger.LogInformation(" with key {key} received from real provider and set in cache.", key);
                }
                else
                {
                    _logger.LogError("Failed to set  with key {key} in cache.", key);
                }

                return cached;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while checking the cache.");
                throw ex.GetBaseException();
            }
        }

        // Add batch ops


        /// <summary>
        /// Synchronously checks the cache for an  with a specified key.
        /// </summary>
        /// <remarks>
        /// If the  is found in the cache, it is returned. If not, the  is retrieved from the real provider and then cached before being returned.
        /// </remarks>
        /// <param name="">The  to cache.</param>
        /// <param name="key">The key to use for caching the .</param>
        /// <returns>The cached .</returns>
        /// <exception cref="ArgumentNullException">Thrown when the  is null.</exception>
        /// <exception cref="ArgumentException">Thrown when the key is null, an empty string, or contains only white-space characters.</exception>
        /// <exception cref="NullReferenceException">Thrown when the  is not successfully retrieved.</exception>
        public T GetFromCache(T data, string key, GetFlags? flags = null)
        {
            throw new NotImplementedException();
            /*
            try
            {
                // Null checks
                ArgumentNullException.ThrowIfNull(data);
                ArgumentException.ThrowIfNullOrWhiteSpace(key);

                // Check if the  is in the cache and return if found
                MemoryCache MemoryCache = MemoryCache.GetInstance(_settings, _logger);
                _logger.LogInformation("Checking local cache for  with key {key}.", key);
                var cached = MemoryCache.Get<T>(key);
                if (cached is not null)
                {
                    _logger.LogInformation("Cached  with key {key} found in local cache.", key);
                    return cached;
                }

                // If not, get the  from the real provider and set it in the cache
                _logger.LogInformation("Cached  with key {key} not found in local cache. Getting  from real provider.", key);
                cached = _realProvider.Get(data);

                if (cached is null)
                {
                    _logger.LogError(" with key {key} not received from real provider.", key);
                    throw new NullReferenceException(string.Format(" with key {0} was not successfully retrieved.", key));
                }

                // Attempt to return the  after setting it in the cache
                _logger.LogInformation("Attempting to set  received from real provider with {key} in the cache.", key);
                if (MemoryCache.Set(key, cached))
                {
                    _logger.LogInformation(" with key {key} received from real provider and set in cache.", key);
                }
                else
                {
                    _logger.LogError("Failed to set  with key {key} in cache.", key);
                }

                return cached;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while checking the cache.");
                throw ex.GetBaseException();
            }
            */
        }

        public Task<IDictionary<string, T>> GetBatchFromCacheAsync(IDictionary<string, T> data, IEnumerable<string> keys, GetFlags? flags)
        {
            throw new NotImplementedException();
        }
    }
}
