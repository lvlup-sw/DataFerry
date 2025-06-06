// ===========================================================================
// <copyright file="BatchProcessor.cs" company="Level Up Software">
// Copyright (c) Level Up Software. All rights reserved.
// </copyright>
// ===========================================================================

using System.Diagnostics;

using lvlup.DataFerry.Concurrency.Contracts;

namespace lvlup.DataFerry.Concurrency.Algorithms;

/// <summary>
/// Provides high-performance batch processing capabilities for concurrent priority queue operations.
/// </summary>
/// <typeparam name="TPriority">The type of priority values.</typeparam>
/// <typeparam name="TElement">The type of elements stored in the queue.</typeparam>
/// <remarks>
/// <para>
/// This class implements optimized batch addition operations for the ConcurrentPriorityQueue,
/// supporting both sequential and parallel processing strategies based on batch size and configuration.
/// </para>
/// <para>
/// The processor supports asynchronous enumeration of items, automatic batching, and detailed
/// result tracking including success/failure counts and any exceptions encountered.
/// </para>
/// </remarks>
internal sealed class BatchProcessor<TPriority, TElement>
{
    #region Fields

    /// <summary>
    /// The priority queue to process batch operations against.
    /// </summary>
    private readonly ConcurrentPriorityQueue<TPriority, TElement> _queue;

    /// <summary>
    /// The comparer used for priority ordering.
    /// </summary>
    private readonly IComparer<TPriority> _comparer;

    /// <summary>
    /// Optional node pool for efficient memory reuse.
    /// </summary>
    private readonly INodePool<ConcurrentPriorityQueue<TPriority, TElement>.SkipListNode>? _nodePool;

    /// <summary>
    /// Observability interface for metrics and logging.
    /// </summary>
    private readonly IQueueObservability? _observability;

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="BatchProcessor{TPriority, TElement}"/> class.
    /// </summary>
    /// <param name="queue">The priority queue to operate on.</param>
    /// <param name="comparer">The priority comparer.</param>
    /// <param name="nodePool">Optional node pool for memory efficiency.</param>
    /// <param name="observability">Optional observability interface.</param>
    public BatchProcessor(
        ConcurrentPriorityQueue<TPriority, TElement> queue,
        IComparer<TPriority> comparer,
        INodePool<ConcurrentPriorityQueue<TPriority, TElement>.SkipListNode>? nodePool,
        IQueueObservability? observability)
    {
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _comparer = comparer ?? throw new ArgumentNullException(nameof(comparer));
        _nodePool = nodePool;
        _observability = observability;
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Attempts to add a range of items to the queue in optimized batches.
    /// </summary>
    /// <param name="items">An async enumerable sequence of priority-element pairs to add.</param>
    /// <param name="options">Configuration options for the batch operation.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>A result object containing statistics about the batch operation.</returns>
    /// <remarks>
    /// <para>
    /// This method processes items in configurable batch sizes, optionally sorting each batch
    /// for better cache locality and using parallel processing for large batches.
    /// </para>
    /// <para>
    /// The operation can be limited by MaxItems in the options, and will respect cancellation
    /// requests between batch processing.
    /// </para>
    /// </remarks>
    public async Task<BatchOperationResult> TryAddRangeAsync(
        IAsyncEnumerable<(TPriority Priority, TElement Element)> items,
        BatchOperationOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(items, nameof(items));
        ArgumentNullException.ThrowIfNull(options, nameof(options));

        var result = new BatchOperationResult();
        var batch = new List<PreparedNode>(options.BatchSize);
        var stopwatch = Stopwatch.StartNew();

        try
        {
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

            // Process any remaining items
            if (batch.Count > 0)
            {
                ProcessBatch(batch, result, options);
            }
        }
        catch (Exception ex)
        {
            result.Errors.Add(ex);
            _observability?.LogEvent(Microsoft.Extensions.Logging.LogLevel.Error,
                "Batch processing failed: {Error}", ex.Message);
        }
        finally
        {
            result.Duration = stopwatch.Elapsed;
        }

        return result;
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Prepares a node for batch insertion.
    /// </summary>
    /// <param name="priority">The priority of the node.</param>
    /// <param name="element">The element to store in the node.</param>
    /// <returns>A prepared node ready for insertion.</returns>
    private PreparedNode PrepareNode(TPriority priority, TElement element)
    {
        var insertLevel = _queue.GenerateLevel();
        var node = _nodePool?.Rent() ?? 
            new ConcurrentPriorityQueue<TPriority, TElement>.SkipListNode(
                priority, element, insertLevel, _comparer);

        return new PreparedNode(node, priority, element, insertLevel);
    }

    /// <summary>
    /// Processes a batch of prepared nodes.
    /// </summary>
    /// <param name="batch">The batch of nodes to process.</param>
    /// <param name="result">The result object to update with statistics.</param>
    /// <param name="options">The batch operation options.</param>
    private void ProcessBatch(
        List<PreparedNode> batch,
        BatchOperationResult result,
        BatchOperationOptions options)
    {
        if (batch.Count == 0) return;

        var batchStopwatch = Stopwatch.StartNew();

        // Sort batch for better cache locality if requested
        if (options.SortBatch)
        {
            batch.Sort((a, b) => _comparer.Compare(a.Priority, b.Priority));
        }

        // Process in parallel if batch is large enough
        if (batch.Count > options.ParallelThreshold)
        {
            var successCount = 0;
            var failureCount = 0;

            Parallel.ForEach(batch, node =>
            {
                if (TryAddPreparedNode(node))
                {
                    Interlocked.Increment(ref successCount);
                }
                else
                {
                    Interlocked.Increment(ref failureCount);
                    _nodePool?.Return(node.Node);
                }
            });

            result.SuccessCount += successCount;
            result.FailureCount += failureCount;
        }
        else
        {
            // Sequential processing for smaller batches
            foreach (var node in batch)
            {
                if (TryAddPreparedNode(node))
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

        var batchDuration = batchStopwatch.ElapsedMilliseconds;
        _observability?.RecordOperation("BatchAdd", true, batchDuration,
            new Dictionary<string, object>
            {
                ["BatchSize"] = batch.Count,
                ["SuccessRate"] = batch.Count > 0 ? (double)result.SuccessCount / batch.Count : 0
            });
    }

    /// <summary>
    /// Attempts to add a prepared node to the queue.
    /// </summary>
    /// <param name="preparedNode">The prepared node to add.</param>
    /// <returns>True if the node was successfully added; otherwise, false.</returns>
    private bool TryAddPreparedNode(PreparedNode preparedNode)
    {
        // This method would need to be exposed by ConcurrentPriorityQueue
        // For now, we'll use the standard TryAdd method
        return _queue.TryAdd(preparedNode.Priority, preparedNode.Element);
    }

    #endregion

    #region Nested Types

    /// <summary>
    /// Represents a node prepared for batch insertion.
    /// </summary>
    private readonly struct PreparedNode
    {
        /// <summary>
        /// Gets the skip list node instance.
        /// </summary>
        public ConcurrentPriorityQueue<TPriority, TElement>.SkipListNode Node { get; }

        /// <summary>
        /// Gets the priority value.
        /// </summary>
        public TPriority Priority { get; }

        /// <summary>
        /// Gets the element value.
        /// </summary>
        public TElement Element { get; }

        /// <summary>
        /// Gets the insertion level for the node.
        /// </summary>
        public int InsertLevel { get; }

        /// <summary>
        /// Initializes a new instance of the <see cref="PreparedNode"/> struct.
        /// </summary>
        /// <param name="node">The skip list node.</param>
        /// <param name="priority">The priority value.</param>
        /// <param name="element">The element value.</param>
        /// <param name="insertLevel">The insertion level.</param>
        public PreparedNode(
            ConcurrentPriorityQueue<TPriority, TElement>.SkipListNode node,
            TPriority priority,
            TElement element,
            int insertLevel)
        {
            Node = node;
            Priority = priority;
            Element = element;
            InsertLevel = insertLevel;
        }
    }

    #endregion
}

/// <summary>
/// Represents the result of a batch operation on the concurrent priority queue.
/// </summary>
public class BatchOperationResult
{
    /// <summary>
    /// Gets or sets the number of items successfully processed.
    /// </summary>
    public int SuccessCount { get; set; }

    /// <summary>
    /// Gets or sets the number of items that failed to process.
    /// </summary>
    public int FailureCount { get; set; }

    /// <summary>
    /// Gets or sets the total duration of the batch operation.
    /// </summary>
    public TimeSpan Duration { get; set; }

    /// <summary>
    /// Gets the collection of errors encountered during processing.
    /// </summary>
    public List<Exception> Errors { get; } = new();

    /// <summary>
    /// Gets the overall success rate as a percentage.
    /// </summary>
    public double SuccessRate => 
        (SuccessCount + FailureCount) > 0 
            ? (double)SuccessCount / (SuccessCount + FailureCount) * 100 
            : 0;
}

/// <summary>
/// Configuration options for batch operations on the concurrent priority queue.
/// </summary>
public class BatchOperationOptions
{
    /// <summary>
    /// Gets or sets the size of each batch to process.
    /// </summary>
    /// <value>Default is 32.</value>
    public int BatchSize { get; set; } = 32;

    /// <summary>
    /// Gets or sets a value indicating whether to sort each batch before processing.
    /// </summary>
    /// <value>Default is true.</value>
    public bool SortBatch { get; set; } = true;

    /// <summary>
    /// Gets or sets the minimum batch size threshold for parallel processing.
    /// </summary>
    /// <value>Default is 100.</value>
    public int ParallelThreshold { get; set; } = 100;

    /// <summary>
    /// Gets or sets the maximum number of items to process.
    /// </summary>
    /// <value>Null indicates no limit.</value>
    public int? MaxItems { get; set; }
}