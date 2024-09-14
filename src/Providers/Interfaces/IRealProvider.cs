using Polly.Wrap;

namespace DataFerry.Providers.Interfaces
{
    /// <summary>
    /// An interface for the data source.
    /// </summary>
    public interface IRealProvider<T>
    {
        /// <summary>
        /// Asynchronousy get data from data source
        /// </summary>
        /// <param name="key"></param>
        /// <returns>The data <typeparamref name="T"/></returns>
        Task<T?> GetFromSourceAsync(string key);

        /// <summary>
        /// Asynchronously set data in data source (upsert)
        /// </summary>
        /// <param name="data"></param>
        /// <returns>True if the operation is successful; otherwise, false.</returns>
        Task<bool> SetInSourceAsync(T data);

        /// <summary>
        /// Asynchronously delete data from data source
        /// </summary>
        /// <param name="key"></param>
        /// <returns>True if the operation is successful; otherwise, false.</returns>
        Task<bool> DeleteFromSourceAsync(string key);

        /// <summary>
        /// Asynchronousy get batch of data from data source
        /// </summary>
        /// <param name="keys"></param>
        /// <param name="cancellationToken"></param>
        /// <returns>A <typeparamref name="IDictionary{string, T}"/> of keys and data.</returns>
        Task<IDictionary<string, T>> GetBatchFromSourceAsync(IEnumerable<string> keys, CancellationToken? cancellationToken = null);

        /// <summary>
        /// Asynchronously sets multiple entries in the data source using specified keys.
        /// </summary>
        /// <param name="data">The data to set in data soure.</param>
        /// <param name="cancellationToken">Optional cancellation token.</param>
        /// <returns>True if the operation is successful; otherwise, false.</returns>
        Task<IDictionary<string, bool>> SetBatchInSourceAsync(IDictionary<string, T> data, CancellationToken? cancellationToken = null);

        /// <summary>
        /// Asynchronously removes multiple entries from the data source using specified keys.
        /// </summary>
        /// <param name="keys">The keys of the entries to remove.</param>
        /// <param name="cancellationToken">Optional cancellation token.</param>
        /// <returns>True if the operation is successful; otherwise, false.</returns>
        Task<IDictionary<string, bool>> RemoveBatchFromSourceAsync(IEnumerable<string> keys, CancellationToken? cancellationToken = null);

        /// <summary>
        /// The polly policy used in the <see cref="IRealProvider{T}"/>.
        /// </summary>
        AsyncPolicyWrap<object> Policy { get; set; }
    }
}