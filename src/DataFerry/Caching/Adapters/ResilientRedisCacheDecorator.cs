using System.Buffers;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Polly.Wrap;

namespace lvlup.DataFerry.Caching.Adapters;

/// <summary>
/// Decorator for IBufferDistributedCache (typically wrapping RedisCache)
/// that adds resilience using Polly policies and provides hooks for
/// custom events and metrics.
/// </summary>
public sealed class ResilientRedisCacheDecorator : IBufferDistributedCache, IDisposable
{
    private readonly IBufferDistributedCache _innerCache;
    private readonly AsyncPolicyWrap _asyncPolicy;
    private readonly PolicyWrap? _syncPolicy;
    private readonly ILogger<ResilientRedisCacheDecorator> _logger;
    // private readonly DistributedCacheMetricsInstruments _metrics;
    // TODO: Define and inject custom events if desired

    public ResilientRedisCacheDecorator(
        IBufferDistributedCache innerCache, // Inject the cache registered by AddStackExchangeRedisCache
        AsyncPolicyWrap asyncPolicy,
        ILogger<ResilientRedisCacheDecorator> logger,
        PolicyWrap? syncPolicy = null
        /* DistributedCacheMetricsInstruments metrics = null */)
    {
        _innerCache = innerCache ?? throw new ArgumentNullException(nameof(innerCache));
        _asyncPolicy = asyncPolicy ?? throw new ArgumentNullException(nameof(asyncPolicy));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _syncPolicy = syncPolicy;
        // _metrics = metrics;
    }

    public void Dispose()
    {
        // TODO release managed resources here
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