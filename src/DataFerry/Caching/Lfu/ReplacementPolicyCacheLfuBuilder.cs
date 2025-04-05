using System.Diagnostics.Metrics;
using lvlup.DataFerry.Caching.Abstractions;
using lvlup.DataFerry.Caching.Options;

namespace lvlup.DataFerry.Caching.Lfu;

/// <summary>
/// Builder for creating LFU <see cref="ReplacementPolicyCache{TKey, TValue}"/> instances.
/// </summary>
public class ReplacementPolicyCacheLfuBuilder<TKey, TValue> // Corrected LFU capitalization
    where TKey : notnull
{
    public ReplacementPolicyCacheLfuBuilder(string name) { /* Implementation */ }

    /// <summary> Sets the options using the consolidated options class. </summary>
    public ReplacementPolicyCacheLfuBuilder<TKey, TValue> WithOptions(ReplacementPolicyCacheOptions<TKey> options) { /* Implementation */ return this; } // Updated options type

    public ReplacementPolicyCacheLfuBuilder<TKey, TValue> WithMeterFactory(IMeterFactory? meterFactory) { /* Implementation */ return this; }

    public ReplacementPolicyCache<TKey, TValue> Build() { /* Implementation */ throw new NotImplementedException(); }
}