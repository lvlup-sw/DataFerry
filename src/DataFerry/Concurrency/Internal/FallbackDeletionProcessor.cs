// ===========================================================================
// <copyright file="FallbackDeletionProcessor.cs" company="Level Up Software">
// Copyright (c) Level Up Software. All rights reserved.
// </copyright>
// ===========================================================================

using System.Threading.Channels;

using Microsoft.Extensions.Logging;

using lvlup.DataFerry.Concurrency.Contracts;

namespace lvlup.DataFerry.Concurrency.Internal;

/// <summary>
/// Processes node deletions asynchronously when primary deletion attempts fail, ensuring eventual consistency.
/// </summary>
/// <typeparam name="TPriority">The type used for priority values.</typeparam>
/// <typeparam name="TElement">The type of the elements stored in the queue.</typeparam>
/// <remarks>
/// <para>
/// This processor handles fallback deletion scenarios where immediate physical removal of nodes
/// fails due to contention or other transient issues. It uses a background task with an unbounded
/// channel to queue nodes for retry, ensuring that logically deleted nodes are eventually removed
/// from the skip list structure.
/// </para>
/// <para>
/// The processor is designed to be resilient to failures and will continue attempting deletions
/// until successful or the processor is disposed. This helps maintain memory efficiency and
/// structural integrity of the concurrent priority queue.
/// </para>
/// </remarks>
internal sealed class FallbackDeletionProcessor<TPriority, TElement> : IDisposable
{
    private readonly Channel<ConcurrentPriorityQueue<TPriority, TElement>.SkipListNode> _deletionChannel;
    private readonly Task _processingTask;
    private readonly CancellationTokenSource _cancellationTokenSource;
    private readonly IQueueObservability? _observability;
    private readonly Action<ConcurrentPriorityQueue<TPriority, TElement>.SkipListNode> _removeNodePhysically;
    private readonly TimeSpan _retryDelay = TimeSpan.FromMilliseconds(10);
    private readonly int _maxRetries = 3;

    /// <summary>
    /// Initializes a new instance of the <see cref="FallbackDeletionProcessor{TPriority, TElement}"/> class.
    /// </summary>
    /// <param name="observability">Optional observability interface for logging and metrics.</param>
    /// <param name="removeNodePhysically">The delegate to perform physical node removal.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="removeNodePhysically"/> is null.</exception>
    public FallbackDeletionProcessor(
        IQueueObservability? observability,
        Action<ConcurrentPriorityQueue<TPriority, TElement>.SkipListNode> removeNodePhysically)
    {
        ArgumentNullException.ThrowIfNull(removeNodePhysically, nameof(removeNodePhysically));

        _observability = observability;
        _removeNodePhysically = removeNodePhysically;
        _cancellationTokenSource = new CancellationTokenSource();
        _deletionChannel = Channel.CreateUnbounded<ConcurrentPriorityQueue<TPriority, TElement>.SkipListNode>(
            new UnboundedChannelOptions 
            { 
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = false
            });
        _processingTask = Task.Run(ProcessDeletionsAsync);
    }

    /// <summary>
    /// Gets the number of nodes currently queued for deletion.
    /// </summary>
    public int PendingDeletions => _deletionChannel.Reader.Count;

    /// <summary>
    /// Attempts to schedule a node for fallback deletion processing.
    /// </summary>
    /// <param name="node">The node to schedule for deletion.</param>
    /// <returns><c>true</c> if the node was successfully queued; <c>false</c> if the channel is closed.</returns>
    /// <remarks>
    /// This method is non-blocking and thread-safe. Nodes are processed asynchronously
    /// in the order they are queued.
    /// </remarks>
    public bool TrySchedule(ConcurrentPriorityQueue<TPriority, TElement>.SkipListNode node)
    {
        ArgumentNullException.ThrowIfNull(node, nameof(node));
        
        if (node.Type is not ConcurrentPriorityQueue<TPriority, TElement>.SkipListNode.NodeType.Data)
        {
            return false;
        }

        return _deletionChannel.Writer.TryWrite(node);
    }

    /// <summary>
    /// Processes queued deletion requests asynchronously.
    /// </summary>
    private async Task ProcessDeletionsAsync()
    {
        try
        {
            await foreach (var node in _deletionChannel.Reader.ReadAllAsync(_cancellationTokenSource.Token).ConfigureAwait(false))
            {
                await ProcessNodeDeletionWithRetry(node).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
            _observability?.LogEvent(LogLevel.Information, "Fallback deletion processor shutting down");
        }
        catch (Exception ex)
        {
            _observability?.LogEvent(LogLevel.Error, 
                "Unexpected error in fallback deletion processor: {Error}", ex.Message);
        }
    }

    /// <summary>
    /// Processes a single node deletion with retry logic.
    /// </summary>
    private async Task ProcessNodeDeletionWithRetry(ConcurrentPriorityQueue<TPriority, TElement>.SkipListNode node)
    {
        var retryCount = 0;
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        while (retryCount < _maxRetries && !_cancellationTokenSource.Token.IsCancellationRequested)
        {
            try
            {
                // Verify node is still marked for deletion
                if (!node.IsDeleted)
                {
                    _observability?.LogEvent(LogLevel.Warning, 
                        "Node scheduled for fallback deletion is no longer marked as deleted. Priority: {Priority}",
                        node.Priority);
                    return;
                }

                // Attempt physical removal
                _removeNodePhysically(node);
                
                stopwatch.Stop();
                _observability?.RecordOperation("FallbackDeletion", true, stopwatch.ElapsedMilliseconds,
                    new Dictionary<string, object> 
                    { 
                        ["RetryCount"] = retryCount,
                        ["Priority"] = node.Priority?.ToString() ?? "null"
                    });
                
                return;
            }
            catch (Exception ex)
            {
                retryCount++;
                
                if (retryCount >= _maxRetries)
                {
                    stopwatch.Stop();
                    _observability?.RecordOperation("FallbackDeletion", false, stopwatch.ElapsedMilliseconds,
                        new Dictionary<string, object> 
                        { 
                            ["RetryCount"] = retryCount,
                            ["FailureReason"] = "MaxRetriesExceeded"
                        });
                    
                    _observability?.LogEvent(LogLevel.Error, 
                        "Fallback deletion failed after {MaxRetries} retries for node with priority {Priority}: {Error}", 
                        _maxRetries, node.Priority, ex.Message);
                    return;
                }

                // Wait before retry with exponential backoff
                var delay = TimeSpan.FromMilliseconds(_retryDelay.TotalMilliseconds * Math.Pow(2, retryCount - 1));
                await Task.Delay(delay, _cancellationTokenSource.Token).ConfigureAwait(false);
                
                _observability?.LogEvent(LogLevel.Warning, 
                    "Retrying fallback deletion for node with priority {Priority}. Attempt {RetryCount}/{MaxRetries}",
                    node.Priority, retryCount + 1, _maxRetries);
            }
        }
    }

    /// <summary>
    /// Disposes the fallback deletion processor and ensures graceful shutdown.
    /// </summary>
    public void Dispose()
    {
        // Signal cancellation
        _cancellationTokenSource.Cancel();
        
        // Complete the channel to prevent new items
        _deletionChannel.Writer.TryComplete();
        
        // Wait for processing to complete with timeout
        try
        {
            // Suppress VSTHRD002 because this is in Dispose where we need synchronous wait
            #pragma warning disable VSTHRD002
            _processingTask.GetAwaiter().GetResult();
            #pragma warning restore VSTHRD002
        }
        catch (Exception ex)
        {
            _observability?.LogEvent(LogLevel.Error, 
                "Error during fallback processor shutdown: {Error}", ex.Message);
        }
        
        _cancellationTokenSource.Dispose();
    }

}