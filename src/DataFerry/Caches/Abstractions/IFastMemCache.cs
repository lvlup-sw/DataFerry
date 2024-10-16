namespace lvlup.DataFerry.Caches.Abstractions
{
    /// <summary>
    /// A contract for interacting with an in-memory cache with time-to-live (TTL) support.
    /// </summary>
    /// <typeparam name="TKey">The type of the keys in the cache.</typeparam>
    /// <typeparam name="TValue">The type of the values in the cache.</typeparam>
    /// <remarks>Based on FastCache.</remarks>
    public interface IFastMemCache<TKey, TValue> : IEnumerable<KeyValuePair<TKey, TValue>>, IDisposable where TKey : notnull
    {
        /// <summary>
        /// Gets the number of items in the cache.
        /// </summary>
        int Count { get; }

        /// <summary>
        /// Clears all items from the cache.
        /// </summary>
        void Clear();

        /// <summary>
        /// Adds or updates an item in the cache with the specified time-to-live (TTL).
        /// </summary>
        /// <param name="key">The key of the item to add or update.</param>
        /// <param name="value">The value of the item to add or update.</param>
        /// <param name="ttl">The time-to-live for the item in the cache.</param>
        void AddOrUpdate(TKey key, TValue value, TimeSpan ttl);

        /// <summary>
        /// Attempts to retrieve an item from the cache.
        /// </summary>
        /// <param name="key">The key of the item to retrieve.</param>
        /// <param name="value">When this method returns, contains the value associated with the specified key, if the key is found; otherwise, the default value for the type of the <paramref name="value" /> parameter. This parameter is passed uninitialized.</param>
        /// <returns><c>true</c> if the cache contains an item with the specified key; otherwise, <c>false</c>.</returns>
        bool TryGet(TKey key, out TValue value);

        /// <summary>
        /// Gets an item from the cache or adds it if it doesn't exist, using the specified value factory and TTL.
        /// </summary>
        /// <param name="key">The key of the item to get or add.</param>
        /// <param name="valueFactory">The function used to generate the value if the key is not found.</param>
        /// <param name="ttl">The time-to-live for the item in the cache.</param>
        /// <returns>The value associated with the specified key.</returns>
        TValue GetOrAdd(TKey key, Func<TKey, TValue> valueFactory, TimeSpan ttl);

        /// <summary>
        /// Gets an item from the cache or adds it if it doesn't exist, using the specified value factory, TTL, and factory argument.
        /// </summary>
        /// <typeparam name="TArg">The type of the argument to be passed to the value factory.</typeparam>
        /// <param name="key">The key of the item to get or add.</param>
        /// <param name="valueFactory">The function used to generate the value if the key is not found.</param>
        /// <param name="ttl">The time-to-live for the item in the cache.</param>
        /// <param name="factoryArgument">The argument to be passed to the value factory.</param>
        /// <returns>The value associated with the specified key.</returns>
        TValue GetOrAdd<TArg>(TKey key, Func<TKey, TArg, TValue> valueFactory, TimeSpan ttl, TArg factoryArgument);

        /// <summary>
        /// Gets an item from the cache or adds it if it doesn't exist, using the specified value and TTL.
        /// </summary>
        /// <param name="key">The key of the item to get or add.</param>
        /// <param name="value">The value to add if the key is not found.</param>
        /// <param name="ttl">The time-to-live for the item in the cache.</param>
        /// <returns>The value associated with the specified key.</returns>
        TValue GetOrAdd(TKey key, TValue value, TimeSpan ttl);

        /// <summary>
        /// Removes an item from the cache.
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
