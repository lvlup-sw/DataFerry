using System.ComponentModel.DataAnnotations;
using lvlup.DataFerry.Caching.Abstractions;

namespace lvlup.DataFerry.Caching.Options;

/// <summary>
/// Options for implementations of <see cref="ReplacementPolicyCache{TKey,TValue}"/>.
/// </summary>
/// <typeparam name="TKey">Type of keys stored in the cache.</typeparam>
public class ReplacementPolicyCacheOptions<TKey>
    where TKey : notnull
{
    /// <summary>
    /// Gets or sets the maximum number of items that can be stored in the cache.
    /// </summary>
    /// <value>Defaults to 1024.</value>
    [Range(3, int.MaxValue - 1)]
    public int Capacity { get; set; } = 1024;

    /// <summary>
    /// Gets or sets the default time to evict individual items from the cache.
    /// </summary>
    /// <value>Defaults to 5 minutes.</value>
    [TimeSpan(minMs: 1)]
    public TimeSpan DefaultTimeToEvict { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Gets or sets the amount of time by which an items's eviction time is extended upon a cache hit.
    /// </summary>
    /// <value>Defaults to 5 minutes.</value>
    [TimeSpan(minMs: 1)]
    public TimeSpan ExtendedTimeToEvictAfterHit { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Gets or sets a value indicating whether an item's time to evict should be extended upon a cache hit.
    /// </summary>
    /// <value>Defaults to <see langword="false"/>.</value>
    public bool ExtendTimeToEvictAfterHit { get; set; }

    /// <summary>
    /// Gets or sets the cache's level of concurrency.
    /// </summary>
    /// <value>Defaults to <see cref="Environment.ProcessorCount"/>.</value>
    [Range(1, int.MaxValue)]
    public int ConcurrencyLevel { get; set; } = Environment.ProcessorCount;

    /// <summary>
    /// Gets or sets the custom time provider used for timestamp generation in the cache.
    /// </summary>
    /// <value>Defaults to <see langword="null"/>.</value>
    public TimeProvider? TimeProvider { get; set; }

    /// <summary>
    /// Gets or sets the comparer used to evaluate keys.
    /// </summary>
    /// <value>Defaults based on TKey type.</value>
    public IEqualityComparer<TKey> KeyComparer { get; set; }
        = typeof(TKey) == typeof(string) ? (IEqualityComparer<TKey>)StringComparer.Ordinal : EqualityComparer<TKey>.Default;

    /// <summary>
    /// Gets or sets a value indicating how often cache metrics are refreshed.
    /// </summary>
    /// <value>Defaults to 30 seconds.</value>
    [TimeSpan(min: "00:00:05")]
    public TimeSpan MetricPublicationInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Gets or sets a value indicating whether metrics are published or not.
    /// </summary>
    /// <value>Defaults to <see langword="true"/>.</value>
    public bool PublishMetrics { get; set; } = true;
}