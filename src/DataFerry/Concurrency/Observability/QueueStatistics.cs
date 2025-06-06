// ===========================================================================
// <copyright file="QueueStatistics.cs" company="Level Up Software">
// Copyright (c) Level Up Software. All rights reserved.
// </copyright>
// ===========================================================================

using System.Collections.Concurrent;
using System.Diagnostics;

namespace lvlup.DataFerry.Concurrency.Observability;

/// <summary>
/// Provides detailed statistical analysis and tracking for queue operations.
/// </summary>
/// <remarks>
/// <para>
/// This class maintains comprehensive statistics about queue behavior, including
/// operation frequencies, timing percentiles, success rates, and trend analysis.
/// It uses thread-safe data structures to minimize contention while providing
/// accurate real-time insights.
/// </para>
/// <para>
/// Statistics are maintained using sliding time windows and percentile calculations
/// to provide both point-in-time and historical trend information.
/// </para>
/// </remarks>
public sealed class QueueStatistics
{
    #region Global Variables

    private readonly string _queueName;
    private readonly TimeSpan _windowDuration;
    private readonly object _statsLock = new();
    
    // Operation tracking
    private readonly ConcurrentDictionary<string, OperationStats> _operationStats;
    private readonly ConcurrentDictionary<string, SlidingWindow<TimedValue>> _operationWindows;
    
    // Global statistics
    private long _totalOperations;
    private long _totalSuccesses;
    private long _totalFailures;
    private readonly Stopwatch _uptime;
    
    // Percentile tracking
    private readonly PercentileTracker _globalLatencyTracker;
    private readonly ConcurrentDictionary<string, PercentileTracker> _operationLatencyTrackers;
    
    // Trend analysis
    private readonly TrendAnalyzer _throughputTrend;
    private readonly TrendAnalyzer _latencyTrend;
    private readonly TrendAnalyzer _successRateTrend;

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="QueueStatistics"/> class.
    /// </summary>
    /// <param name="queueName">The name of the queue for identification.</param>
    /// <param name="windowDuration">The duration of the sliding window for statistics.</param>
    /// <exception cref="ArgumentException">Thrown when queueName is null or empty.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when windowDuration is less than or equal to zero.</exception>
    public QueueStatistics(string queueName, TimeSpan windowDuration)
    {
        ArgumentException.ThrowIfNullOrEmpty(queueName, nameof(queueName));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(windowDuration, TimeSpan.Zero, nameof(windowDuration));

        _queueName = queueName;
        _windowDuration = windowDuration;
        _uptime = Stopwatch.StartNew();
        
        _operationStats = new ConcurrentDictionary<string, OperationStats>();
        _operationWindows = new ConcurrentDictionary<string, SlidingWindow<TimedValue>>();
        
        _globalLatencyTracker = new PercentileTracker();
        _operationLatencyTrackers = new ConcurrentDictionary<string, PercentileTracker>();
        
        _throughputTrend = new TrendAnalyzer("Throughput", 60); // 60 data points
        _latencyTrend = new TrendAnalyzer("Latency", 60);
        _successRateTrend = new TrendAnalyzer("SuccessRate", 60);
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Records the completion of an operation with its performance metrics.
    /// </summary>
    /// <param name="operation">The name of the operation.</param>
    /// <param name="success">Whether the operation succeeded.</param>
    /// <param name="latencyMs">The operation latency in milliseconds.</param>
    /// <param name="timestamp">Optional timestamp; defaults to current time.</param>
    public void RecordOperation(string operation, bool success, double latencyMs, DateTime? timestamp = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(operation, nameof(operation));
        ArgumentOutOfRangeException.ThrowIfNegative(latencyMs, nameof(latencyMs));

        var ts = timestamp ?? DateTime.UtcNow;
        var timedValue = new TimedValue(ts, latencyMs, success);
        
        // Update operation-specific stats
        var stats = _operationStats.GetOrAdd(operation, _ => new OperationStats());
        stats.RecordOperation(success, latencyMs);
        
        // Update sliding window
        var window = _operationWindows.GetOrAdd(operation, _ => new SlidingWindow<TimedValue>(_windowDuration));
        window.Add(timedValue);
        
        // Update percentile trackers
        _globalLatencyTracker.Add(latencyMs);
        var operationTracker = _operationLatencyTrackers.GetOrAdd(operation, _ => new PercentileTracker());
        operationTracker.Add(latencyMs);
        
        // Update global counters
        Interlocked.Increment(ref _totalOperations);
        if (success)
        {
            Interlocked.Increment(ref _totalSuccesses);
        }
        else
        {
            Interlocked.Increment(ref _totalFailures);
        }
        
        // Update trends periodically
        UpdateTrends();
    }

    /// <summary>
    /// Gets a comprehensive snapshot of current statistics.
    /// </summary>
    /// <returns>A statistics snapshot containing all current metrics.</returns>
    public StatisticsSnapshot GetSnapshot()
    {
        var snapshot = new StatisticsSnapshot
        {
            QueueName = _queueName,
            Timestamp = DateTime.UtcNow,
            UptimeSeconds = _uptime.Elapsed.TotalSeconds,
            TotalOperations = Interlocked.Read(ref _totalOperations),
            TotalSuccesses = Interlocked.Read(ref _totalSuccesses),
            TotalFailures = Interlocked.Read(ref _totalFailures)
        };
        
        // Calculate global metrics
        snapshot.OverallSuccessRate = snapshot.TotalOperations > 0 
            ? (double)snapshot.TotalSuccesses / snapshot.TotalOperations 
            : 1.0;
            
        snapshot.OperationsPerSecond = snapshot.UptimeSeconds > 0 
            ? snapshot.TotalOperations / snapshot.UptimeSeconds 
            : 0;
        
        // Get percentiles
        var globalPercentiles = _globalLatencyTracker.GetPercentiles();
        snapshot.LatencyP50 = globalPercentiles.P50;
        snapshot.LatencyP90 = globalPercentiles.P90;
        snapshot.LatencyP95 = globalPercentiles.P95;
        snapshot.LatencyP99 = globalPercentiles.P99;
        
        // Get operation breakdowns
        snapshot.OperationBreakdown = new Dictionary<string, OperationSnapshot>();
        foreach (var kvp in _operationStats)
        {
            var operationName = kvp.Key;
            var stats = kvp.Value;
            var percentiles = _operationLatencyTrackers[operationName].GetPercentiles();
            
            snapshot.OperationBreakdown[operationName] = new OperationSnapshot
            {
                Count = stats.Count,
                SuccessCount = stats.SuccessCount,
                FailureCount = stats.FailureCount,
                SuccessRate = stats.SuccessRate,
                AverageLatency = stats.AverageLatency,
                MinLatency = stats.MinLatency,
                MaxLatency = stats.MaxLatency,
                LatencyP50 = percentiles.P50,
                LatencyP90 = percentiles.P90,
                LatencyP95 = percentiles.P95,
                LatencyP99 = percentiles.P99
            };
        }
        
        // Get recent trends
        snapshot.ThroughputTrend = _throughputTrend.GetTrend();
        snapshot.LatencyTrend = _latencyTrend.GetTrend();
        snapshot.SuccessRateTrend = _successRateTrend.GetTrend();
        
        return snapshot;
    }

    /// <summary>
    /// Gets statistics for operations within a specific time window.
    /// </summary>
    /// <param name="operation">The operation name, or null for all operations.</param>
    /// <param name="window">The time window to analyze.</param>
    /// <returns>Statistics for the specified window.</returns>
    public WindowedStatistics GetWindowedStatistics(string? operation, TimeSpan window)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(window, TimeSpan.Zero, nameof(window));

        var cutoff = DateTime.UtcNow - window;
        var stats = new WindowedStatistics
        {
            Window = window,
            StartTime = cutoff,
            EndTime = DateTime.UtcNow
        };
        
        if (operation != null)
        {
            // Get stats for specific operation
            if (_operationWindows.TryGetValue(operation, out var operationWindow))
            {
                var values = operationWindow.GetValues(window);
                CalculateWindowedStats(values, stats);
            }
        }
        else
        {
            // Get stats for all operations
            var allValues = new List<TimedValue>();
            foreach (var w in _operationWindows.Values)
            {
                allValues.AddRange(w.GetValues(window));
            }
            CalculateWindowedStats(allValues, stats);
        }
        
        return stats;
    }

    /// <summary>
    /// Resets all statistics to their initial state.
    /// </summary>
    public void Reset()
    {
        lock (_statsLock)
        {
            _operationStats.Clear();
            _operationWindows.Clear();
            _operationLatencyTrackers.Clear();
            
            _totalOperations = 0;
            _totalSuccesses = 0;
            _totalFailures = 0;
            
            _globalLatencyTracker.Reset();
            _throughputTrend.Reset();
            _latencyTrend.Reset();
            _successRateTrend.Reset();
            
            _uptime.Restart();
        }
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Updates trend analyzers with current metrics.
    /// </summary>
    private void UpdateTrends()
    {
        // Update trends every second
        if (_uptime.ElapsedMilliseconds % 1000 < 50) // Within 50ms of a second boundary
        {
            var currentOps = Interlocked.Read(ref _totalOperations);
            var currentSuccesses = Interlocked.Read(ref _totalSuccesses);
            
            var throughput = currentOps / _uptime.Elapsed.TotalSeconds;
            var successRate = currentOps > 0 ? (double)currentSuccesses / currentOps : 1.0;
            var avgLatency = _globalLatencyTracker.GetPercentiles().P50;
            
            _throughputTrend.AddDataPoint(throughput);
            _successRateTrend.AddDataPoint(successRate);
            _latencyTrend.AddDataPoint(avgLatency);
        }
    }

    /// <summary>
    /// Calculates statistics for a windowed set of values.
    /// </summary>
    private void CalculateWindowedStats(List<TimedValue> values, WindowedStatistics stats)
    {
        if (values.Count == 0)
        {
            stats.Count = 0;
            stats.SuccessRate = 1.0;
            stats.AverageLatency = 0;
            return;
        }
        
        stats.Count = values.Count;
        stats.SuccessCount = values.Count(v => v.Success);
        stats.FailureCount = stats.Count - stats.SuccessCount;
        stats.SuccessRate = (double)stats.SuccessCount / stats.Count;
        
        var latencies = values.Select(v => v.Latency).ToList();
        stats.AverageLatency = latencies.Average();
        stats.MinLatency = latencies.Min();
        stats.MaxLatency = latencies.Max();
        
        // Calculate percentiles
        latencies.Sort();
        stats.LatencyP50 = GetPercentile(latencies, 0.50);
        stats.LatencyP90 = GetPercentile(latencies, 0.90);
        stats.LatencyP95 = GetPercentile(latencies, 0.95);
        stats.LatencyP99 = GetPercentile(latencies, 0.99);
    }

    /// <summary>
    /// Gets a specific percentile from a sorted list of values.
    /// </summary>
    private static double GetPercentile(List<double> sortedValues, double percentile)
    {
        if (sortedValues.Count == 0) return 0;
        
        var index = (int)Math.Ceiling(percentile * sortedValues.Count) - 1;
        index = Math.Max(0, Math.Min(index, sortedValues.Count - 1));
        return sortedValues[index];
    }

    #endregion

    #region Nested Types

    /// <summary>
    /// Tracks statistics for a specific operation type.
    /// </summary>
    private sealed class OperationStats
    {
        private long _count;
        private long _successCount;
        private long _failureCount;
        private double _totalLatency;
        private double _minLatency = double.MaxValue;
        private double _maxLatency = double.MinValue;
        private readonly object _lock = new();

        public long Count => Interlocked.Read(ref _count);
        public long SuccessCount => Interlocked.Read(ref _successCount);
        public long FailureCount => Interlocked.Read(ref _failureCount);
        
        public double SuccessRate => Count > 0 ? (double)SuccessCount / Count : 1.0;
        public double AverageLatency => Count > 0 ? _totalLatency / Count : 0;
        public double MinLatency => _minLatency == double.MaxValue ? 0 : _minLatency;
        public double MaxLatency => _maxLatency == double.MinValue ? 0 : _maxLatency;

        public void RecordOperation(bool success, double latencyMs)
        {
            Interlocked.Increment(ref _count);
            
            if (success)
            {
                Interlocked.Increment(ref _successCount);
            }
            else
            {
                Interlocked.Increment(ref _failureCount);
            }
            
            lock (_lock)
            {
                _totalLatency += latencyMs;
                _minLatency = Math.Min(_minLatency, latencyMs);
                _maxLatency = Math.Max(_maxLatency, latencyMs);
            }
        }
    }

    /// <summary>
    /// Represents a value with timestamp for windowed analysis.
    /// </summary>
    private readonly record struct TimedValue(DateTime Timestamp, double Latency, bool Success);

    /// <summary>
    /// Implements a sliding time window for values.
    /// </summary>
    private sealed class SlidingWindow<T> where T : struct
    {
        private readonly TimeSpan _windowDuration;
        private readonly Queue<(DateTime Timestamp, T Value)> _values = new();
        private readonly object _lock = new();

        public SlidingWindow(TimeSpan windowDuration)
        {
            _windowDuration = windowDuration;
        }

        public void Add(T value)
        {
            lock (_lock)
            {
                var now = DateTime.UtcNow;
                _values.Enqueue((now, value));
                
                // Remove old values
                var cutoff = now - _windowDuration;
                while (_values.Count > 0 && _values.Peek().Timestamp < cutoff)
                {
                    _values.Dequeue();
                }
            }
        }

        public List<T> GetValues(TimeSpan window)
        {
            lock (_lock)
            {
                var cutoff = DateTime.UtcNow - window;
                return _values
                    .Where(v => v.Timestamp >= cutoff)
                    .Select(v => v.Value)
                    .ToList();
            }
        }
    }

    /// <summary>
    /// Tracks percentiles for a distribution of values.
    /// </summary>
    private sealed class PercentileTracker
    {
        private readonly List<double> _values = new();
        private readonly object _lock = new();
        private bool _sorted = false;

        public void Add(double value)
        {
            lock (_lock)
            {
                _values.Add(value);
                _sorted = false;
            }
        }

        public (double P50, double P90, double P95, double P99) GetPercentiles()
        {
            lock (_lock)
            {
                if (_values.Count == 0)
                {
                    return (0, 0, 0, 0);
                }
                
                if (!_sorted)
                {
                    _values.Sort();
                    _sorted = true;
                }
                
                return (
                    GetPercentile(0.50),
                    GetPercentile(0.90),
                    GetPercentile(0.95),
                    GetPercentile(0.99)
                );
            }
        }

        public void Reset()
        {
            lock (_lock)
            {
                _values.Clear();
                _sorted = false;
            }
        }

        private double GetPercentile(double percentile)
        {
            var index = (int)Math.Ceiling(percentile * _values.Count) - 1;
            index = Math.Max(0, Math.Min(index, _values.Count - 1));
            return _values[index];
        }
    }

    /// <summary>
    /// Analyzes trends in metric values over time.
    /// </summary>
    private sealed class TrendAnalyzer
    {
        private readonly string _metricName;
        private readonly int _maxPoints;
        private readonly Queue<(DateTime Timestamp, double Value)> _dataPoints = new();
        private readonly object _lock = new();

        public TrendAnalyzer(string metricName, int maxPoints)
        {
            _metricName = metricName;
            _maxPoints = maxPoints;
        }

        public void AddDataPoint(double value)
        {
            lock (_lock)
            {
                _dataPoints.Enqueue((DateTime.UtcNow, value));
                
                while (_dataPoints.Count > _maxPoints)
                {
                    _dataPoints.Dequeue();
                }
            }
        }

        public TrendInfo GetTrend()
        {
            lock (_lock)
            {
                if (_dataPoints.Count < 2)
                {
                    return new TrendInfo { MetricName = _metricName, Trend = TrendDirection.Stable };
                }
                
                var values = _dataPoints.Select(p => p.Value).ToArray();
                var recentAvg = values.Skip(values.Length / 2).Average();
                var olderAvg = values.Take(values.Length / 2).Average();
                
                var changePercent = olderAvg != 0 ? (recentAvg - olderAvg) / olderAvg * 100 : 0;
                
                return new TrendInfo
                {
                    MetricName = _metricName,
                    CurrentValue = values.Last(),
                    AverageValue = values.Average(),
                    ChangePercent = changePercent,
                    Trend = changePercent switch
                    {
                        > 5 => TrendDirection.Increasing,
                        < -5 => TrendDirection.Decreasing,
                        _ => TrendDirection.Stable
                    }
                };
            }
        }

        public void Reset()
        {
            lock (_lock)
            {
                _dataPoints.Clear();
            }
        }
    }

    #endregion

    #region Public Types

    /// <summary>
    /// Represents a comprehensive snapshot of queue statistics.
    /// </summary>
    public sealed class StatisticsSnapshot
    {
        public string QueueName { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public double UptimeSeconds { get; set; }
        public long TotalOperations { get; set; }
        public long TotalSuccesses { get; set; }
        public long TotalFailures { get; set; }
        public double OverallSuccessRate { get; set; }
        public double OperationsPerSecond { get; set; }
        public double LatencyP50 { get; set; }
        public double LatencyP90 { get; set; }
        public double LatencyP95 { get; set; }
        public double LatencyP99 { get; set; }
        public Dictionary<string, OperationSnapshot> OperationBreakdown { get; set; } = new();
        public TrendInfo ThroughputTrend { get; set; } = new();
        public TrendInfo LatencyTrend { get; set; } = new();
        public TrendInfo SuccessRateTrend { get; set; } = new();
    }

    /// <summary>
    /// Represents statistics for a specific operation type.
    /// </summary>
    public sealed class OperationSnapshot
    {
        public long Count { get; set; }
        public long SuccessCount { get; set; }
        public long FailureCount { get; set; }
        public double SuccessRate { get; set; }
        public double AverageLatency { get; set; }
        public double MinLatency { get; set; }
        public double MaxLatency { get; set; }
        public double LatencyP50 { get; set; }
        public double LatencyP90 { get; set; }
        public double LatencyP95 { get; set; }
        public double LatencyP99 { get; set; }
    }

    /// <summary>
    /// Represents statistics for a specific time window.
    /// </summary>
    public sealed class WindowedStatistics
    {
        public TimeSpan Window { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public int Count { get; set; }
        public int SuccessCount { get; set; }
        public int FailureCount { get; set; }
        public double SuccessRate { get; set; }
        public double AverageLatency { get; set; }
        public double MinLatency { get; set; }
        public double MaxLatency { get; set; }
        public double LatencyP50 { get; set; }
        public double LatencyP90 { get; set; }
        public double LatencyP95 { get; set; }
        public double LatencyP99 { get; set; }
    }

    /// <summary>
    /// Represents trend information for a metric.
    /// </summary>
    public sealed class TrendInfo
    {
        public string MetricName { get; set; } = string.Empty;
        public double CurrentValue { get; set; }
        public double AverageValue { get; set; }
        public double ChangePercent { get; set; }
        public TrendDirection Trend { get; set; }
    }

    /// <summary>
    /// Indicates the direction of a trend.
    /// </summary>
    public enum TrendDirection
    {
        Increasing,
        Stable,
        Decreasing
    }

    #endregion
}