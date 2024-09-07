using Polly.Wrap;

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

        /// <summary>
        /// Asynchronously sets multiple entries in the data source using specified keys.
        /// </summary>
        /// <param name="data">The data to cache.</param>
        /// <param name="cancellationToken">Optional cancellation token.</param>
        /// <returns>True if the operation is successful; otherwise, false.</returns>
        Task<bool> SetBatchInCacheAsync(Dictionary<string, T> data, CancellationToken? cancellationToken = null);

        /// <summary>
        /// Asynchronously removes multiple entries from the data source using specified keys.
        /// </summary>
        /// <param name="keys">The keys of the entries to remove.</param>
        /// <param name="cancellationToken">Optional cancellation token.</param>
        /// <returns>True if the operation is successful; otherwise, false.</returns>
        Task<bool> RemoveBatchFromCacheAsync(IEnumerable<string> keys, CancellationToken? cancellationToken = null);

        /// <summary>
        /// Set the polly policy used in the <see cref="IRealProvider{T}"/>.
        /// </summary>
        /// <param name="policy"></param>
        void ConfigurePollyPolicy(AsyncPolicyWrap<object> policy);
    }
}