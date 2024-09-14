namespace DataFerry.Providers.Interfaces
{
    /// <summary>
    /// An interface for the Cache Provider.
    /// </summary>
    /// <remarks>
    /// The main benefit of this class is how it tightly couples
    /// the database and cache, ensuring synchronous behavior.
    /// </remarks>
    public interface ICacheProvider<T> where T : class
    {
        /// <summary>
        /// Gets the record from the cache and data source with a specified key.
        /// </summary>
        /// <param name="key">The key of the record to retrieve.</param>
        /// <param name="flags">Flags to configure the behavior of the operation.</param>
        /// <returns>The data <typeparamref name="T"/></returns>
        Task<T?> GetDataAsync(string key, GetFlags? flags = null);

        /// <summary>
        /// Adds a record to the cache and data source with a specified key.
        /// </summary>
        /// <param name="key">The key of the record to add.</param>
        /// <param name="data">The data to add to the cache.</param>
        /// <param name="expiration">The expiration time for the record.</param>
        /// <returns>True if the operation is successful; otherwise, false.</returns>
        Task<bool> SetDataAsync(string key, T data, TimeSpan? expiration = default);

        /// <summary>
        /// Removes a record from the cache and data source with a specified key.
        /// </summary>
        /// <returns>Returns true if the entry was removed from the cache and data source, false otherwise.</returns>
        /// <param name="key">The key of the record to remove.</param>
        Task<bool> RemoveDataAsync(string key);

        /// <summary>
        /// Gets multiple records from the cache or data source with the specified keys.
        /// </summary>
        /// <param name="keys">The keys of the records to retrieve.</param>
        /// <param name="flags">Flags to configure the behavior of the operation.</param>
        /// <param name="cancellationToken">Cancellation token to stop the operation.</param>
        /// <returns>A <typeparamref name="IDictionary"/> of <typeparamref name="string"/> keys and <typeparamref name="T"/> data</returns>
        Task<IDictionary<string, T>> GetDataBatchAsync(IEnumerable<string> keys, GetFlags? flags = null, CancellationToken? cancellationToken = null);

        /// <summary>
        /// Sets multiple records in the cache and data source with the specified keys.
        /// </summary>
        /// <param name="IDictionary{string, T}">A dictionary containing the keys and data to store in the cache and data source.</param>
        /// <param name="cancellationToken">Cancellation token to stop the operation.</param>
        /// <returns>True if all records were set successfully; otherwise, false.</returns>
        Task<IDictionary<string, bool>> SetDataBatchAsync(IDictionary<string, T> data, TimeSpan? expiration = default, CancellationToken ? cancellationToken = null);

        /// <summary>
        /// Removes multiple records from the cache and data source with the specified keys.
        /// </summary>
        /// <param name="keys">The keys of the records to remove.</param>
        /// <param name="cancellationToken">Cancellation token to stop the operation.</param>
        /// <returns>True if all records were removed successfully; otherwise, false.</returns>
        Task<IDictionary<string, bool>> RemoveDataBatchAsync(IEnumerable<string> keys, CancellationToken? cancellationToken = null);

        /// <summary>
        /// Gets the data source object representation.
        /// </summary>
        IRealProvider<T> RealProvider { get; }

        /// <summary>
        /// Gets the cache object representation.
        /// </summary>
        DistributedCache<T> Cache { get; }
    }
}