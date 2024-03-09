using CacheProvider.Caches.Interfaces;
using CacheProvider.Providers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using System.Collections.Concurrent;
using System.Text.Json;

namespace CacheProvider.Caches
{
    /// <summary>
    /// MemoryCache is an in-memory caching implementation with asynchronous operations.
    /// </summary>
    /// <remarks>
    /// This class inherits the <see cref="IMemoryCache"/> interface and makes use of a <see cref="ConcurrentDictionary{TKey, TValue}"/> object behind the scenes.
    /// Cache invalidation is also implemented in this class using <see cref="TimeSpan"/>.
    /// </remarks> 
    public class MemoryCache : IMemoryCache
    {
        private readonly CacheSettings _settings;
        private readonly ILogger _logger;
        private readonly ConcurrentDictionary<string, (object value, DateTime timeStamp)> _data;
        private static MemoryCache? _instance = null;

        /// <summary>
        /// Private constructor of <see cref="MemoryCache"/>.
        /// </summary>
        /// <remarks>
        /// This prevents more than one instance being created per process.
        /// </remarks>
        /// <param name="settings">The settings for the cache.</param>
        /// <exception cref="ArgumentNullException">Thrown when settings or logger is null.</exception>"
        private MemoryCache(CacheSettings settings, ILogger logger)
        {
            ArgumentNullException.ThrowIfNull(settings);
            ArgumentNullException.ThrowIfNull(logger);

            _settings = settings;
            _logger = logger;
            _data = new ConcurrentDictionary<string, (object value, DateTime timeStamp)>();
        }

        /// <summary>
        /// Initializes a new instance of a <see cref="MemoryCache"/>.
        /// </summary>
        /// <param name="settings">The settings for the cache.</param>
        /// <exception cref="ArgumentNullException">Thrown when settings or logger is null.</exception>"
        public static MemoryCache GetInstance(CacheSettings settings, ILogger logger) => _instance ??= new MemoryCache(settings, logger);

        /// <summary>
        /// Retrieves an  from the cache using a key. Note this is a synchronous operation in <see cref="MemoryCache"/>.
        /// </summary>
        /// <remarks>
        /// Returns the  if it exists in the cache, null otherwise.
        /// </remarks>
        /// <param name="key">The key of the  to retrieve.</param>
        public T? Get<T>(string key)
        {
            _logger.LogInformation("Attempting to retrieve  with key {key} from local cache.", key);
            if (!_data.TryGetValue(key, out var ))
            {
                _logger.LogInformation(" with key {key} not found in local cache.", key);
                return default;
            }

            if (IsExpired(, _settings.AbsoluteExpiration))
            {
                _logger.LogWarning(" with key {key} found in local cache but was expired. Removing from cache.", key);
                _data.TryRemove(key, out _);
                return default;
            }

            _logger.LogInformation(" with key {key} retrieved from local cache.", key);
            return LogAndReturnForGet<T>(.value, key);
        }


        private static bool IsExpired((object value, DateTime timeStamp) entry, int absoluteExpirationMinutes)
        {
            return DateTime.UtcNow - entry.timeStamp > TimeSpan.FromMinutes(absoluteExpirationMinutes);
        }

        /// <summary>
        /// Adds an  from the cache using a key.
        /// </summary>
        /// <remarks>
        /// Returns true if the  was added to the cache, false otherwise.
        /// </remarks>
        /// <param name="key">The key to use for the .</param>
        /// <param name="">The  to add to the cache.</param>
        public bool Set<T>(string key, T )
        {
            _logger.LogInformation("Attempting to add  with key {key} to cache.", key);
            if (key is null ||  is null)
            {
                _logger.LogWarning("Failed to add  with key {key} to cache. Key or  is null.", key);
                return false;
            }

            bool success = _data.TryAdd(key, (, DateTime.UtcNow));
            return LogAndReturnForSet(key, success);
        }

        /// <summary>
        /// Remove an  to the cache with a specified key.
        /// </summary>
        /// <remarks>
        /// Returns true if the  was remove from the cache, false otherwise.
        /// </remarks>
        /// <param name="key">The key of the  to remove.</param>
        public bool Remove(string key)
        {
            _logger.LogInformation("Attempting to remove  with key {key} from cache.", key);
            if (key is null)
            {
                _logger.LogWarning("Failed to remove  with key {key} from cache. Key is null.", key);
                return false;
            }

            bool success = _data.TryRemove(key, out _);
            return LogAndReturnForRemove(key, success);
        }

        /// <summary>
        /// Retrieves an object representation of the cache.
        /// </summary>
        /// <remarks>
        /// In this case, a <see cref="ConcurrentDictionary{TKey, TValue}"/> object is returned.
        /// <see cref="{TValue}"/> is a tuple of <see cref="object"/> and <see cref="DateTime"/>.
        /// </remarks>
        public object GetCache() => _data;

        // Logging methods to simplify return statements
        private T? LogAndReturnForGet<T>(object value, string key)
        {
            bool success = value is not null && value is T;

            string message = success
                ? $"Get operation completed for key: {key}"
                : $"Get operation failed for key: {key}";

            if (success)
                _logger.LogInformation(message);
            else
                _logger.LogError(message);

            return success ? (T)value! : default;
        }

        private bool LogAndReturnForSet(string key, bool success)
        {
            string message = success
                ? $"Set operation completed for key: {key}"
                : $"Set operation failed for key: {key}";

            if (success)
                _logger.LogInformation(message);
            else
                _logger.LogError(message);

            return success;
        }

        private bool LogAndReturnForRemove(string key, bool success)
        {
            string message = success
                ? $"Remove operation completed for key: {key}"
                : $"Remove operation failed for key: {key}";

            if (success)
                _logger.LogInformation(message);
            else
                _logger.LogError(message);

            return success;
        }
    }
}
