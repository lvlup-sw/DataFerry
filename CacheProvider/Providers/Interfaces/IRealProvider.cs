namespace CacheProvider.Providers.Interfaces
{
    /// <summary>
    /// An interface for the real provider.
    /// </summary>
    public interface IRealProvider<T>
    {
        /// <summary>
        /// Asynchronousy get data from data source
        /// </summary>
        /// <param name="key"></param>
        Task<T?> GetAsync(string key);

        /// <summary>
        /// Asynchronously set data in data source
        /// </summary>
        /// <param name="key"></param>
        /// <param name="data"></param>
        /// <returns></returns>
        Task<bool> SetAsync(T data);

        /// <summary>
        /// Asynchronously delete data from data source
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        Task<bool> DeleteAsync(string key);

        /// <summary>
        /// Asynchronousy get batch of data from data source
        /// </summary>
        /// <param name="keys"></param>
        /// <param name="cancellationToken"></param>
        Task<Dictionary<string, T?>> GetBatchAsync(IEnumerable<string> keys, CancellationToken? cancellationToken = null);
    }
}