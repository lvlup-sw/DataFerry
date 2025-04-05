using lvlup.DataFerry.Caching.Abstractions;

namespace lvlup.DataFerry.Caching.Metrics;

/// <summary>
/// Metrics published by <see cref="ReplacementPolicyCache{TKey,TValue}"/>.
/// </summary>
public static class ReplacementPolicyCacheMetrics
{
    public const string CacheMeterName = "Dataferry.Cache.Memory";
    public const string Hits = "rpcache.hits";
    public const string Misses = "rpcache.misses";
    public const string Expirations = "rpcache.expirations";
    public const string Adds = "rpcache.adds";
    public const string Removals = "rpcache.removals";
    public const string Evictions = "rpcache.evictions";
    public const string Updates = "rpcache.updates";
    public const string Count = "rpcache.entries";
    public const string Compacted = "rpcache.compacted_entries";
}