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
    public class LocalCache : ICache
    {
        private readonly CacheSettings _settings;
        private readonly ConcurrentDictionary<string, (object value, DateTime timeStamp)> _data;

        /// <summary>
        /// Initializes a new instance of a <see cref="LocalCache"/>.
        /// </summary>
        /// <param name="settings">The settings for the cache.</param>
        /// <exception cref="ArgumentNullException"></exception>"
        public LocalCache(IOptions<CacheSettings> settings)
        {
            ArgumentNullException.ThrowIfNull(settings.Value);

            _settings = settings.Value;
            _data = new ConcurrentDictionary<string, (object value, DateTime timeStamp)>();
        }

        /// <summary>
        /// Asynchronously retrieves an item from the cache using a key.
        /// </summary>
        /// <remarks>
        /// Returns the item if it exists in the cache, null otherwise.
        /// </remarks>
        /// <param name="key">The key of the item to retrieve.</param>
        public async Task<T?> GetItemAsync<T>(string key)
        {
            if (_data.TryGetValue(key, out var entry))
            {
                if (DateTime.UtcNow - entry.timeStamp <= TimeSpan.FromMinutes(_settings.AbsoluteExpiration))
                {
                    return await Task.FromResult(entry.value is T item ? item : default);
                }
                else
                {
                    _data.TryRemove(key, out _);
                }
            }
            return await Task.FromResult(default(T));
        }

        /// <summary>
        /// Asynchronously removes an item from the cache using a key.
        /// </summary>
        /// <remarks>
        /// Returns true if the item was added to the cache, false otherwise.
        /// </remarks>
        /// <param name="key">The key to use for the item.</param>
        /// <param name="item">The item to add to the cache.</param>
        public async Task<bool> SetItemAsync<T>(string key, T item)
        {
            _data[key] = (item!, DateTime.UtcNow);
            return await Task.FromResult(true);
        }

        /// <summary>
        /// Asynchronously adds an item to the cache with a specified key.
        /// </summary>
        /// <remarks>
        /// Returns true if the item was remove from the cache, false otherwise.
        /// </remarks>
        /// <param name="key">The key of the item to remove.</param>
        public async Task<bool> RemoveItemAsync(string key)
        {
            var result = _data.TryRemove(key, out _);
            return await Task.FromResult(result);
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
