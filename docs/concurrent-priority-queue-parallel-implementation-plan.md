# ConcurrentPriorityQueue Parallel Implementation Plan

## Overview

This document outlines a parallel implementation strategy for enhancing the ConcurrentPriorityQueue, designed for 4 independent agents to work simultaneously without conflicts. Each agent owns specific components and communicates through well-defined interfaces.

## Agent Assignments

### Agent 1: Core Infrastructure & Memory Management
**Owner**: Physical deletion reliability, memory management, and Clear() implementation

### Agent 2: Observability & Diagnostics
**Owner**: Observability infrastructure, metrics, logging, and tracing

### Agent 3: Query Operations & Performance
**Owner**: TryPeek operations, enumerators, and read performance optimizations

### Agent 4: Advanced Features & Batch Operations
**Owner**: Batch operations, priority updates, and adaptive algorithms

## Coordination Interfaces

### Shared Contracts
Create these interfaces in `src/DataFerry/Concurrency/Contracts/` first:

```csharp
// INodePool.cs - Shared by Agent 1 & 4
public interface INodePool<TNode>
{
    TNode Rent();
    bool Return(TNode node);
    void Clear();
}

// IQueueObservability.cs - Shared by all agents
public interface IQueueObservability
{
    void RecordOperation(string operation, bool success, double durationMs, 
                        Dictionary<string, object>? tags = null);
    IActivity? StartActivity(string operationName);
    void LogEvent(LogLevel level, string message, params object[] args);
}

// IQueueStatistics.cs - Shared by Agent 2 & 3
public interface IQueueStatistics
{
    int CurrentCount { get; }
    long TotalOperations { get; }
    double AverageOperationTime { get; }
    Dictionary<string, long> OperationCounts { get; }
}
```

## Agent 1: Core Infrastructure & Memory Management

### Objectives
1. Fix physical deletion reliability
2. Implement Clear() method
3. Create node pooling infrastructure

### File Structure
```
src/DataFerry/Concurrency/
├── Internal/
│   ├── NodePool.cs (new)
│   ├── FallbackDeletionProcessor.cs (new)
│   └── MemoryManager.cs (new)
└── ConcurrentPriorityQueue.cs (modify)
```

### Implementation Tasks

#### 1.1 Create FallbackDeletionProcessor.cs
```csharp
namespace lvlup.DataFerry.Concurrency.Internal;

internal sealed class FallbackDeletionProcessor<TPriority, TElement> : IDisposable
{
    private readonly Channel<SkipListNode> _deletionChannel;
    private readonly Task _processingTask;
    private readonly CancellationTokenSource _cancellationTokenSource;
    private readonly IQueueObservability? _observability;

    public FallbackDeletionProcessor(IQueueObservability? observability)
    {
        _observability = observability;
        _cancellationTokenSource = new CancellationTokenSource();
        _deletionChannel = Channel.CreateUnbounded<SkipListNode>(
            new UnboundedChannelOptions 
            { 
                SingleReader = true,
                SingleWriter = false 
            });
        _processingTask = Task.Run(ProcessDeletionsAsync);
    }

    public bool TrySchedule(SkipListNode node)
    {
        return _deletionChannel.Writer.TryWrite(node);
    }

    private async Task ProcessDeletionsAsync()
    {
        await foreach (var node in _deletionChannel.Reader.ReadAllAsync(_cancellationTokenSource.Token))
        {
            try
            {
                await RemoveNodePhysically(node);
                _observability?.RecordOperation("FallbackDeletion", true, 0);
            }
            catch (Exception ex)
            {
                _observability?.LogEvent(LogLevel.Error, 
                    "Fallback deletion failed for node with priority {Priority}: {Error}", 
                    node.Priority, ex.Message);
            }
        }
    }

    // Implementation of RemoveNodePhysically...

    public void Dispose()
    {
        _cancellationTokenSource.Cancel();
        _deletionChannel.Writer.TryComplete();
        _processingTask.Wait(TimeSpan.FromSeconds(5));
        _cancellationTokenSource.Dispose();
    }
}
```

#### 1.2 Create NodePool.cs
```csharp
namespace lvlup.DataFerry.Concurrency.Internal;

internal sealed class NodePool<TPriority, TElement> : INodePool<SkipListNode>
{
    private readonly ObjectPool<SkipListNode> _pool;
    private readonly IComparer<TPriority> _comparer;
    private readonly int _maxHeight;

    public NodePool(int maxHeight, IComparer<TPriority> comparer, int maxPoolSize)
    {
        _maxHeight = maxHeight;
        _comparer = comparer;
        _pool = new DefaultObjectPool<SkipListNode>(
            new NodePooledObjectPolicy(maxHeight, comparer), 
            maxPoolSize);
    }

    public SkipListNode Rent() => _pool.Get();

    public bool Return(SkipListNode node)
    {
        if (!ValidateNode(node)) return false;
        ResetNode(node);
        return _pool.Return(node);
    }

    public void Clear()
    {
        // Force pool cleanup
    }

    private bool ValidateNode(SkipListNode node) => 
        node.Type == SkipListNode.NodeType.Data && !node.IsDeleted;

    private void ResetNode(SkipListNode node)
    {
        node.IsInserted = false;
        node.IsDeleted = false;
        node.Priority = default!;
        node.Element = default!;
        for (int i = 0; i <= node.TopLevel; i++)
        {
            node.SetNextNode(i, null!);
        }
    }
}
```

#### 1.3 Modify ConcurrentPriorityQueue.cs
Add these fields and modify methods:
```csharp
// New fields
private readonly FallbackDeletionProcessor<TPriority, TElement> _fallbackProcessor;
private readonly INodePool<SkipListNode>? _nodePool;
private volatile int _clearVersion = 0;

// In constructor
_fallbackProcessor = new FallbackDeletionProcessor<TPriority, TElement>(_observability);
_nodePool = enableNodePooling 
    ? new NodePool<TPriority, TElement>(_topLevel, _comparer, Environment.ProcessorCount * 4)
    : null;

// Clear implementation
public void Clear()
{
    var newVersion = Interlocked.Increment(ref _clearVersion);
    var oldCount = Interlocked.Exchange(ref _count, 0);
    
    // Create new sentinel nodes
    var newHead = new SkipListNode(SkipListNode.NodeType.Head, _topLevel) { IsInserted = true };
    var newTail = new SkipListNode(SkipListNode.NodeType.Tail, _topLevel) { IsInserted = true };
    
    // Link new structure
    for (int level = BottomLevel; level <= _topLevel; level++)
    {
        newHead.SetNextNode(level, newTail);
    }
    
    // Atomic swap
    var oldHead = Interlocked.Exchange(ref _head, newHead);
    var oldTail = Interlocked.Exchange(ref _tail, newTail);
    
    // Clear node pool
    _nodePool?.Clear();
    
    // Schedule cleanup
    if (oldCount > 0)
    {
        _taskOrchestrator.Run(() => CleanupOldStructure(oldHead, oldTail, newVersion));
    }
    
    _observability?.RecordOperation("Clear", true, 0, 
        new Dictionary<string, object> { ["OldCount"] = oldCount });
}
```

### Unit Tests Required
- TestClear_EmptyQueue_Succeeds
- TestClear_ConcurrentOperations_IsSafe  
- TestFallbackDeletion_WhenPrimaryFails_Succeeds
- TestNodePool_RentReturn_MaintainsIntegrity

---

## Agent 2: Observability & Diagnostics

### Objectives
1. Implement full observability infrastructure
2. Add metrics, logging, and tracing
3. Create diagnostics API

### File Structure
```
src/DataFerry/Concurrency/
├── Observability/
│   ├── QueueObservability.cs (new)
│   ├── QueueMetrics.cs (new)
│   ├── QueueStatistics.cs (new)
│   └── ActivityExtensions.cs (new)
└── ConcurrentPriorityQueue.cs (modify)
```

### Implementation Tasks

#### 2.1 Create QueueObservability.cs
```csharp
namespace lvlup.DataFerry.Concurrency.Observability;

internal sealed class QueueObservability : IQueueObservability, IQueueStatistics
{
    private readonly ILogger _logger;
    private readonly Meter _meter;
    private readonly ActivitySource _activitySource;
    
    // Metrics
    private readonly Counter<long> _operationCounter;
    private readonly Histogram<double> _operationDuration;
    private readonly UpDownCounter<long> _queueSize;
    
    // Statistics
    private long _totalOperations;
    private readonly ConcurrentDictionary<string, long> _operationCounts;
    private readonly RollingAverage _averageOperationTime;

    public QueueObservability(
        ILoggerFactory loggerFactory, 
        IMeterFactory meterFactory,
        string queueName,
        int maxSize)
    {
        _logger = loggerFactory.CreateLogger($"ConcurrentPriorityQueue.{queueName}");
        _meter = meterFactory.Create($"DataFerry.ConcurrentPriorityQueue.{queueName}");
        _activitySource = new ActivitySource($"DataFerry.ConcurrentPriorityQueue.{queueName}");
        
        InitializeMetrics();
        InitializeStatistics();
    }

    private void InitializeMetrics()
    {
        _operationCounter = _meter.CreateCounter<long>(
            "queue.operations.count",
            unit: "{operation}",
            description: "Total number of queue operations");
            
        _operationDuration = _meter.CreateHistogram<double>(
            "queue.operation.duration",
            unit: "ms",
            description: "Duration of queue operations");
            
        _queueSize = _meter.CreateUpDownCounter<long>(
            "queue.size",
            unit: "{items}",
            description: "Current number of items in queue");
    }

    public void RecordOperation(string operation, bool success, double durationMs, 
                               Dictionary<string, object>? tags = null)
    {
        var allTags = new List<KeyValuePair<string, object?>>
        {
            new("operation", operation),
            new("success", success)
        };
        
        if (tags != null)
        {
            allTags.AddRange(tags.Select(kvp => new KeyValuePair<string, object?>(kvp.Key, kvp.Value)));
        }
        
        _operationCounter.Add(1, allTags.ToArray());
        _operationDuration.Record(durationMs, allTags.ToArray());
        
        Interlocked.Increment(ref _totalOperations);
        _operationCounts.AddOrUpdate(operation, 1, (_, count) => count + 1);
        _averageOperationTime.Add(durationMs);
        
        if (!success)
        {
            _logger.LogWarning("Operation {Operation} failed after {Duration}ms", 
                              operation, durationMs);
        }
    }

    public IActivity? StartActivity(string operationName)
    {
        var activity = _activitySource.StartActivity(operationName);
        return activity != null ? new ActivityWrapper(activity) : null;
    }

    // IQueueStatistics implementation
    public int CurrentCount => (int)_queueSize.GetValue();
    public long TotalOperations => _totalOperations;
    public double AverageOperationTime => _averageOperationTime.Average;
    public Dictionary<string, long> OperationCounts => 
        new(_operationCounts);
}
```

#### 2.2 Create QueueMetrics.cs
```csharp
namespace lvlup.DataFerry.Concurrency.Observability;

public sealed class QueueMetrics
{
    private readonly Meter _meter;
    private readonly string _queueName;
    
    // Performance counters
    public readonly Counter<long> AddOperations;
    public readonly Counter<long> DeleteOperations;
    public readonly Counter<long> PeekOperations;
    public readonly Counter<long> UpdateOperations;
    
    // Failure counters
    public readonly Counter<long> OperationFailures;
    public readonly Counter<long> ContentionEvents;
    
    // Histograms
    public readonly Histogram<double> OperationLatency;
    public readonly Histogram<int> SprayJumpLength;
    
    // Gauges
    public readonly ObservableGauge<double> QueueFullnessRatio;
    public readonly ObservableGauge<long> PendingDeletions;

    public QueueMetrics(IMeterFactory meterFactory, string queueName, 
                       Func<int> getCurrentCount, int maxSize)
    {
        _meter = meterFactory.Create($"DataFerry.Queue.{queueName}");
        _queueName = queueName;
        
        InitializeCounters();
        InitializeHistograms();
        InitializeGauges(getCurrentCount, maxSize);
    }
}
```

#### 2.3 Integration with ConcurrentPriorityQueue
```csharp
// Modified constructor
public ConcurrentPriorityQueue(
    ITaskOrchestrator taskOrchestrator,
    IComparer<TPriority> comparer,
    IServiceProvider? serviceProvider = null,
    ConcurrentPriorityQueueOptions? options = null)
{
    options ??= new ConcurrentPriorityQueueOptions();
    
    // Initialize observability if services provided
    if (serviceProvider != null)
    {
        var loggerFactory = serviceProvider.GetService<ILoggerFactory>();
        var meterFactory = serviceProvider.GetService<IMeterFactory>();
        
        if (loggerFactory != null && meterFactory != null)
        {
            _observability = new QueueObservability(
                loggerFactory, 
                meterFactory,
                options.QueueName ?? "Default",
                options.MaxSize);
        }
    }
    
    // Rest of initialization...
}
```

### Unit Tests Required
- TestObservability_RecordsMetricsCorrectly
- TestStatistics_TracksOperationCounts
- TestActivity_PropagatesContext
- TestMetrics_HandlesHighThroughput

---

## Agent 3: Query Operations & Performance

### Objectives
1. Implement TryPeek operations
2. Enhance enumerators
3. Optimize read performance

### File Structure
```
src/DataFerry/Concurrency/
├── Extensions/
│   ├── QueueQueryExtensions.cs (new)
│   └── EnumeratorExtensions.cs (new)
├── Enumerators/
│   ├── PriorityEnumerator.cs (new)
│   ├── FullEnumerator.cs (new)
│   └── SnapshotEnumerator.cs (new)
└── ConcurrentPriorityQueue.cs (modify)
```

### Implementation Tasks

#### 3.1 Add TryPeek Methods to ConcurrentPriorityQueue
```csharp
public bool TryPeek([MaybeNullWhen(false)] out TPriority priority, 
                   [MaybeNullWhen(false)] out TElement element)
{
    var activity = _observability?.StartActivity(nameof(TryPeek));
    var stopwatch = Stopwatch.StartNew();
    
    try
    {
        var curr = Volatile.Read(ref _head).GetNextNode(BottomLevel);
        
        while (curr.Type is not SkipListNode.NodeType.Tail)
        {
            // Skip invalid nodes
            if (NodeIsInvalidOrDeleted(curr))
            {
                curr = curr.GetNextNode(BottomLevel);
                continue;
            }
            
            // Found valid node - take snapshot of values
            priority = curr.Priority;
            element = curr.Element;
            
            // Double-check node is still valid after read
            if (!curr.IsDeleted)
            {
                _observability?.RecordOperation(nameof(TryPeek), true, 
                    stopwatch.ElapsedMilliseconds);
                return true;
            }
            
            // Node was deleted between checks, continue
            curr = curr.GetNextNode(BottomLevel);
        }
        
        priority = default!;
        element = default!;
        _observability?.RecordOperation(nameof(TryPeek), false, 
            stopwatch.ElapsedMilliseconds, 
            new Dictionary<string, object> { ["Reason"] = "Empty" });
        return false;
    }
    finally
    {
        activity?.Dispose();
    }
}

public bool TryPeekPriority([MaybeNullWhen(false)] out TPriority priority)
{
    return TryPeek(out priority, out _);
}

public bool TryPeekRange(int count, out List<(TPriority Priority, TElement Element)> items)
{
    items = new List<(TPriority, TElement)>(count);
    var curr = Volatile.Read(ref _head).GetNextNode(BottomLevel);
    
    while (curr.Type is not SkipListNode.NodeType.Tail && items.Count < count)
    {
        if (!NodeIsInvalidOrDeleted(curr))
        {
            items.Add((curr.Priority, curr.Element));
        }
        curr = curr.GetNextNode(BottomLevel);
    }
    
    return items.Count > 0;
}
```

#### 3.2 Create FullEnumerator.cs
```csharp
namespace lvlup.DataFerry.Concurrency.Enumerators;

internal sealed class FullEnumerator<TPriority, TElement> : 
    IEnumerator<(TPriority Priority, TElement Element)>, 
    IAsyncEnumerator<(TPriority Priority, TElement Element)>
{
    private readonly SkipListNode _head;
    private readonly int _clearVersion;
    private readonly Func<int> _getCurrentVersion;
    private SkipListNode? _current;
    private (TPriority Priority, TElement Element) _currentValue;

    public FullEnumerator(SkipListNode head, int clearVersion, Func<int> getCurrentVersion)
    {
        _head = head;
        _clearVersion = clearVersion;
        _getCurrentVersion = getCurrentVersion;
        _current = null;
    }

    public (TPriority Priority, TElement Element) Current => _currentValue;
    object IEnumerator.Current => Current;

    public bool MoveNext()
    {
        ThrowIfQueueCleared();
        
        _current = _current?.GetNextNode(0) ?? _head.GetNextNode(0);
        
        while (_current.Type is not SkipListNode.NodeType.Tail)
        {
            if (!NodeIsInvalidOrDeleted(_current))
            {
                _currentValue = (_current.Priority, _current.Element);
                return true;
            }
            _current = _current.GetNextNode(0);
        }
        
        return false;
    }

    public async ValueTask<bool> MoveNextAsync()
    {
        // Yield periodically for better async behavior
        if (Random.Shared.Next(100) == 0)
            await Task.Yield();
            
        return MoveNext();
    }

    private void ThrowIfQueueCleared()
    {
        if (_clearVersion != _getCurrentVersion())
            throw new InvalidOperationException("Queue was cleared during enumeration");
    }

    public void Reset() => _current = null;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    public void Dispose() { }
}
```

#### 3.3 Create QueueQueryExtensions.cs
```csharp
namespace lvlup.DataFerry.Concurrency.Extensions;

public static class QueueQueryExtensions
{
    public static IEnumerable<TElement> Where<TPriority, TElement>(
        this IConcurrentPriorityQueue<TPriority, TElement> queue,
        Func<TPriority, bool> predicate)
    {
        foreach (var (priority, element) in queue.GetFullEnumerator())
        {
            if (predicate(priority))
                yield return element;
        }
    }

    public static async IAsyncEnumerable<(TPriority Priority, TElement Element)> 
        ToAsyncEnumerable<TPriority, TElement>(
        this IConcurrentPriorityQueue<TPriority, TElement> queue,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await using var enumerator = queue.GetAsyncEnumerator(cancellationToken);
        while (await enumerator.MoveNextAsync())
        {
            yield return enumerator.Current;
        }
    }

    public static List<(TPriority Priority, TElement Element)> TakeSnapshot<TPriority, TElement>(
        this IConcurrentPriorityQueue<TPriority, TElement> queue,
        int maxItems = int.MaxValue)
    {
        var snapshot = new List<(TPriority, TElement)>();
        foreach (var item in queue.GetFullEnumerator())
        {
            snapshot.Add(item);
            if (snapshot.Count >= maxItems) break;
        }
        return snapshot;
    }
}
```

### Unit Tests Required
- TestTryPeek_ReturnsMinimumElement
- TestTryPeek_EmptyQueue_ReturnsFalse
- TestEnumerator_HandlesModificationsDuringIteration
- TestQueryExtensions_FilterCorrectly
- TestSnapshot_ConsistentView

---

## Agent 4: Advanced Features & Batch Operations

### Objectives
1. Implement batch operations
2. Add priority update functionality
3. Create adaptive algorithms

### File Structure
```
src/DataFerry/Concurrency/
├── Algorithms/
│   ├── AdaptiveSprayTracker.cs (new)
│   ├── BatchProcessor.cs (new)
│   └── PriorityUpdateStrategy.cs (new)
├── Extensions/
│   └── BatchOperationExtensions.cs (new)
└── ConcurrentPriorityQueue.cs (modify)
```

### Implementation Tasks

#### 4.1 Create BatchProcessor.cs
```csharp
namespace lvlup.DataFerry.Concurrency.Algorithms;

internal sealed class BatchProcessor<TPriority, TElement>
{
    private readonly ConcurrentPriorityQueue<TPriority, TElement> _queue;
    private readonly IComparer<TPriority> _comparer;
    private readonly INodePool<SkipListNode>? _nodePool;
    private readonly IQueueObservability? _observability;

    public async Task<BatchOperationResult> TryAddRangeAsync(
        IAsyncEnumerable<(TPriority Priority, TElement Element)> items,
        BatchOperationOptions options,
        CancellationToken cancellationToken = default)
    {
        var result = new BatchOperationResult();
        var batch = new List<PreparedNode>(options.BatchSize);
        
        await foreach (var item in items.WithCancellation(cancellationToken))
        {
            var preparedNode = PrepareNode(item.Priority, item.Element);
            batch.Add(preparedNode);
            
            if (batch.Count >= options.BatchSize)
            {
                ProcessBatch(batch, result, options);
                batch.Clear();
            }
            
            if (options.MaxItems.HasValue && result.SuccessCount >= options.MaxItems.Value)
                break;
        }
        
        if (batch.Count > 0)
        {
            ProcessBatch(batch, result, options);
        }
        
        return result;
    }

    private void ProcessBatch(
        List<PreparedNode> batch, 
        BatchOperationResult result,
        BatchOperationOptions options)
    {
        // Sort batch for better cache locality
        if (options.SortBatch)
        {
            batch.Sort((a, b) => _comparer.Compare(a.Priority, b.Priority));
        }
        
        // Process in parallel if batch is large enough
        if (batch.Count > options.ParallelThreshold)
        {
            Parallel.ForEach(batch, node =>
            {
                if (_queue.TryAddPreparedNode(node))
                {
                    Interlocked.Increment(ref result.SuccessCount);
                }
                else
                {
                    Interlocked.Increment(ref result.FailureCount);
                    _nodePool?.Return(node.Node);
                }
            });
        }
        else
        {
            foreach (var node in batch)
            {
                if (_queue.TryAddPreparedNode(node))
                {
                    result.SuccessCount++;
                }
                else
                {
                    result.FailureCount++;
                    _nodePool?.Return(node.Node);
                }
            }
        }
        
        _observability?.RecordOperation("BatchAdd", true, 0,
            new Dictionary<string, object>
            {
                ["BatchSize"] = batch.Count,
                ["SuccessRate"] = (double)result.SuccessCount / batch.Count
            });
    }
}

public class BatchOperationResult
{
    public int SuccessCount { get; set; }
    public int FailureCount { get; set; }
    public TimeSpan Duration { get; set; }
    public List<Exception> Errors { get; } = new();
}

public class BatchOperationOptions
{
    public int BatchSize { get; set; } = 32;
    public bool SortBatch { get; set; } = true;
    public int ParallelThreshold { get; set; } = 100;
    public int? MaxItems { get; set; }
}
```

#### 4.2 Create AdaptiveSprayTracker.cs
```csharp
namespace lvlup.DataFerry.Concurrency.Algorithms;

internal sealed class AdaptiveSprayTracker
{
    private readonly int _baseOffsetK;
    private readonly int _baseOffsetM;
    private readonly RollingWindow<SprayResult> _results;
    private int _currentOffsetK;
    private int _currentOffsetM;
    private readonly object _adjustmentLock = new();

    public AdaptiveSprayTracker(int offsetK, int offsetM, int windowSize = 100)
    {
        _baseOffsetK = _currentOffsetK = offsetK;
        _baseOffsetM = _currentOffsetM = offsetM;
        _results = new RollingWindow<SprayResult>(windowSize);
    }

    public (int OffsetK, int OffsetM) GetCurrentOffsets()
    {
        lock (_adjustmentLock)
        {
            return (_currentOffsetK, _currentOffsetM);
        }
    }

    public void RecordResult(bool success, int jumpLength, TimeSpan duration, int contentionLevel)
    {
        var result = new SprayResult
        {
            Success = success,
            JumpLength = jumpLength,
            Duration = duration,
            ContentionLevel = contentionLevel,
            Timestamp = DateTime.UtcNow
        };
        
        _results.Add(result);
        
        if (_results.Count >= _results.Capacity / 2)
        {
            AdjustParameters();
        }
    }

    private void AdjustParameters()
    {
        lock (_adjustmentLock)
        {
            var recentResults = _results.GetAll();
            var successRate = recentResults.Count(r => r.Success) / (double)recentResults.Count;
            var avgContentionLevel = recentResults.Average(r => r.ContentionLevel);
            var avgDuration = recentResults.Average(r => r.Duration.TotalMilliseconds);
            
            // Adjust OffsetM based on success rate and contention
            if (successRate < 0.6 && avgContentionLevel > 5)
            {
                // High contention, low success - increase spread
                _currentOffsetM = Math.Min(_currentOffsetM + 1, _baseOffsetM * 3);
            }
            else if (successRate > 0.9 && avgContentionLevel < 2)
            {
                // Low contention, high success - can be more aggressive
                _currentOffsetM = Math.Max(_currentOffsetM - 1, 1);
            }
            
            // Adjust OffsetK based on operation duration
            if (avgDuration > 10) // ms
            {
                _currentOffsetK = Math.Min(_currentOffsetK + 1, _baseOffsetK * 2);
            }
            else if (avgDuration < 1)
            {
                _currentOffsetK = Math.Max(_currentOffsetK - 1, 1);
            }
        }
    }

    private class SprayResult
    {
        public bool Success { get; init; }
        public int JumpLength { get; init; }
        public TimeSpan Duration { get; init; }
        public int ContentionLevel { get; init; }
        public DateTime Timestamp { get; init; }
    }
}
```

#### 4.3 Create PriorityUpdateStrategy.cs
```csharp
namespace lvlup.DataFerry.Concurrency.Algorithms;

internal sealed class PriorityUpdateStrategy<TPriority, TElement>
{
    private readonly ConcurrentPriorityQueue<TPriority, TElement> _queue;
    private readonly IComparer<TPriority> _comparer;
    private readonly IQueueObservability? _observability;

    public bool TryUpdatePriority(
        TPriority oldPriority,
        TPriority newPriority,
        Func<TElement, TElement> elementTransform,
        UpdateOptions options = default)
    {
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            // For same priority, just update in place
            if (_comparer.Compare(oldPriority, newPriority) == 0)
            {
                return _queue.Update(oldPriority, (p, e) => elementTransform(e));
            }
            
            // Need to remove and re-add
            TElement? element = default;
            var found = false;
            
            // Try to atomically remove old entry
            if (options.UseStrictMatching)
            {
                found = _queue.TryDeleteExact(oldPriority, options.ElementMatcher, out element);
            }
            else
            {
                found = _queue.TryDelete(oldPriority, out element);
            }
            
            if (!found)
            {
                _observability?.RecordOperation("UpdatePriority", false, 
                    stopwatch.ElapsedMilliseconds,
                    new Dictionary<string, object> { ["Reason"] = "NotFound" });
                return false;
            }
            
            // Transform element
            var newElement = elementTransform(element);
            
            // Try to add with new priority
            if (_queue.TryAdd(newPriority, newElement))
            {
                _observability?.RecordOperation("UpdatePriority", true, 
                    stopwatch.ElapsedMilliseconds);
                return true;
            }
            
            // Rollback on failure
            if (options.EnableRollback)
            {
                _queue.TryAdd(oldPriority, element);
            }
            
            return false;
        }
        catch (Exception ex)
        {
            _observability?.LogEvent(LogLevel.Error, 
                "Priority update failed: {Error}", ex.Message);
            throw;
        }
    }

    public async Task<int> BulkUpdatePrioritiesAsync(
        Func<TPriority, TElement, (TPriority NewPriority, TElement NewElement)?> updateFunc,
        CancellationToken cancellationToken = default)
    {
        var updates = new List<(TPriority OldPriority, TElement OldElement, 
                                TPriority NewPriority, TElement NewElement)>();
        
        // Phase 1: Collect updates
        foreach (var (priority, element) in _queue.GetFullEnumerator())
        {
            if (cancellationToken.IsCancellationRequested)
                break;
                
            var update = updateFunc(priority, element);
            if (update.HasValue)
            {
                updates.Add((priority, element, update.Value.NewPriority, update.Value.NewElement));
            }
        }
        
        // Phase 2: Apply updates
        var successCount = 0;
        foreach (var update in updates)
        {
            if (TryUpdatePriority(update.OldPriority, update.NewPriority, 
                                 _ => update.NewElement))
            {
                successCount++;
            }
        }
        
        return successCount;
    }
}

public struct UpdateOptions
{
    public bool UseStrictMatching { get; set; }
    public Func<TElement, bool>? ElementMatcher { get; set; }
    public bool EnableRollback { get; set; }
}
```

#### 4.4 Add Methods to ConcurrentPriorityQueue
```csharp
// Batch operations support
private readonly BatchProcessor<TPriority, TElement> _batchProcessor;
private readonly AdaptiveSprayTracker _adaptiveTracker;
private readonly PriorityUpdateStrategy<TPriority, TElement> _updateStrategy;

// In constructor
_batchProcessor = new BatchProcessor<TPriority, TElement>(this, _comparer, _nodePool, _observability);
_adaptiveTracker = new AdaptiveSprayTracker(_offsetK, _offsetM);
_updateStrategy = new PriorityUpdateStrategy<TPriority, TElement>(this, _comparer, _observability);

// Public API
public Task<BatchOperationResult> TryAddRangeAsync(
    IAsyncEnumerable<(TPriority Priority, TElement Element)> items,
    BatchOperationOptions? options = null,
    CancellationToken cancellationToken = default)
{
    return _batchProcessor.TryAddRangeAsync(items, options ?? new(), cancellationToken);
}

public bool TryUpdatePriority(
    TPriority oldPriority,
    TPriority newPriority,
    TElement element)
{
    return _updateStrategy.TryUpdatePriority(oldPriority, newPriority, _ => element);
}

// Modified SpraySearch to use adaptive parameters
internal SkipListNode SpraySearch(int currCount)
{
    var (offsetK, offsetM) = _adaptiveTracker.GetCurrentOffsets();
    var sprayParams = SprayParameters.CalculateParameters(currCount, _topLevel, offsetK, offsetM);
    
    var stopwatch = Stopwatch.StartNew();
    var result = PerformSprayWalk(sprayParams);
    
    _adaptiveTracker.RecordResult(
        success: result != _head,
        jumpLength: sprayParams.MaxJumpLength,
        duration: stopwatch.Elapsed,
        contentionLevel: EstimateContentionLevel());
        
    return result;
}
```

### Unit Tests Required
- TestBatchAdd_LargeBatch_CompletesSuccessfully
- TestBatchAdd_WithFailures_ReportsCorrectly
- TestAdaptiveSpray_AdjustsParameters
- TestPriorityUpdate_Atomic_Succeeds
- TestBulkUpdate_PartialFailure_RollsBack

---

## Integration Points

### Shared Test Infrastructure
Create `src/DataFerry.Tests/TestHelpers/ConcurrentPriorityQueueTestBase.cs`:
```csharp
public abstract class ConcurrentPriorityQueueTestBase
{
    protected Mock<ITaskOrchestrator> MockOrchestrator { get; private set; }
    protected Mock<IQueueObservability> MockObservability { get; private set; }
    
    [TestInitialize]
    public virtual void BaseSetup()
    {
        MockOrchestrator = new Mock<ITaskOrchestrator>();
        MockObservability = new Mock<IQueueObservability>();
        SetupMocks();
    }
    
    protected ConcurrentPriorityQueue<int, string> CreateTestQueue(
        int maxSize = 1000,
        bool enableObservability = true,
        bool enableNodePooling = true)
    {
        var options = new ConcurrentPriorityQueueOptions
        {
            MaxSize = maxSize,
            EnableNodePooling = enableNodePooling
        };
        
        return new ConcurrentPriorityQueue<int, string>(
            MockOrchestrator.Object,
            Comparer<int>.Default,
            enableObservability ? CreateMockServiceProvider() : null,
            options);
    }
}
```

### Coordination Timeline

**Week 1 - Setup Phase**
- All agents: Define and commit shared interfaces
- All agents: Create project structure and empty files
- All agents: Write unit test shells

**Week 2 - Core Implementation**
- Agent 1: Implement core infrastructure
- Agent 2: Implement observability (depends on Agent 1's IQueueObservability)
- Agent 3: Implement query operations (independent)
- Agent 4: Implement algorithms (independent)

**Week 3 - Integration**
- All agents: Integrate components into ConcurrentPriorityQueue
- All agents: Run integration tests
- All agents: Performance benchmarking

**Week 4 - Polish**
- All agents: Fix bugs found in integration
- All agents: Complete documentation
- All agents: Final performance tuning

## Success Criteria

1. All unit tests pass independently for each agent's work
2. Integration tests pass when all components combined
3. No performance regression vs. current implementation
4. Full observability coverage of all operations
5. Memory leak tests pass under sustained load

## Conflict Resolution

- Use feature branches: `cpq-agent-1-infrastructure`, `cpq-agent-2-observability`, etc.
- Daily sync on interface changes
- Shared test data in `TestHelpers/SharedTestData.cs`
- Performance benchmarks must improve or maintain current levels