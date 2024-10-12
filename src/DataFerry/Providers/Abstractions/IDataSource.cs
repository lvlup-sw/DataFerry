using Polly.Wrap;
using System.Buffers;
using StackExchange.Redis;

namespace lvlup.DataFerry.Providers.Abstractions
{
    /// <summary>
    /// A contract for interacting with a data source that supports low allocation data transfers.
    /// </summary>
    public interface IDataSource
    {
        /// <summary>
        /// Attempts to retrieve an existing data source item synchronously.
        /// </summary>
        /// <param name="key">The unique key for the data source item.</param>
        /// <param name="destination">The target to write the data source contents on success.</param>
        /// <returns><c>true</c> if the data source item is found and successfully written to the <paramref name="destination"/>, <c>false</c> otherwise.</returns>
        bool GetFromSource(string key, IBufferWriter<byte> destination);

        /// <summary>
        /// Sets or overwrites a data source item synchronously.
        /// </summary>
        /// <param name="key">The key of the entry to create.</param>
        /// <param name="value">The value for this data source entry, represented as a <see cref="ReadOnlySequence{byte}"/> of bytes.</param>
        /// <returns><c>true</c> if the data source item is set successfully, <c>false</c> otherwise.</returns>
        bool SetInSource(string key, ReadOnlySequence<byte> value);

        /// <summary>
        /// Refreshes a value in the data source synchronously based on its key, resetting its sliding expiration timeout (if any).
        /// </summary>
        /// <param name="key">A string identifying the requested value.</param>  
        /// <returns><c>true</c> if the data source item is refreshed successfully, <c>false</c> otherwise.</returns>
        bool RefreshInSource(string key);

        /// <summary>
        /// Removes the value with the given key synchronously.
        /// </summary>
        /// <param name="key">A string identifying the requested value.</param>
        /// <returns><c>true</c> if the data source item is removed successfully, <c>false</c> otherwise.</returns>
        bool RemoveFromSource(string key);

        /// <summary>
        /// Asynchronously attempts to retrieve an existing data source entry.
        /// </summary>
        /// <param name="key">The unique key for the data source entry.</param>
        /// <param name="destination">The target to write the data source contents on success.</param>
        /// <param name="token">The <see cref="CancellationToken"/> used to propagate notifications that the operation should be canceled.</param>
        /// <returns><c>true</c>  
        /// if the data source entry is found and successfully written to the<paramref name="destination"/>, <c>false</c> otherwise.</returns>
        ValueTask<bool> GetFromSourceAsync(string key, IBufferWriter<byte> destination, CancellationToken token = default);

        /// <summary>
        /// Asynchronously sets or overwrites a data source entry.
        /// </summary>
        /// <param name="key">The key of the entry to create.</param>
        /// <param name="value">The value for this data source entry, represented as a <see cref="ReadOnlySequence{byte}"/> of bytes.</param>
        /// <param name="options">The data source options for the value.</param>
        /// <param name="token">The <see cref="CancellationToken"/> used to propagate notifications that the operation should be canceled.</param>
        /// <returns><c>true</c>  
        /// if the data source item is set successfully, <c>false</c> otherwise.</returns>
        ValueTask<bool> SetInSourceAsync(string key, ReadOnlySequence<byte> value, CancellationToken token = default);

        /// <summary>
        /// Asynchronously refreshes a value in the data source based on its key, resetting its sliding expiration timeout (if any).
        /// </summary>
        /// <param name="key">A string identifying the requested value.</param>
        /// <param name="token">The  
	    /// <see cref="CancellationToken"/> used to propagate notifications that the operation should be canceled.</param>
        /// <returns><c>true</c>  
        /// if the data source item is refreshed successfully, <c>false</c> otherwise.</returns>
        ValueTask<bool> RefreshInSourceAsync(string key, CancellationToken token = default);

        /// <summary>
        /// Asynchronously removes the value with the given key from the data source.
        /// </summary>
        /// <param name="key">A string identifying the requested value.</param>
        /// <param name="token">The  
        /// <see cref = "CancellationToken" /> used to propagate notifications that the operation should be canceled.</param>
        /// <returns><c>true</c>
        /// if the data source item is removed successfully, <c>false</c> otherwise.</returns>
        ValueTask<bool> RemoveFromSourceAsync(string key, CancellationToken token = default);

        /// <summary>
        /// Asynchronously retrieves a batch of values from the data source based on their keys.
        /// </summary>
        /// <typeparam name="T">The type of the values to be retrieved.</typeparam>
        /// <param name="keys">A collection of keys identifying the requested values.</param>
        /// <param name="callback">A callback function to be invoked for each retrieved key-value pair.</param>
        /// <param name="token">The <see cref="CancellationToken"/> used to propagate notifications that the operation should be canceled.</param>
        ValueTask GetBatchFromSourceAsync<T>(IEnumerable<string> keys, Action<string, T?> callback, CancellationToken token = default);

        /// <summary>
        /// Asynchronously sets a batch of values in the data source.
        /// </summary>
        /// <param name="data">A dictionary containing the key-value pairs to be set in the data source, where the values are represented as <see cref="ReadOnlySequence{byte}"/> of bytes.</param>
        /// <param name="absoluteExpiration">The absolute expiration time for the entries.</param>
        /// <param name="callback">A callback function to be invoked for each key, indicating whether the operation was successful.</param>
        /// <param name="token">The <see cref="CancellationToken"/> used to propagate notifications that the operation should be canceled.</param>
        ValueTask SetBatchInSourceAsync(IDictionary<string, ReadOnlySequence<byte>> data, TimeSpan? absoluteExpiration, Action<string, bool> callback, CancellationToken token = default);

        /// <summary>
        /// Asynchronously refreshes a batch of values in the data source based on their keys, resetting their sliding expiration timeout (if any).
        /// </summary>
        /// <param name="keys">A collection of keys identifying the requested values.</param>
        /// <param name="callback">A callback function to be invoked for each key, indicating whether the refresh operation was successful.</param>
        /// <param name="token">The <see cref="CancellationToken"/> used to propagate notifications that the operation should be canceled.</param>
        ValueTask RefreshBatchFromSourceAsync(IEnumerable<string> keys, Action<string, bool> callback, CancellationToken token = default);

        /// <summary>
        /// Asynchronously removes a batch of values from the data sourcee based on their keys.
        /// </summary>
        /// <param name="keys">A collection of keys identifying the values to be removed.</param>
        /// <param name="callback">A callback function to be invoked for each key, indicating whether the remove operation was successful.</param>
        /// <param name="token">The <see cref="CancellationToken"/> used to propagate notifications that the operation should be canceled.</param>
        ValueTask RemoveBatchFromSourceAsync(IEnumerable<string> keys, Action<string, bool> callback, CancellationToken token = default);

        /// <summary>
        /// Get the configured Polly policy.
        /// </summary>
        AsyncPolicyWrap<object> GetPollyPolicy();

        /// <summary>
        /// Set the fallback value for the polly retry policy.
        /// </summary>
        /// <remarks>Policy will return <see cref="RedisValue.Null"/> if not set.</remarks>
        /// <param name="value"></param>
        void SetFallbackValue(object value);
    }
}