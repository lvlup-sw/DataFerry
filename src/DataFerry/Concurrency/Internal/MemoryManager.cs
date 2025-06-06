// ===========================================================================
// <copyright file="MemoryManager.cs" company="Level Up Software">
// Copyright (c) Level Up Software. All rights reserved.
// </copyright>
// ===========================================================================

using System.Runtime.CompilerServices;

using Microsoft.Extensions.Logging;

using lvlup.DataFerry.Concurrency.Contracts;
using lvlup.DataFerry.Orchestrators.Contracts;

namespace lvlup.DataFerry.Concurrency.Internal;

/// <summary>
/// Manages memory-related operations for the concurrent priority queue including cleanup and monitoring.
/// </summary>
/// <typeparam name="TPriority">The type used for priority values.</typeparam>
/// <typeparam name="TElement">The type of the elements stored in the queue.</typeparam>
/// <remarks>
/// <para>
/// This class coordinates memory management activities including old structure cleanup after
/// clear operations, memory pressure monitoring, and coordination with the node pool and
/// fallback deletion processor to ensure efficient memory usage.
/// </para>
/// <para>
/// The memory manager uses generation tracking to safely clean up old structures without
/// interfering with ongoing operations on newer structures.
/// </para>
/// </remarks>
internal sealed class MemoryManager<TPriority, TElement>
{
    private readonly INodePool<ConcurrentPriorityQueue<TPriority, TElement>.SkipListNode>? _nodePool;
    private readonly FallbackDeletionProcessor<TPriority, TElement>? _fallbackProcessor;
    private readonly IQueueObservability? _observability;
    private readonly ITaskOrchestrator _taskOrchestrator;
    private readonly object _cleanupLock = new();
    private volatile int _activeCleanupTasks;

    /// <summary>
    /// Initializes a new instance of the <see cref="MemoryManager{TPriority, TElement}"/> class.
    /// </summary>
    /// <param name="nodePool">Optional node pool for memory reuse.</param>
    /// <param name="fallbackProcessor">Optional fallback processor for deferred deletions.</param>
    /// <param name="observability">Optional observability interface for monitoring.</param>
    /// <param name="taskOrchestrator">Task orchestrator for background operations.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="taskOrchestrator"/> is null.</exception>
    public MemoryManager(
        INodePool<ConcurrentPriorityQueue<TPriority, TElement>.SkipListNode>? nodePool,
        FallbackDeletionProcessor<TPriority, TElement>? fallbackProcessor,
        IQueueObservability? observability,
        ITaskOrchestrator taskOrchestrator)
    {
        ArgumentNullException.ThrowIfNull(taskOrchestrator, nameof(taskOrchestrator));

        _nodePool = nodePool;
        _fallbackProcessor = fallbackProcessor;
        _observability = observability;
        _taskOrchestrator = taskOrchestrator;
    }

    /// <summary>
    /// Gets the number of active cleanup tasks.
    /// </summary>
    public int ActiveCleanupTasks => _activeCleanupTasks;

    /// <summary>
    /// Schedules cleanup of an old skip list structure after a clear operation.
    /// </summary>
    /// <param name="oldHead">The head node of the old structure.</param>
    /// <param name="oldTail">The tail node of the old structure.</param>
    /// <param name="generation">The generation number of the old structure.</param>
    /// <param name="estimatedNodeCount">The estimated number of nodes in the old structure.</param>
    /// <remarks>
    /// This method schedules asynchronous cleanup of the old structure to avoid blocking
    /// the clear operation. The cleanup process traverses the old structure and ensures
    /// all nodes are properly cleaned up without interfering with the new structure.
    /// </remarks>
    public void ScheduleStructureCleanup(
        ConcurrentPriorityQueue<TPriority, TElement>.SkipListNode oldHead, 
        ConcurrentPriorityQueue<TPriority, TElement>.SkipListNode oldTail, 
        int generation,
        int estimatedNodeCount)
    {
        ArgumentNullException.ThrowIfNull(oldHead, nameof(oldHead));
        ArgumentNullException.ThrowIfNull(oldTail, nameof(oldTail));

        Interlocked.Increment(ref _activeCleanupTasks);

        _taskOrchestrator.Run(async () =>
        {
            try
            {
                await CleanupOldStructureAsync(oldHead, oldTail, generation, estimatedNodeCount).ConfigureAwait(false);
            }
            finally
            {
                Interlocked.Decrement(ref _activeCleanupTasks);
            }
        });
    }

    /// <summary>
    /// Performs cleanup of an old skip list structure.
    /// </summary>
    private async Task CleanupOldStructureAsync(
        ConcurrentPriorityQueue<TPriority, TElement>.SkipListNode oldHead, 
        ConcurrentPriorityQueue<TPriority, TElement>.SkipListNode oldTail,
        int generation,
        int estimatedNodeCount)
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var cleanedNodes = 0;
        var failedNodes = 0;

        try
        {
            _observability?.LogEvent(LogLevel.Information,
                "Starting cleanup of old structure. Generation: {Generation}, EstimatedNodes: {NodeCount}",
                generation, estimatedNodeCount);

            // Clear node pool to prevent reuse of old nodes
            _nodePool?.Clear();

            // Traverse the old structure and clean up nodes
            var current = oldHead.GetNextNode(0);
            var batchSize = Math.Max(10, estimatedNodeCount / 100); // Process in batches
            var currentBatch = 0;

            while (current != null && current != oldTail)
            {
                var next = current.GetNextNode(0);

                try
                {
                    // Mark node as deleted if not already
                    if (!current.IsDeleted)
                    {
                        current.IsDeleted = true;
                    }

                    // For data nodes, clear references to help GC
                    if (current.Type == ConcurrentPriorityQueue<TPriority, TElement>.SkipListNode.NodeType.Data)
                    {
                        ClearNodeReferences(current);
                        cleanedNodes++;
                    }
                }
                catch (Exception ex)
                {
                    failedNodes++;
                    _observability?.LogEvent(LogLevel.Warning,
                        "Failed to clean node during structure cleanup: {Error}", ex.Message);
                }

                current = next;
                currentBatch++;

                // Yield periodically to avoid blocking
                if (currentBatch >= batchSize)
                {
                    await Task.Yield();
                    currentBatch = 0;
                }
            }

            // Clear sentinel nodes
            ClearSentinelNode(oldHead);
            ClearSentinelNode(oldTail);

            stopwatch.Stop();

            _observability?.RecordOperation("StructureCleanup", true, stopwatch.ElapsedMilliseconds,
                new Dictionary<string, object>
                {
                    ["Generation"] = generation,
                    ["CleanedNodes"] = cleanedNodes,
                    ["FailedNodes"] = failedNodes,
                    ["TotalNodes"] = estimatedNodeCount
                });

            _observability?.LogEvent(LogLevel.Information,
                "Completed cleanup of old structure. Generation: {Generation}, Cleaned: {CleanedNodes}, Failed: {FailedNodes}, Duration: {Duration}ms",
                generation, cleanedNodes, failedNodes, stopwatch.ElapsedMilliseconds);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            
            _observability?.RecordOperation("StructureCleanup", false, stopwatch.ElapsedMilliseconds,
                new Dictionary<string, object>
                {
                    ["Generation"] = generation,
                    ["Error"] = ex.Message
                });

            _observability?.LogEvent(LogLevel.Error,
                "Structure cleanup failed for generation {Generation}: {Error}",
                generation, ex.Message);
        }
    }

    /// <summary>
    /// Attempts to reclaim memory by triggering garbage collection if memory pressure is high.
    /// </summary>
    /// <param name="force">If true, forces garbage collection regardless of memory pressure.</param>
    /// <returns><c>true</c> if garbage collection was triggered; otherwise, <c>false</c>.</returns>
    /// <remarks>
    /// This method should be used judiciously as forced garbage collection can impact performance.
    /// It's primarily intended for scenarios where large amounts of memory have been freed
    /// and immediate reclamation is beneficial.
    /// </remarks>
    public bool TryReclaimMemory(bool force = false)
    {
        var beforeMemory = GC.GetTotalMemory(false);
        
        // Check if we should trigger GC
        if (!force && beforeMemory < GetMemoryPressureThreshold())
        {
            return false;
        }

        _observability?.LogEvent(LogLevel.Information,
            "Triggering memory reclamation. Current memory: {BeforeMemory} bytes",
            beforeMemory);

        // Perform garbage collection
        GC.Collect(2, GCCollectionMode.Optimized, blocking: false);

        var afterMemory = GC.GetTotalMemory(false);
        var reclaimed = beforeMemory - afterMemory;

        _observability?.RecordOperation("MemoryReclaim", true, 0,
            new Dictionary<string, object>
            {
                ["BeforeMemory"] = beforeMemory,
                ["AfterMemory"] = afterMemory,
                ["ReclaimedBytes"] = reclaimed,
                ["Forced"] = force
            });

        return true;
    }

    /// <summary>
    /// Clears references in a data node to help garbage collection.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ClearNodeReferences(ConcurrentPriorityQueue<TPriority, TElement>.SkipListNode node)
    {
        // Clear element reference
        if (!typeof(TElement).IsValueType)
        {
            node.Element = default!;
        }

        // Note: Priority is read-only and cannot be cleared

        // Clear next node references
        for (int i = 0; i <= node.TopLevel; i++)
        {
            node.SetNextNode(i, null!);
        }
    }

    /// <summary>
    /// Clears references in a sentinel node.
    /// </summary>
    private void ClearSentinelNode(ConcurrentPriorityQueue<TPriority, TElement>.SkipListNode sentinelNode)
    {
        // Clear all next pointers
        for (int i = 0; i <= sentinelNode.TopLevel; i++)
        {
            sentinelNode.SetNextNode(i, null!);
        }
    }

    /// <summary>
    /// Gets the memory pressure threshold for triggering garbage collection.
    /// </summary>
    private long GetMemoryPressureThreshold()
    {
        // Trigger GC if memory usage exceeds 100MB
        // This is a conservative threshold that can be tuned based on application needs
        return 100 * 1024 * 1024;
    }

}