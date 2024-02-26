namespace CacheObject.Caches
{
    /// <summary>
    /// A cache interface for caching arbitrary objects.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public interface ICache<T>
    {
        /// <summary>
        /// Asynchronously retrieves an item from the cache using a key.
        /// </summary>
        Task<T> GetItemAsync(string key);

        /// <summary>
        /// Asynchronously adds an item to the cache with a specified key.
        /// </summary>
        Task SetItemAsync(string key, T item);

        /// <summary>
        /// Asynchronously removes an item from the cache using a key.
        /// </summary>
        Task RemoveItemAsync(string key);

        /// <summary>
        /// Retrieves the count of items in the cache.
        /// </summary>
        int GetItemCount();

        /// <summary>
        /// Retrieves an item from the cache using a key.
        /// </summary>
        T GetItem(string key);

        /// <summary>
        /// Retrieves all items from the cache.
        /// </summary>
        object GetItems();
    }
}
