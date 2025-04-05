using lvlup.DataFerry.Caching.Abstractions;

namespace lvlup.DataFerry.Caching.Metrics;

/// <summary>
/// Metrics published by <see cref="ReplacementPolicyCache{TKey,TValue}"/>.
/// </summary>
public static class ReplacementPolicyCacheMetrics
{
    // Use Meter name from Options?
    public const string CacheMeterName = "Dataferry.Caching";

    // Base Counters
    public const string Hits = "rpcache.hits";
    public const string Misses = "rpcache.misses";
    public const string Adds = "rpcache.adds";
    public const string Updates = "rpcache.updates";
    public const string Removals = "rpcache.removals";

    // Eviction Counters
    public const string Evictions = "rpcache.evictions";
    public const string EvictionsExpired = "rpcache.evictions.expired";
    public const string EvictionsCapacity = "rpcache.evictions.capacity";

    // Latency Histograms
    public const string GetLatency = "rpcache.get.latency";
    public const string SetLatency = "rpcache.set.latency";
    public const string RemoveLatency = "rpcache.remove.latency";

    // Gauges
    public const string Count = "rpcache.count";
    public const string HitRatio = "rpcache.hit_ratio";
    public const string EstimatedSize = "rpcache.estimated_size";
}