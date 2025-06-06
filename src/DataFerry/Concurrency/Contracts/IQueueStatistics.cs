// ===========================================================================
// <copyright file="IQueueStatistics.cs" company="Level Up Software">
// Copyright (c) Level Up Software. All rights reserved.
// </copyright>
// ===========================================================================

namespace lvlup.DataFerry.Concurrency.Contracts;

/// <summary>
/// Provides real-time statistics and metrics about queue operations and state.
/// </summary>
/// <remarks>
/// <para>
/// This interface exposes key performance indicators and operational metrics
/// for monitoring queue health and performance. Implementations should provide
/// thread-safe access to statistics without significantly impacting queue performance.
/// </para>
/// </remarks>
public interface IQueueStatistics
{
    /// <summary>
    /// Gets the current number of items in the queue.
    /// </summary>
    /// <value>The current item count.</value>
    /// <remarks>
    /// This value may be approximate in highly concurrent scenarios due to
    /// ongoing operations. It provides a point-in-time snapshot of queue size.
    /// </remarks>
    int CurrentCount { get; }

    /// <summary>
    /// Gets the total number of operations performed on the queue since initialization.
    /// </summary>
    /// <value>The cumulative operation count across all operation types.</value>
    /// <remarks>
    /// This counter includes all operations regardless of success or failure,
    /// providing insight into overall queue activity.
    /// </remarks>
    long TotalOperations { get; }

    /// <summary>
    /// Gets the average operation time in milliseconds across all operations.
    /// </summary>
    /// <value>The rolling average of operation durations.</value>
    /// <remarks>
    /// This metric helps identify performance trends and potential bottlenecks.
    /// The average is typically calculated over a sliding time window.
    /// </remarks>
    double AverageOperationTime { get; }

    /// <summary>
    /// Gets a dictionary of operation counts by operation type.
    /// </summary>
    /// <value>A dictionary mapping operation names to their occurrence counts.</value>
    /// <remarks>
    /// <para>
    /// Provides detailed breakdown of queue usage patterns. Common operation types include:
    /// </para>
    /// <list type="bullet">
    /// <item>"TryAdd" - Addition attempts</item>
    /// <item>"TryDeleteMin" - Minimum deletion attempts</item>
    /// <item>"TryDelete" - Specific item deletion attempts</item>
    /// <item>"Update" - Priority update operations</item>
    /// <item>"Clear" - Queue clear operations</item>
    /// </list>
    /// </remarks>
    Dictionary<string, long> OperationCounts { get; }
}