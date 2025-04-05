using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace lvlup.DataFerry.Caching.Metrics;

/// <summary>
/// Holds the System.Diagnostics.Metrics instruments for a specific cache instance.
/// </summary>
public sealed class ReplacementPolicyCacheInstruments : IDisposable
{
    private readonly Meter _meter;
    private readonly List<Instrument> _instruments = new();
    private long _hitCountInternal = 0;
    private long _missCountInternal = 0;

    // Counters (Using long)
    public Counter<long> HitsCounter { get; }
    public Counter<long> MissesCounter { get; }
    public Counter<long> AddsCounter { get; }
    public Counter<long> UpdatesCounter { get; }
    public Counter<long> RemovalsCounter { get; }
    public Counter<long> EvictionsCounter { get; }
    public Counter<long> EvictionsExpiredCounter { get; }
    public Counter<long> EvictionsCapacityCounter { get; }

    // Histograms (Using double for milliseconds)
    public Histogram<double> GetLatencyHistogram { get; }
    public Histogram<double> SetLatencyHistogram { get; }
    public Histogram<double> RemoveLatencyHistogram { get; }

    public ActivitySource ActivitySource { get; }

    public ReplacementPolicyCacheInstruments(IMeterFactory meterFactory, string cacheName)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheName);

        _meter = meterFactory.Create(ReplacementPolicyCacheMetrics.CacheMeterName, "1.0.0");
        ActivitySource = new ActivitySource($"{ReplacementPolicyCacheMetrics.CacheMeterName}.{cacheName}", "1.0.0");
        var tags = new KeyValuePair<string, object?>("cache_name", cacheName);

        // Create Instruments with specific types
        HitsCounter = CreateCounter<long>(ReplacementPolicyCacheMetrics.Hits, "Total cache hits.", tags);
        MissesCounter = CreateCounter<long>(ReplacementPolicyCacheMetrics.Misses, "Total cache misses.", tags);
        AddsCounter = CreateCounter<long>(ReplacementPolicyCacheMetrics.Adds, "Total items added.", tags);
        UpdatesCounter = CreateCounter<long>(ReplacementPolicyCacheMetrics.Updates, "Total items updated/overwritten.", tags);
        RemovalsCounter = CreateCounter<long>(ReplacementPolicyCacheMetrics.Removals, "Total items explicitly removed.", tags);
        EvictionsCounter = CreateCounter<long>(ReplacementPolicyCacheMetrics.Evictions, "Total items evicted (expired or capacity).", tags);
        EvictionsExpiredCounter = CreateCounter<long>(ReplacementPolicyCacheMetrics.EvictionsExpired, "Items evicted due to expiration.", tags);
        EvictionsCapacityCounter = CreateCounter<long>(ReplacementPolicyCacheMetrics.EvictionsCapacity, "Items evicted due to capacity limits.", tags);

        GetLatencyHistogram = CreateHistogram<double>(ReplacementPolicyCacheMetrics.GetLatency, "ms", "Latency for TryGet operations.", tags);
        SetLatencyHistogram = CreateHistogram<double>(ReplacementPolicyCacheMetrics.SetLatency, "ms", "Latency for Set operations.", tags);
        RemoveLatencyHistogram = CreateHistogram<double>(ReplacementPolicyCacheMetrics.RemoveLatency, "ms", "Latency for Remove operations.", tags);

        _instruments.Add(_meter.CreateObservableGauge<double>(ReplacementPolicyCacheMetrics.HitRatio, ObserveHitRatio, description: "Ratio of cache hits to total requests."));
        // Register ObservableGauge for Count here, needs a Func<> to the cache's GetCount() method
        // _instruments.Add(_meter.CreateObservableGauge<long>(ReplacementPolicyCacheMetrics.Count, () => _cacheInstance.GetCount(), description: "Current item count.", tags: tags));
    }

    public void RecordHit() => Interlocked.Increment(ref _hitCountInternal);
    public void RecordMiss() => Interlocked.Increment(ref _missCountInternal);
    private Measurement<double> ObserveHitRatio() { long total = Interlocked.Read(ref _hitCountInternal) + Interlocked.Read(ref _missCountInternal); double ratio = total == 0 ? 0 : (double)Interlocked.Read(ref _hitCountInternal) / total; return new Measurement<double>(ratio); }
    // Helper methods now correctly receive the 'params' array
    private Counter<T> CreateCounter<T>(string name, string? description = null, params KeyValuePair<string, object?>[] tags) where T : struct
    {
        // The 'tags' parameter here inside the method IS the params array
        var counter = _meter.CreateCounter<T>(name, description: description);
        _instruments.Add(counter);
        return counter;
    }

    private Histogram<T> CreateHistogram<T>(string name, string? unit = null, string? description = null, params KeyValuePair<string, object?>[] tags) where T : struct
    {
        // The 'tags' parameter here inside the method IS the params array
        var histogram = _meter.CreateHistogram<T>(name, unit, description, tags);
        _instruments.Add(histogram);
        return histogram;
    }
    
    public void Dispose() { ActivitySource.Dispose(); }
}