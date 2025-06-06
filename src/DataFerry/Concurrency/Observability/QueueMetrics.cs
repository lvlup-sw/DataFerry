// ===========================================================================
// <copyright file="QueueMetrics.cs" company="Level Up Software">
// Copyright (c) Level Up Software. All rights reserved.
// </copyright>
// ===========================================================================

using System.Diagnostics.Metrics;

using Microsoft.Extensions.Logging;

namespace lvlup.DataFerry.Concurrency.Observability;

/// <summary>
/// Provides a comprehensive set of metrics for monitoring concurrent priority queue operations and performance.
/// </summary>
/// <remarks>
/// <para>
/// This class encapsulates all metric instruments used to monitor the queue's behavior,
/// including operation counts, failure rates, latency distributions, and resource utilization.
/// The metrics follow OpenTelemetry semantic conventions for compatibility with standard monitoring tools.
/// </para>
/// <para>
/// Metrics are organized into categories:
/// - Operation counters: Track the frequency of different operations
/// - Failure counters: Monitor error rates and contention
/// - Histograms: Measure latency and size distributions
/// - Gauges: Provide real-time queue state information
/// </para>
/// </remarks>
public sealed class QueueMetrics : IDisposable
{
    #region Global Variables

    private readonly Meter _meter;
    private readonly string _queueName;
    private readonly ILogger? _logger;
    
    // Performance counters
    public Counter<long> AddOperations { get; private set; } = null!;
    public Counter<long> DeleteOperations { get; private set; } = null!;
    public Counter<long> DeleteMinOperations { get; private set; } = null!;
    public Counter<long> PeekOperations { get; private set; } = null!;
    public Counter<long> UpdateOperations { get; private set; } = null!;
    public Counter<long> ClearOperations { get; private set; } = null!;
    public Counter<long> EnumerationOperations { get; private set; } = null!;
    
    // Failure counters
    public Counter<long> OperationFailures { get; private set; } = null!;
    public Counter<long> ContentionEvents { get; private set; } = null!;
    public Counter<long> TimeoutEvents { get; private set; } = null!;
    public Counter<long> CapacityExceeded { get; private set; } = null!;
    
    // Histograms
    public Histogram<double> OperationLatency { get; private set; } = null!;
    public Histogram<int> SprayJumpLength { get; private set; } = null!;
    public Histogram<int> NodeHeight { get; private set; } = null!;
    public Histogram<double> LockWaitTime { get; private set; } = null!;
    public Histogram<int> RetryCount { get; private set; } = null!;
    
    // Gauges
    public ObservableGauge<double> QueueFullnessRatio { get; private set; } = null!;
    public ObservableGauge<long> PendingDeletions { get; private set; } = null!;
    public ObservableGauge<int> ActiveOperations { get; private set; } = null!;
    public ObservableGauge<double> ThroughputRate { get; private set; } = null!;
    
    // Internal tracking
    private long _lastOperationCount;
    private DateTime _lastThroughputCheck;
    private readonly Func<int> _getCurrentCount;
    private readonly int _maxSize;
    private bool _disposed;

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="QueueMetrics"/> class.
    /// </summary>
    /// <param name="meterFactory">The meter factory for creating metrics.</param>
    /// <param name="queueName">The name of the queue for metric identification.</param>
    /// <param name="getCurrentCount">Function to retrieve the current queue count.</param>
    /// <param name="maxSize">The maximum size of the queue.</param>
    /// <param name="logger">Optional logger for metric-related events.</param>
    /// <exception cref="ArgumentNullException">Thrown when required parameters are null.</exception>
    /// <exception cref="ArgumentException">Thrown when queueName is null or empty.</exception>
    /// <remarks>
    /// All metrics are prefixed with "dataferry.queue." and tagged with the queue name
    /// to enable filtering and aggregation in monitoring systems.
    /// </remarks>
    public QueueMetrics(
        IMeterFactory meterFactory,
        string queueName,
        Func<int> getCurrentCount,
        int maxSize,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(meterFactory, nameof(meterFactory));
        ArgumentException.ThrowIfNullOrEmpty(queueName, nameof(queueName));
        ArgumentNullException.ThrowIfNull(getCurrentCount, nameof(getCurrentCount));
        ArgumentOutOfRangeException.ThrowIfLessThan(maxSize, 1, nameof(maxSize));

        _meter = meterFactory.Create($"DataFerry.Queue.{queueName}");
        _queueName = queueName;
        _getCurrentCount = getCurrentCount;
        _maxSize = maxSize;
        _logger = logger;
        _lastThroughputCheck = DateTime.UtcNow;
        
        InitializeCounters();
        InitializeHistograms();
        InitializeGauges();
        
        _logger?.LogDebug("Queue metrics initialized for queue: {QueueName}", queueName);
    }

    #endregion

    #region Initialization Methods

    /// <summary>
    /// Initializes all counter metrics used for tracking operation counts and failures.
    /// </summary>
    private void InitializeCounters()
    {
        // Operation counters
        AddOperations = _meter.CreateCounter<long>(
            "dataferry.queue.add.count",
            unit: "{operation}",
            description: "Number of add operations attempted");
            
        DeleteOperations = _meter.CreateCounter<long>(
            "dataferry.queue.delete.count",
            unit: "{operation}",
            description: "Number of delete operations attempted");
            
        DeleteMinOperations = _meter.CreateCounter<long>(
            "dataferry.queue.delete_min.count",
            unit: "{operation}",
            description: "Number of delete-min operations attempted");
            
        PeekOperations = _meter.CreateCounter<long>(
            "dataferry.queue.peek.count",
            unit: "{operation}",
            description: "Number of peek operations attempted");
            
        UpdateOperations = _meter.CreateCounter<long>(
            "dataferry.queue.update.count",
            unit: "{operation}",
            description: "Number of update operations attempted");
            
        ClearOperations = _meter.CreateCounter<long>(
            "dataferry.queue.clear.count",
            unit: "{operation}",
            description: "Number of clear operations performed");
            
        EnumerationOperations = _meter.CreateCounter<long>(
            "dataferry.queue.enumeration.count",
            unit: "{operation}",
            description: "Number of enumeration operations started");
        
        // Failure and contention counters
        OperationFailures = _meter.CreateCounter<long>(
            "dataferry.queue.failures.count",
            unit: "{failure}",
            description: "Number of operation failures by type");
            
        ContentionEvents = _meter.CreateCounter<long>(
            "dataferry.queue.contention.count",
            unit: "{event}",
            description: "Number of contention events encountered");
            
        TimeoutEvents = _meter.CreateCounter<long>(
            "dataferry.queue.timeout.count",
            unit: "{timeout}",
            description: "Number of operation timeouts");
            
        CapacityExceeded = _meter.CreateCounter<long>(
            "dataferry.queue.capacity_exceeded.count",
            unit: "{rejection}",
            description: "Number of operations rejected due to capacity limits");
    }

    /// <summary>
    /// Initializes all histogram metrics used for measuring distributions.
    /// </summary>
    private void InitializeHistograms()
    {
        OperationLatency = _meter.CreateHistogram<double>(
            "dataferry.queue.operation.latency",
            unit: "ms",
            description: "Latency distribution of queue operations");
            
        SprayJumpLength = _meter.CreateHistogram<int>(
            "dataferry.queue.spray.jump_length",
            unit: "{nodes}",
            description: "Distribution of spray operation jump lengths");
            
        NodeHeight = _meter.CreateHistogram<int>(
            "dataferry.queue.node.height",
            unit: "{levels}",
            description: "Distribution of skip list node heights");
            
        LockWaitTime = _meter.CreateHistogram<double>(
            "dataferry.queue.lock.wait_time",
            unit: "ms",
            description: "Time spent waiting for locks");
            
        RetryCount = _meter.CreateHistogram<int>(
            "dataferry.queue.retry.count",
            unit: "{retries}",
            description: "Number of retries per operation");
    }

    /// <summary>
    /// Initializes all gauge metrics used for real-time state monitoring.
    /// </summary>
    private void InitializeGauges()
    {
        QueueFullnessRatio = _meter.CreateObservableGauge<double>(
            "dataferry.queue.fullness.ratio",
            () => CalculateFullnessRatio(),
            unit: "1",
            description: "Queue utilization as a ratio (0-1)");
            
        PendingDeletions = _meter.CreateObservableGauge<long>(
            "dataferry.queue.pending_deletions.count",
            () => ObservePendingDeletions(),
            unit: "{nodes}",
            description: "Number of nodes pending physical deletion");
            
        ActiveOperations = _meter.CreateObservableGauge<int>(
            "dataferry.queue.active_operations.count",
            () => ObserveActiveOperations(),
            unit: "{operations}",
            description: "Number of currently active operations");
            
        ThroughputRate = _meter.CreateObservableGauge<double>(
            "dataferry.queue.throughput.rate",
            () => CalculateThroughputRate(),
            unit: "{operations}/s",
            description: "Operations per second throughput rate");
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Records a successful operation with its latency.
    /// </summary>
    /// <param name="operationType">The type of operation performed.</param>
    /// <param name="latencyMs">The operation latency in milliseconds.</param>
    /// <param name="additionalTags">Optional additional tags for the metric.</param>
    public void RecordSuccess(string operationType, double latencyMs, params KeyValuePair<string, object?>[] additionalTags)
    {
        var tags = BuildTags(operationType, true, additionalTags);
        
        IncrementOperationCounter(operationType, tags);
        OperationLatency.Record(latencyMs, tags);
        
        _logger?.LogTrace("Operation {OperationType} succeeded in {Latency}ms", operationType, latencyMs);
    }

    /// <summary>
    /// Records a failed operation with its latency and failure reason.
    /// </summary>
    /// <param name="operationType">The type of operation that failed.</param>
    /// <param name="latencyMs">The operation latency in milliseconds.</param>
    /// <param name="failureReason">The reason for failure.</param>
    /// <param name="additionalTags">Optional additional tags for the metric.</param>
    public void RecordFailure(string operationType, double latencyMs, string failureReason, params KeyValuePair<string, object?>[] additionalTags)
    {
        var tags = BuildTags(operationType, false, additionalTags);
        var failureTags = tags.Concat(new[] { new KeyValuePair<string, object?>("reason", failureReason) }).ToArray();
        
        IncrementOperationCounter(operationType, tags);
        OperationLatency.Record(latencyMs, tags);
        OperationFailures.Add(1, failureTags);
        
        _logger?.LogDebug("Operation {OperationType} failed after {Latency}ms: {Reason}", 
            operationType, latencyMs, failureReason);
    }

    /// <summary>
    /// Records a contention event during an operation.
    /// </summary>
    /// <param name="operationType">The operation that experienced contention.</param>
    /// <param name="contentionType">The type of contention encountered.</param>
    /// <param name="waitTimeMs">The time spent waiting due to contention.</param>
    public void RecordContention(string operationType, string contentionType, double waitTimeMs)
    {
        var tags = new[]
        {
            new KeyValuePair<string, object?>("queue", _queueName),
            new KeyValuePair<string, object?>("operation", operationType),
            new KeyValuePair<string, object?>("contention_type", contentionType)
        };
        
        ContentionEvents.Add(1, tags);
        LockWaitTime.Record(waitTimeMs, tags);
        
        _logger?.LogTrace("Contention in {Operation}: {ContentionType} for {WaitTime}ms", 
            operationType, contentionType, waitTimeMs);
    }

    /// <summary>
    /// Records a spray operation's jump length for analysis.
    /// </summary>
    /// <param name="jumpLength">The number of nodes jumped in the spray operation.</param>
    public void RecordSprayJump(int jumpLength)
    {
        SprayJumpLength.Record(jumpLength, new KeyValuePair<string, object?>("queue", _queueName));
    }

    /// <summary>
    /// Records the height of a newly created skip list node.
    /// </summary>
    /// <param name="height">The height of the node (number of levels).</param>
    public void RecordNodeHeight(int height)
    {
        NodeHeight.Record(height, new KeyValuePair<string, object?>("queue", _queueName));
    }

    /// <summary>
    /// Records the number of retries needed for an operation.
    /// </summary>
    /// <param name="operationType">The type of operation.</param>
    /// <param name="retries">The number of retries performed.</param>
    public void RecordRetries(string operationType, int retries)
    {
        if (retries > 0)
        {
            RetryCount.Record(retries, 
                new KeyValuePair<string, object?>("queue", _queueName),
                new KeyValuePair<string, object?>("operation", operationType));
        }
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Increments the appropriate operation counter based on operation type.
    /// </summary>
    private void IncrementOperationCounter(string operationType, KeyValuePair<string, object?>[] tags)
    {
        switch (operationType.ToLowerInvariant())
        {
            case "tryadd":
            case "add":
                AddOperations.Add(1, tags);
                break;
            case "trydelete":
            case "delete":
                DeleteOperations.Add(1, tags);
                break;
            case "trydeletemin":
            case "deletemin":
                DeleteMinOperations.Add(1, tags);
                break;
            case "trypeek":
            case "peek":
                PeekOperations.Add(1, tags);
                break;
            case "update":
                UpdateOperations.Add(1, tags);
                break;
            case "clear":
                ClearOperations.Add(1, tags);
                break;
            case "getenumerator":
            case "enumeration":
                EnumerationOperations.Add(1, tags);
                break;
        }
        
        Interlocked.Increment(ref _lastOperationCount);
    }

    /// <summary>
    /// Builds a standard set of tags for metrics.
    /// </summary>
    private KeyValuePair<string, object?>[] BuildTags(string operationType, bool success, params KeyValuePair<string, object?>[] additionalTags)
    {
        var baseTags = new[]
        {
            new KeyValuePair<string, object?>("queue", _queueName),
            new KeyValuePair<string, object?>("operation", operationType),
            new KeyValuePair<string, object?>("success", success)
        };
        
        return additionalTags.Length > 0 ? baseTags.Concat(additionalTags).ToArray() : baseTags;
    }

    /// <summary>
    /// Calculates the current queue fullness ratio.
    /// </summary>
    private double CalculateFullnessRatio()
    {
        var currentCount = _getCurrentCount();
        return (double)currentCount / _maxSize;
    }

    /// <summary>
    /// Observes the number of pending deletions (placeholder implementation).
    /// </summary>
    private long ObservePendingDeletions()
    {
        // This would be implemented by the queue to track nodes awaiting physical deletion
        return 0;
    }

    /// <summary>
    /// Observes the number of active operations (placeholder implementation).
    /// </summary>
    private int ObserveActiveOperations()
    {
        // This would be implemented by tracking operation start/end
        return 0;
    }

    /// <summary>
    /// Calculates the current throughput rate in operations per second.
    /// </summary>
    private double CalculateThroughputRate()
    {
        var now = DateTime.UtcNow;
        var elapsed = (now - _lastThroughputCheck).TotalSeconds;
        
        if (elapsed < 1.0)
        {
            return 0; // Not enough time has passed
        }
        
        var currentCount = Interlocked.Read(ref _lastOperationCount);
        var rate = currentCount / elapsed;
        
        _lastThroughputCheck = now;
        Interlocked.Exchange(ref _lastOperationCount, 0);
        
        return rate;
    }

    #endregion

    #region IDisposable Implementation

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _meter.Dispose();
        _disposed = true;
        
        _logger?.LogDebug("Queue metrics disposed for queue: {QueueName}", _queueName);
    }

    #endregion
}