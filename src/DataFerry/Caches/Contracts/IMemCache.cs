namespace lvlup.DataFerry.Caches.Contracts
{
    /// <summary>
    /// A contract for interacting with an in-memory cache with time-to-live (TTL) support.
    /// </summary>
    /// <typeparam name="TKey">The type of the keys in the cache.</typeparam>
    /// <typeparam name="TValue">The type of the values in the cache.</typeparam>
    /// <remarks>Based on FastCache and BitFaster.</remarks>
    public interface IMemCache<TKey, TValue> : IEnumerable<KeyValuePair<TKey, TValue>>, IDisposable where TKey : notnull
    {
        /// <summary>
        /// Returns the total number of items in the cache, 
        /// including expired items that have not yet been removed by the cleanup process.
        /// </summary>
        int Count { get; }

        /// <summary>
        /// Gets or sets the maximum number of items that the cache can hold.
        /// </summary>
        int MaxSize { get; set; }

        /// <summary>
        /// Clears all items from the cache.
        /// </summary>
        void Clear();

        /// <summary>
        /// Adds or updates an item in the cache. If the item already exists, its value and TTL are updated.
        /// </summary>
        /// <param name="key">The unique identifier for the item.</param>
        /// <param name="value">The data associated with the key.</param>
        /// <param name="ttl">The time-to-live (TTL) for the item, after which it will expire.</param>
        void AddOrUpdate(TKey key, TValue value, TimeSpan ttl);

        /// <summary>
        /// Attempts to retrieve the value associated with the given key.
        /// </summary>
        /// <param name="key">The key of the item to retrieve.</param>
        /// <param name="value">
        /// If the key is found, this output parameter will contain its corresponding value; otherwise, it will contain the default value for the type.
        /// </param>
        /// <returns>True if the key was found and its value was retrieved; otherwise, false.</returns>
        bool TryGet(TKey key, out TValue value);

        /// <summary>
        /// Adds a key-value pair to the cache if the key does not already exist. 
        /// If the key exists, returns the existing value without updating it.
        /// </summary>
        /// <param name="key">The key of the item to add or retrieve.</param>
        /// <param name="valueFactory">A function to generate the value if the key is not found.</param>
        /// <param name="ttl">The time-to-live (TTL) for the item, after which it will expire.</param>
        /// <returns>The value associated with the specified key.</returns>
        TValue GetOrAdd(TKey key, Func<TKey, TValue> valueFactory, TimeSpan ttl);

        /// <summary>
        /// Adds a key-value pair to the cache if the key does not already exist, 
        /// using the provided factory function to generate the value. 
        /// If the key exists, returns the existing value without updating it.
        /// </summary>
        /// <param name="key">The key of the item to add or retrieve.</param>
        /// <param name="valueFactory">A function to generate the value if the key is not found.</param>
        /// <param name="ttl">The time-to-live (TTL) for the item, after which it will expire.</param>
        /// <param name="factoryArgument">An argument to pass to the `valueFactory` function.</param>
        /// <returns>The value associated with the specified key.</returns>
        TValue GetOrAdd<TArg>(TKey key, Func<TKey, TArg, TValue> valueFactory, TimeSpan ttl, TArg factoryArgument);

        /// <summary>
        /// Adds a key-value pair to the cache if the key does not already exist. 
        /// If the key exists, its value is returned without any updates.
        /// </summary>
        /// <param name="key">The key of the item to add or retrieve.</param>
        /// <param name="value">The value to add if the key is not found.</param>
        /// <param name="ttl">The time-to-live (TTL) for the item, after which it will expire.</param>
        TValue GetOrAdd(TKey key, TValue value, TimeSpan ttl);

        /// <summary>
        /// Attempts to remove the item associated with the specified key from the cache.
        /// </summary>
        /// <param name="key">The key of the item to remove.</param>
        void Remove(TKey key);

        /// <summary>
        /// Refresh the time-to-live value for the associated item.
        /// </summary>
        /// <param name="key">The key of the item to refresh.</param>
        /// <param name="ttl">The new time-to-live value for the item.</param>
        void Refresh(TKey key, TimeSpan ttl);

        /// <summary>
        /// Evicts all expired items from the cache.
        /// </summary>
        void EvictExpired();

        /// <summary>
        /// Check backplane for messages from subscription
        /// </summary>
        /// <returns>Used to synchronize in-memory caches across nodes.</returns>
        void CheckBackplane();
    }
}
