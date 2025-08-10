using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;

namespace lvlup.DataFerry.Concurrency;

/// <summary>
/// Helper class to encapsulate observability concerns (logging, metrics, tracing)
/// for the ConcurrentPriorityQueue.
/// </summary>
/// <typeparam name="TPriority">The type used for priority values.</typeparam>
/// <typeparam name="TElement">The type of the elements stored in the queue.</typeparam>
internal class ConcurrentPriorityQueueObservabilityHelper<TPriority, TElement>
{
    private readonly ILogger _logger;
    private readonly Meter _meter;
    private readonly Func<int> _getCountFunc;
    private readonly ActivitySource _activitySource = new("lvlup.DataFerry.Concurrency.ConcurrentPriorityQueue");

    // Metrics Instruments (examples)
    private readonly Counter<long> _addsCounter;
    private readonly Counter<long> _deletesCounter;
    private readonly Counter<long> _updatesCounter;
    private readonly Counter<long> _failuresCounter;
    private readonly Histogram<double> _addDurationHistogram;
    private readonly Histogram<double> _deleteDurationHistogram;
    private readonly Counter<long> _physicalDeleteTasksScheduled;
    private readonly Counter<long> _physicalDeleteTasksDropped;

    public ConcurrentPriorityQueueObservabilityHelper(
        ILoggerFactory loggerFactory,
        IMeterFactory meterFactory,
        int configuredMaxSize,
        Func<int> getCountFunc)
    {
        ArgumentNullException.ThrowIfNull(loggerFactory);
        ArgumentNullException.ThrowIfNull(meterFactory);
        ArgumentNullException.ThrowIfNull(getCountFunc);

        _logger = loggerFactory.CreateLogger<ConcurrentPriorityQueue<TPriority, TElement>>();
        _meter = meterFactory.Create("lvlup.DataFerry.Concurrency.ConcurrentPriorityQueue");
        _getCountFunc = getCountFunc;

        // Initialize Instruments (examples)
        _addsCounter = _meter.CreateCounter<long>("priorityqueue.adds.count", description: "Number of successful add operations.");
        _deletesCounter = _meter.CreateCounter<long>("priorityqueue.deletes.count", description: "Number of successful delete operations.");
        _updatesCounter = _meter.CreateCounter<long>("priorityqueue.updates.count", description: "Number of successful update operations.");
        _failuresCounter = _meter.CreateCounter<long>("priorityqueue.operations.failed.count", unit: "{operation}", description: "Number of failed operations (add, delete, update)."); // Use tag for type
        _addDurationHistogram = _meter.CreateHistogram<double>("priorityqueue.add.duration", unit: "ms", description: "Duration of TryAdd operations.");
        _deleteDurationHistogram = _meter.CreateHistogram<double>("priorityqueue.delete.duration", unit: "ms", description: "Duration of delete operations (TryRemove, TryDeleteMin, TryDeleteAbsoluteMin)."); // Tag for type
        _physicalDeleteTasksScheduled = _meter.CreateCounter<long>("priorityqueue.physical_delete.scheduled.count");
        _physicalDeleteTasksDropped = _meter.CreateCounter<long>("priorityqueue.physical_delete.dropped.count");

        // Observable Gauge for queue count relative to max size
        _meter.CreateObservableGauge<double>(
            "priorityqueue.fullness.ratio",
            ObserveQueueFullness,
            description: "Ratio of current count to max size.");

        // UpDownCounter for current queue size
        _ = _meter.CreateObservableUpDownCounter<int>(
            "priorityqueue.current.count",
            () => new Measurement<int>(_getCountFunc()),
            description: "Current number of items in the queue.");

        double ObserveQueueFullness()
        {
            var currentCount = _getCountFunc();
            return configuredMaxSize == int.MaxValue
                ? 0.0 // For unbounded queues, return 0
                : (double)currentCount / configuredMaxSize;
        }
    }

    public void LogQueueInitialized(int maxSize, int levels, double probability, int offsetK, int offsetM)
    {
        _logger.LogInformation("ConcurrentPriorityQueue initialized. MaxSize: {MaxSize}, Levels: {Levels}, Probability: {Probability}, OffsetK: {OffsetK}, OffsetM: {OffsetM}",
            maxSize, levels, probability, offsetK, offsetM);
    }

    public Activity? StartActivity(string name) => _activitySource.StartActivity(name);

    public void LogAddAttempt(TPriority priority) => _logger.LogDebug("TryAdd called for Priority: {Priority}", priority);

    public void RecordAddSuccess(TPriority priority, int currentCount, double durationMs)
    {
        _addsCounter.Add(1);
        _addDurationHistogram.Record(durationMs);
        _logger.LogInformation("Node added successfully for Priority: {Priority}. New Count: {Count}. Duration: {DurationMs}ms",
            priority, currentCount, durationMs);
    }

    public void RecordAddFailure(TPriority priority, double durationMs)
    {
        _failuresCounter.Add(1, new KeyValuePair<string, object?>("operation", "add"));
        _addDurationHistogram.Record(durationMs);
        _logger.LogWarning("TryAdd failed for Priority: {Priority}. Duration: {DurationMs}ms", priority, durationMs);
    }

    public void LogMaxSizeExceeded(int currentCount) =>
        _logger.LogInformation("MaxSize exceeded during Add. Attempting TryDeleteMin. Current Count: {Count}", currentCount);

    public void RecordDeleteSuccess(string deleteType, int newCount, double durationMs) // deleteType: "min", "absolute_min", "specific"
    {
        _deletesCounter.Add(1, new KeyValuePair<string, object?>("delete_type", deleteType));
        _deleteDurationHistogram.Record(durationMs, new KeyValuePair<string, object?>("delete_type", deleteType));
        _logger.LogInformation("Node deleted successfully via {DeleteType}. New Count: {Count}. Duration: {DurationMs}ms",
            deleteType, newCount, durationMs);
    }

    public void RecordDeleteFailure(string deleteType, double durationMs)
    {
        _failuresCounter.Add(1, new KeyValuePair<string, object?>("operation", "delete"), new KeyValuePair<string, object?>("delete_type", deleteType));
        _deleteDurationHistogram.Record(durationMs, new KeyValuePair<string, object?>("delete_type", deleteType)); // Record duration even on failure
        _logger.LogWarning("Delete operation ({DeleteType}) failed. Duration: {DurationMs}ms", deleteType, durationMs);
    }

    public void RecordUpdateSuccess(int currentCount, double durationMs)
    {
        _updatesCounter.Add(1);
        _logger.LogInformation("Node updated successfully. Current Count: {Count}. Duration: {DurationMs}ms",
            currentCount, durationMs);
    }

    public void RecordUpdateFailure(double durationMs)
    {
        _failuresCounter.Add(1, new KeyValuePair<string, object?>("operation", "update"));
        _logger.LogWarning("Update failed. Duration: {DurationMs}ms", durationMs);
    }

    public void RecordPeekSuccess(double durationMs)
    {
        _logger.LogDebug("Peek operation succeeded. Duration: {DurationMs}ms", durationMs);
    }

    public void RecordPeekFailure(double durationMs)
    {
        _logger.LogDebug("Peek operation failed (priority not found). Duration: {DurationMs}ms", durationMs);
    }

    public void LogPhysicalRemovalScheduled(TPriority priority, long sequenceNumber) =>
        _logger.LogDebug("Scheduling physical node removal for Priority: {Priority}, Sequence: {SequenceNumber}", priority, sequenceNumber);

    public void RecordPhysicalRemovalScheduled() => _physicalDeleteTasksScheduled.Add(1);

    public void RecordPhysicalRemovalDropped(TPriority priority, long sequenceNumber)
    {
        _physicalDeleteTasksDropped.Add(1);
        _logger.LogWarning("Physical node removal task dropped for node (Priority: {Priority}, Sequence: {SequenceNumber}) because the background task orchestrator queue is full.",
            priority, sequenceNumber);
    }

    public void LogPhysicalRemovalCancelled(TPriority priority, long sequenceNumber) =>
        _logger.LogDebug("Physical node removal cancelled for Priority: {Priority}, Sequence: {SequenceNumber}", priority, sequenceNumber);

    /// <summary>
    /// Starts observability tracking for a queue operation.
    /// Begins an Activity, logs the attempt, starts a Stopwatch, and sets initial tags.
    /// </summary>
    /// <param name="operationName">The simple name of the operation (e.g., "TryAdd").</param>
    /// <param name="priority">Optional priority to tag the activity.</param>
    /// <param name="tags">Optional additional tags for the activity.</param>
    /// <returns>A tuple containing the started Activity (nullable) and Stopwatch.</returns>
    public (Activity? Activity, Stopwatch Stopwatch) StartOperation(
        string operationName,
        TPriority? priority = default,
        IEnumerable<KeyValuePair<string, object?>>? tags = null)
    {
        string fullActivityName = $"{_meter.Name}.{operationName}";
        var activity = _activitySource.StartActivity(fullActivityName);
        var stopwatch = Stopwatch.StartNew();

        _logger.LogDebug("{OperationName} called.", operationName);

        if (activity is null)
        {
            return (activity, stopwatch);
        }

        // Add priority tag if available and non-null
        // Be cautious about TPriority.ToString() performance or complexity
        if (priority is not null)
        {
            activity.SetTag("priority", priority.ToString());
        }

        // Add any other provided tags
        if (tags is null)
        {
            return (activity, stopwatch);
        }

        foreach (var tag in tags)
        {
            activity.SetTag(tag.Key, tag.Value);
        }

        return (activity, stopwatch);
    }
}