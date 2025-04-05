using System.Buffers;
using Microsoft.Extensions.Caching.Distributed;

namespace lvlup.DataFerry.Caching.Clients.Contracts;

/// <summary>
/// Interface for interacting with a Redis cache store, supporting low-allocation operations.
/// </summary>
public interface IRedisClient : IDisposable
{
    /// <summary>
    /// Asynchronously attempts to retrieve an item from Redis, writing the result to the buffer.
    /// </summary>
    /// <param name="key">The cache key.</param>
    /// <param name="writer">The buffer to write the retrieved value to.</param>
    /// <param name="token">Cancellation token.</param>
    /// <returns>True if the item was found and written, false otherwise.</returns>
    ValueTask<bool> TryGetAsync(string key, IBufferWriter<byte> writer, CancellationToken token = default);

    /// <summary>
    /// Asynchronously retrieves an item from Redis as a byte array.
    /// </summary>
    /// <param name="key">The cache key.</param>
    /// <param name="token">Cancellation token.</param>
    /// <returns>The cached byte array, or null if not found.</returns>
    ValueTask<byte[]?> GetBytesAsync(string key, CancellationToken token = default);

    /// <summary>
    /// Asynchronously sets or overwrites an item in Redis using low-allocation sequence.
    /// </summary>
    /// <param name="key">The cache key.</param>
    /// <param name="data">The data sequence to store.</param>
    /// <param name="options">Cache entry options (expiration).</param>
    /// <param name="token">Cancellation token.</param>
    Task SetAsync(string key, ReadOnlySequence<byte> data, DistributedCacheEntryOptions options, CancellationToken token = default);

    /// <summary>
    /// Asynchronously sets or overwrites an item in Redis using a byte array.
    /// </summary>
    /// <param name="key">The cache key.</param>
    /// <param name="data">The byte array to store.</param>
    /// <param name="options">Cache entry options (expiration).</param>
    /// <param name="token">Cancellation token.</param>
    Task SetBytesAsync(string key, byte[] data, DistributedCacheEntryOptions options, CancellationToken token = default);

    /// <summary>
    /// Asynchronously removes an item from Redis.
    /// </summary>
    /// <param name="key">The cache key.</param>
    /// <param name="token">Cancellation token.</param>
    Task RemoveAsync(string key, CancellationToken token = default);

    /// <summary>
    /// Asynchronously refreshes the expiration time of an item in Redis.
    /// </summary>
    /// <param name="key">The cache key.</param>
    /// <param name="token">Cancellation token.</param>
    Task RefreshAsync(string key, CancellationToken token = default);
}