using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using System.Collections.Concurrent;
using System.Text.Json;

namespace CacheProvider.Caches
{
    /// <summary>
    /// LocalCache is an in-memory caching implementation with asynchronous operations.
    /// </summary>
    /// <remarks>
    /// This class inherits the <see cref="ILocalCache"/> interface and makes use of a <see cref="ConcurrentDictionary{TKey, TValue}"/> object behind the scenes.
    /// Cache invalidation is also implemented in this class using <see cref="TimeSpan"/>.
    /// </remarks> 
    public class LocalCache : ILocalCache
    {
        private readonly CacheSettings _settings;
        private readonly ILogger _logger;
        private readonly ConcurrentDictionary<string, (object value, DateTime timeStamp)> _data;
        private static LocalCache? _instance = null;

        /// <summary>
        /// Private constructor of <see cref="LocalCache"/>.
        /// </summary>
        /// <remarks>
        /// This prevents more than one instance being created per process.
        /// </remarks>
        /// <param name="settings">The settings for the cache.</param>
        /// <exception cref="ArgumentNullException">Thrown when settings or logger is null.</exception>"
        private LocalCache(CacheSettings settings, ILogger logger)
        {
            ArgumentNullException.ThrowIfNull(settings);
            ArgumentNullException.ThrowIfNull(logger);

            _settings = settings;
            _logger = logger;
            _data = new ConcurrentDictionary<string, (object value, DateTime timeStamp)>();
        }

        /// <summary>
        /// Initializes a new instance of a <see cref="LocalCache"/>.
        /// </summary>
        /// <param name="settings">The settings for the cache.</param>
        /// <exception cref="ArgumentNullException">Thrown when settings or logger is null.</exception>"
        public static LocalCache GetInstance(CacheSettings settings, ILogger logger) => _instance ??= new LocalCache(settings, logger);

        /// <summary>
        /// Retrieves an item from the cache using a key. Note this is a synchronous operation in <see cref="LocalCache"/>.
        /// </summary>
        /// <remarks>
        /// Returns the item if it exists in the cache, null otherwise.
        /// </remarks>
        /// <param name="key">The key of the item to retrieve.</param>
        public T? GetItem<T>(string key)
        {
            _logger.LogInformation("Attempting to retrieve item with key {key} from local cache.", key);
            if (!_data.TryGetValue(key, out var item))
            {
                _logger.LogWarning("Item with key {key} not found in local cache.", key);
                return default;
            }

            if (IsExpired(item, _settings.AbsoluteExpiration))
            {
                _logger.LogWarning("Item with key {key} found in local cache but was expired. Removing from cache.", key);
                _data.TryRemove(key, out _);
                return default;
            }

            _logger.LogInformation("Item with key {key} retrieved from local cache.", key);
            return LogAndReturnForGet<T>(item.value, key);
        }


        private static bool IsExpired((object value, DateTime timeStamp) entry, int absoluteExpirationMinutes)
        {
            return DateTime.UtcNow - entry.timeStamp > TimeSpan.FromMinutes(absoluteExpirationMinutes);
        }

        /// <summary>
        /// Adds an item from the cache using a key.
        /// </summary>
        /// <remarks>
        /// Returns true if the item was added to the cache, false otherwise.
        /// </remarks>
        /// <param name="key">The key to use for the item.</param>
        /// <param name="item">The item to add to the cache.</param>
        public bool SetItem<T>(string key, T item)
        {
            if (key is null || item is null) 
                return default;

            return _data.TryAdd(key, (item, DateTime.UtcNow));
        }

        /// <summary>
        /// Remove an item to the cache with a specified key.
        /// </summary>
        /// <remarks>
        /// Returns true if the item was remove from the cache, false otherwise.
        /// </remarks>
        /// <param name="key">The key of the item to remove.</param>
        public bool RemoveItem(string key)
        {
            if (key is null) return default;

            return _data.TryRemove(key, out _);
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
                ? $"GetItemAsync operation completed for key: {key}"
                : $"GetItemAsync operation failed for key: {key}";

            if (success)
                _logger.LogInformation(message);
            else
                _logger.LogError(message);

            return success ? (T)value! : default;
        }
    }
}
