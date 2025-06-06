// ===========================================================================
// <copyright file="ConcurrentPriorityQueueOptions.cs" company="Level Up Software">
// Copyright (c) Level Up Software. All rights reserved.
// </copyright>
// ===========================================================================

namespace lvlup.DataFerry.Concurrency;

/// <summary>
/// Provides configuration options for <see cref="ConcurrentPriorityQueue{TPriority,TElement}"/>.
/// </summary>
/// <remarks>
/// <para>
/// This class encapsulates all configuration parameters for the concurrent priority queue,
/// including capacity limits, performance tuning parameters, and feature toggles.
/// Default values are provided for all options to ensure reasonable behavior out of the box.
/// </para>
/// <para>
/// Options can be adjusted based on specific use cases:
/// - High-throughput scenarios may benefit from node pooling and increased spray parameters
/// - Low-latency scenarios may prefer smaller spray offsets for more predictable behavior
/// - Memory-constrained environments should set appropriate MaxSize limits
/// </para>
/// </remarks>
public sealed class ConcurrentPriorityQueueOptions
{
    /// <summary>
    /// Gets or sets the maximum number of elements allowed in the queue.
    /// </summary>
    /// <value>
    /// The maximum queue size. Defaults to <see cref="ConcurrentPriorityQueue{TPriority,TElement}.DefaultMaxSize"/>.
    /// </value>
    /// <remarks>
    /// Set to <see cref="int.MaxValue"/> to effectively remove size restrictions.
    /// When the queue reaches this limit, add operations will fail until space becomes available.
    /// </remarks>
    public int MaxSize { get; set; } = ConcurrentPriorityQueue<object, object>.DefaultMaxSize;

    /// <summary>
    /// Gets or sets the tuning constant K for the SprayList delete-min operation.
    /// </summary>
    /// <value>
    /// The spray offset K parameter. Defaults to <see cref="ConcurrentPriorityQueue{TPriority,TElement}.DefaultSprayOffsetK"/>.
    /// </value>
    /// <remarks>
    /// This value affects the starting height of spray operations. Higher values may reduce
    /// contention but could result in selecting elements with higher priorities.
    /// </remarks>
    public int SprayOffsetK { get; set; } = ConcurrentPriorityQueue<object, object>.DefaultSprayOffsetK;

    /// <summary>
    /// Gets or sets the tuning constant M for the SprayList delete-min operation.
    /// </summary>
    /// <value>
    /// The spray offset M parameter. Defaults to <see cref="ConcurrentPriorityQueue{TPriority,TElement}.DefaultSprayOffsetM"/>.
    /// </value>
    /// <remarks>
    /// This value scales the maximum jump length in spray operations. Increase this value
    /// when reducing contention is more important than strict priority ordering.
    /// </remarks>
    public int SprayOffsetM { get; set; } = ConcurrentPriorityQueue<object, object>.DefaultSprayOffsetM;

    /// <summary>
    /// Gets or sets the promotion probability for skip list nodes.
    /// </summary>
    /// <value>
    /// The probability of promoting a node to higher levels. Defaults to <see cref="ConcurrentPriorityQueue{TPriority,TElement}.DefaultPromotionProbability"/>.
    /// </value>
    /// <remarks>
    /// Values must be between 0 (exclusive) and 1 (exclusive). Common values are 0.25 or 0.5.
    /// Lower values create flatter structures with potentially better cache locality.
    /// </remarks>
    public double PromotionProbability { get; set; } = ConcurrentPriorityQueue<object, object>.DefaultPromotionProbability;

    /// <summary>
    /// Gets or sets whether node pooling is enabled for memory efficiency.
    /// </summary>
    /// <value>
    /// <c>true</c> to enable node pooling; otherwise, <c>false</c>. Defaults to <c>true</c>.
    /// </value>
    /// <remarks>
    /// Node pooling reduces garbage collection pressure in high-throughput scenarios
    /// by reusing node objects. Disable if memory usage patterns are unpredictable.
    /// </remarks>
    public bool EnableNodePooling { get; set; } = true;

    /// <summary>
    /// Gets or sets whether adaptive spray parameters are enabled.
    /// </summary>
    /// <value>
    /// <c>true</c> to enable adaptive tuning; otherwise, <c>false</c>. Defaults to <c>true</c>.
    /// </value>
    /// <remarks>
    /// When enabled, spray parameters are automatically adjusted based on observed
    /// contention patterns and operation success rates.
    /// </remarks>
    public bool EnableAdaptiveSpray { get; set; } = true;

    /// <summary>
    /// Gets or sets the name of the queue for identification in logs and metrics.
    /// </summary>
    /// <value>
    /// The queue name. Defaults to "Default".
    /// </value>
    /// <remarks>
    /// This name is used as a dimension in metrics and as part of logger names,
    /// enabling filtering and aggregation by queue instance.
    /// </remarks>
    public string QueueName { get; set; } = "Default";

    /// <summary>
    /// Gets or sets whether detailed statistics tracking is enabled.
    /// </summary>
    /// <value>
    /// <c>true</c> to enable statistics; otherwise, <c>false</c>. Defaults to <c>true</c>.
    /// </value>
    /// <remarks>
    /// Statistics tracking has minimal overhead but can be disabled in
    /// extremely performance-sensitive scenarios.
    /// </remarks>
    public bool EnableStatistics { get; set; } = true;

    /// <summary>
    /// Gets or sets the time window for sliding statistics.
    /// </summary>
    /// <value>
    /// The statistics window duration. Defaults to 5 minutes.
    /// </value>
    /// <remarks>
    /// This affects how long historical data is retained for trend analysis
    /// and percentile calculations.
    /// </remarks>
    public TimeSpan StatisticsWindow { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Gets or sets whether distributed tracing is enabled.
    /// </summary>
    /// <value>
    /// <c>true</c> to enable tracing; otherwise, <c>false</c>. Defaults to <c>true</c>.
    /// </value>
    /// <remarks>
    /// Distributed tracing provides detailed operation tracking but may have
    /// performance implications in high-throughput scenarios.
    /// </remarks>
    public bool EnableTracing { get; set; } = true;

    /// <summary>
    /// Gets or sets the initial node pool size when pooling is enabled.
    /// </summary>
    /// <value>
    /// The initial pool size. Defaults to processor count * 4.
    /// </value>
    /// <remarks>
    /// Larger pools reduce allocation pressure but consume more memory.
    /// The pool will grow as needed up to MaxNodePoolSize.
    /// </remarks>
    public int InitialNodePoolSize { get; set; } = Environment.ProcessorCount * 4;

    /// <summary>
    /// Gets or sets the maximum node pool size when pooling is enabled.
    /// </summary>
    /// <value>
    /// The maximum pool size. Defaults to processor count * 16.
    /// </value>
    /// <remarks>
    /// This limits memory consumption by the node pool. Set based on
    /// expected peak throughput and available memory.
    /// </remarks>
    public int MaxNodePoolSize { get; set; } = Environment.ProcessorCount * 16;

    /// <summary>
    /// Gets or sets whether to use fallback deletion processing for failed deletions.
    /// </summary>
    /// <value>
    /// <c>true</c> to enable fallback processing; otherwise, <c>false</c>. Defaults to <c>true</c>.
    /// </value>
    /// <remarks>
    /// Fallback processing ensures nodes are eventually removed even if the
    /// primary deletion fails due to contention or other issues.
    /// </remarks>
    public bool EnableFallbackDeletion { get; set; } = true;

    /// <summary>
    /// Gets or sets the batch size for background cleanup operations.
    /// </summary>
    /// <value>
    /// The cleanup batch size. Defaults to 32.
    /// </value>
    /// <remarks>
    /// Larger batches are more efficient but may cause longer pauses.
    /// Adjust based on latency requirements.
    /// </remarks>
    public int CleanupBatchSize { get; set; } = 32;

    /// <summary>
    /// Gets or sets whether batch operations are enabled.
    /// </summary>
    /// <value>
    /// <c>true</c> to enable batch operations; otherwise, <c>false</c>. Defaults to <c>true</c>.
    /// </value>
    /// <remarks>
    /// Batch operations provide optimized methods for adding multiple items at once,
    /// with support for parallel processing and cache-friendly sorting.
    /// </remarks>
    public bool EnableBatchOperations { get; set; } = true;

    /// <summary>
    /// Gets or sets whether priority update operations are enabled.
    /// </summary>
    /// <value>
    /// <c>true</c> to enable priority updates; otherwise, <c>false</c>. Defaults to <c>true</c>.
    /// </value>
    /// <remarks>
    /// Priority updates allow atomic modification of element priorities without
    /// the risk of losing elements during concurrent operations.
    /// </remarks>
    public bool EnablePriorityUpdates { get; set; } = true;

    /// <summary>
    /// Gets or sets the window size for adaptive spray tracking.
    /// </summary>
    /// <value>
    /// The number of recent operations to track. Defaults to 100.
    /// </value>
    /// <remarks>
    /// A larger window provides more stable parameter adjustments but responds
    /// more slowly to changing workload patterns.
    /// </remarks>
    public int AdaptiveWindowSize { get; set; } = 100;

    /// <summary>
    /// Validates the options and throws if any values are invalid.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when any option value is out of valid range.</exception>
    public void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxSize, 1, nameof(MaxSize));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(SprayOffsetK, nameof(SprayOffsetK));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(SprayOffsetM, nameof(SprayOffsetM));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(PromotionProbability, nameof(PromotionProbability));
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(PromotionProbability, 1, nameof(PromotionProbability));
        ArgumentException.ThrowIfNullOrEmpty(QueueName, nameof(QueueName));
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(StatisticsWindow, TimeSpan.Zero, nameof(StatisticsWindow));
        ArgumentOutOfRangeException.ThrowIfLessThan(InitialNodePoolSize, 1, nameof(InitialNodePoolSize));
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxNodePoolSize, InitialNodePoolSize, nameof(MaxNodePoolSize));
        ArgumentOutOfRangeException.ThrowIfLessThan(CleanupBatchSize, 1, nameof(CleanupBatchSize));
        ArgumentOutOfRangeException.ThrowIfLessThan(AdaptiveWindowSize, 10, nameof(AdaptiveWindowSize));
    }

    /// <summary>
    /// Creates a copy of the current options.
    /// </summary>
    /// <returns>A new instance with the same values.</returns>
    public ConcurrentPriorityQueueOptions Clone()
    {
        return new ConcurrentPriorityQueueOptions
        {
            MaxSize = MaxSize,
            SprayOffsetK = SprayOffsetK,
            SprayOffsetM = SprayOffsetM,
            PromotionProbability = PromotionProbability,
            EnableNodePooling = EnableNodePooling,
            EnableAdaptiveSpray = EnableAdaptiveSpray,
            QueueName = QueueName,
            EnableStatistics = EnableStatistics,
            StatisticsWindow = StatisticsWindow,
            EnableTracing = EnableTracing,
            InitialNodePoolSize = InitialNodePoolSize,
            MaxNodePoolSize = MaxNodePoolSize,
            EnableFallbackDeletion = EnableFallbackDeletion,
            CleanupBatchSize = CleanupBatchSize,
            EnableBatchOperations = EnableBatchOperations,
            EnablePriorityUpdates = EnablePriorityUpdates,
            AdaptiveWindowSize = AdaptiveWindowSize
        };
    }
}