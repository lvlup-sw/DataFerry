using System.Diagnostics.Metrics;
using lvlup.DataFerry.Caching.Abstractions;
using lvlup.DataFerry.Caching.Options;

namespace lvlup.DataFerry.Caching.Lru;

/// <summary>
/// Builder for creating LRU <see cref="ReplacementPolicyCache{TKey, TValue}"/> instances.
/// </summary>
public class ReplacementPolicyCacheLruBuilder<TKey, TValue>
    where TKey : notnull
{
    public ReplacementPolicyCacheLruBuilder(string name) { /* Implementation */ }

    /// <summary> Sets the options using the consolidated options class. </summary>
    public ReplacementPolicyCacheLruBuilder<TKey, TValue> WithOptions(ReplacementPolicyCacheOptions<TKey> options) { /* Implementation */ return this; }

    public ReplacementPolicyCacheLruBuilder<TKey, TValue> WithMeterFactory(IMeterFactory? meterFactory) { /* Implementation */ return this; }

    public ReplacementPolicyCache<TKey, TValue> Build() { /* Implementation */ throw new NotImplementedException(); }
}