using System.Collections.Concurrent;

namespace CacheProvider.Caches
{
    /// <summary>
    /// TestCache is an example of a local cache with asynchronous operations which inherits the <see cref="ICache"/> interface.
    /// </summary>
    /// <remarks>
    /// This class inherits the ICache interface and makes use of a <see cref="ConcurrentDictionary{TKey, TValue}"/> object behind the scenes.
    /// It is intended to be used for testing purposes only and should not be used in a production environment.
    /// </remarks> 
    public class LocalCache : ICache
    {
        private readonly ConcurrentDictionary<string, T> _data;

        /// <summary>
        /// Initializes a new instance of a <see cref="TestCache{T}"/>.
        /// </summary>
        public TestCache() => _data = new ConcurrentDictionary<string, T>();

        /// <summary>
        /// Asynchronously retrieves an item from the cache using a key.
        /// </summary>
        public async virtual Task<T?> GetItemAsync(string key)
        {
            return await Task.FromResult(_data.TryGetValue(key, out T? value) ? value! : default!);
        }

        /// <summary>
        /// Asynchronously removes an item from the cache using a key.
        /// </summary>
        public async virtual Task RemoveItemAsync(string key)
        {
            await Task.FromResult(_data.TryRemove(key, out T? removed));
        }

        /// <summary>
        /// Asynchronously adds an item to the cache with a specified key.
        /// </summary>
        public async virtual Task SetItemAsync(string key, T item)
        {
            await Task.FromResult(_data[key] = item);
        }

        /// <summary>
        /// Retrieves all items from the cache.
        /// </summary>
        public virtual object GetCache() => _data;

        /// <summary>
        /// Retrieves an item from the cache using a key.
        /// </summary>
        public virtual T GetItem(string key) => _data[key];
    }
}
