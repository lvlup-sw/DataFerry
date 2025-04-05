using System.Buffers;
using lvlup.DataFerry.Caching.Abstractions;
using Microsoft.Extensions.Caching.Distributed;

namespace lvlup.DataFerry.Caching.Adapters;

/// <summary>
/// Adapts a concrete DistributedReplacementPolicyCache implementation (which uses IRedisClient internally)
/// to the standard IBufferDistributedCache interface. Handles byte marshalling.
/// </summary>
/// <remarks>
/// Assumes the underlying cache works with object keys/values and handles its own serialization
/// if needed before interacting with IRedisClient (which works with bytes).
/// This adapter primarily passes byte representations through.
/// </remarks>
public class BufferDistributedCacheAdapter : IBufferDistributedCache
{
    private readonly DistributedReplacementPolicyCache<object, object> _cache;

    public BufferDistributedCacheAdapter(DistributedReplacementPolicyCache<object, object> cache)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
    }

    public byte[]? Get(string key)
    {
        throw new NotImplementedException();
    }

    public Task<byte[]?> GetAsync(string key, CancellationToken token = new CancellationToken())
    {
        throw new NotImplementedException();
    }

    public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
    {
        throw new NotImplementedException();
    }

    public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options,
        CancellationToken token = new CancellationToken())
    {
        throw new NotImplementedException();
    }

    public void Refresh(string key)
    {
        throw new NotImplementedException();
    }

    public Task RefreshAsync(string key, CancellationToken token = new CancellationToken())
    {
        throw new NotImplementedException();
    }

    public void Remove(string key)
    {
        throw new NotImplementedException();
    }

    public Task RemoveAsync(string key, CancellationToken token = new CancellationToken())
    {
        throw new NotImplementedException();
    }

    public bool TryGet(string key, IBufferWriter<byte> destination)
    {
        throw new NotImplementedException();
    }

    public ValueTask<bool> TryGetAsync(string key, IBufferWriter<byte> destination, CancellationToken token = new CancellationToken())
    {
        throw new NotImplementedException();
    }

    public void Set(string key, ReadOnlySequence<byte> value, DistributedCacheEntryOptions options)
    {
        throw new NotImplementedException();
    }

    public ValueTask SetAsync(string key, ReadOnlySequence<byte> value, DistributedCacheEntryOptions options,
        CancellationToken token = new CancellationToken())
    {
        throw new NotImplementedException();
    }
}