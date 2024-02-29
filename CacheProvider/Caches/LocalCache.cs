using Microsoft.Extensions.Options;
using System.Collections.Concurrent;

namespace CacheProvider.Caches
{
    /// <summary>
    /// LocalCache is an in-memory caching implementation with asynchronous operations.
    /// </summary>
    /// <remarks>
    /// This class inherits the <see cref="ICache"/> interface and makes use of a <see cref="ConcurrentDictionary{TKey, TValue}"/> object behind the scenes.
    /// Cache invalidation is also implemented in this class using <see cref="TimeSpan"/>.
    /// </remarks> 
    public class LocalCache : ILocalCache
    {
        private readonly CacheSettings _settings;
        private readonly ConcurrentDictionary<string, (object value, DateTime timeStamp)> _data;
        private static LocalCache? _instance = null;

        /// <summary>
        /// Private constructor of <see cref="LocalCache"/>.
        /// </summary>
        /// <remarks>
        /// This prevents more than one instance being created per process.
        /// </remarks>
        /// <param name="settings">The settings for the cache.</param>
        /// <exception cref="ArgumentNullException"></exception>"
        private LocalCache(IOptions<CacheSettings> settings)
        {
            ArgumentNullException.ThrowIfNull(settings.Value);

            _settings = settings.Value;
            _data = new ConcurrentDictionary<string, (object value, DateTime timeStamp)>();
        }

        /// <summary>
        /// Initializes a new instance of a <see cref="LocalCache"/>.
        /// </summary>
        /// <param name="settings">The settings for the cache.</param>
        public static LocalCache GetInstance(IOptions<CacheSettings> settings) => _instance ??= new LocalCache(settings);

        /// <summary>
        /// Retrieves an item from the cache using a key. Note this is a synchronous operation in <see cref="LocalCache"/>.
        /// </summary>
        /// <remarks>
        /// Returns the item if it exists in the cache, null otherwise.
        /// </remarks>
        /// <param name="key">The key of the item to retrieve.</param>
        public T? GetItem<T>(string key)
        {
            if (!_data.TryGetValue(key, out var item))
            {
                return default;
            }

            if (IsExpired(item, _settings.AbsoluteExpiration))
            {
                _data.TryRemove(key, out _);
                return default;
            }

            return item.value is T typeValue
                ? typeValue
                : default;
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
    }
}
