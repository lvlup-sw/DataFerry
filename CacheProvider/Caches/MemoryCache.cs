using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Caching.Memory;

namespace CacheProvider.Caches
{
    /// <summary>
    /// MemoryCache is an in-memory caching implementation.
    /// </summary>
    /// <remarks>
    /// This class inherits the <see cref="IMemoryCache"/> interface behind the scenes.
    /// </remarks> 
    internal class MemoryCache : IMemoryCache, IDisposable
    {
        private readonly IMemoryCache _cache;
        private readonly ILogger _logger;

        /// <summary>
        /// Private constructor of <see cref="MemoryCache"/>.
        /// </summary>
        /// <remarks>
        /// This prevents more than one instance being created per process.
        /// </remarks>
        /// <param name="settings">The settings for the cache.</param>
        /// <exception cref="ArgumentNullException">Thrown when settings or logger is null.</exception>"
        public MemoryCache(IMemoryCache cache, ILogger logger)
        {
            ArgumentNullException.ThrowIfNull(cache);
            ArgumentNullException.ThrowIfNull(logger);

            _cache = cache;
            _logger = logger;
        }

        /// <summary>
        /// Retrieves an  from the cache using a key. Note this is a synchronous operation in <see cref="MemoryCache"/>.
        /// </summary>
        /// <remarks>
        /// Returns the value if it exists in the cache, null otherwise.
        /// </remarks>
        /// <param name="key">The key of the  to retrieve.</param>
        /// <returns><paramref name="value"/></returns>
        public bool TryGetValue(object key, out object? value)
        {
            _logger.LogInformation("Attempting to retrieve data with key {key} from memory cache.", key);
            bool success = _cache.TryGetValue(key, out value);

            string message = value is not null
                ? $"Get operation completed for key: {key}"
                : $"Get operation failed for key: {key}";

            if (success)
                _logger.LogInformation(message);
            else
                _logger.LogError(message);

            return success;
        }

        /// <summary>
        /// Adds a value to the cache using a key.
        /// </summary>
        /// <remarks>
        /// Returns true if the  was added to the cache, false otherwise.
        /// </remarks>
        /// <param name="key">The key to use for the .</param>
        /// <param name="T">The  to add to the cache.</param>
        public void Set<T>(string key, T data)
        {
            _cache.CreateEntry(key).SetValue(data);
            _logger.LogInformation("Set operation completed for key: {key}", key);
        }

        /// <summary>
        /// Remove an  to the cache with a specified key.
        /// </summary>
        /// <remarks>
        /// Returns true if the  was remove from the cache, false otherwise.
        /// </remarks>
        /// <param name="key">The key of the  to remove.</param>
        public void Remove(object key)
        {
            _cache.Remove(key);
            _logger.LogInformation("Remove operation completed for key: {key}", key);
        }

        /// <summary>
        /// Creates a new cache entry for the specified key.
        /// </summary>
        /// <param name="key">The key for the cache entry.</param>
        /// <returns>The created cache entry.</returns>
        public ICacheEntry CreateEntry(object key)
        {
            _logger.LogInformation("Creating cache entry for key {key}.", key);
            return _cache.CreateEntry(key);
        }

        /// <summary>
        /// Disposes the memory cache.
        /// </summary>
        public void Dispose()
        {
            _logger.LogInformation("Disposing memory cache.");
            _cache.Dispose();
        }

        /// <summary>
        /// Retrieves an object representation of the cache.
        /// </summary>
        /// <remarks>
        /// In this case, a <see cref="MemoryCache"/> object is returned.
        /// </remarks>
        public object GetCache() => _cache;
    }
}
