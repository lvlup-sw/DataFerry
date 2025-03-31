// ===========================================================================
// <copyright file="TaskOrchestrator.cs" company="Level Up Software">
// Copyright (c) Level Up Software. All rights reserved.
// </copyright>
// ===========================================================================

using System.Data.Common;
using System.Threading.Channels;
using lvlup.DataFerry.Orchestrators.Contracts;
using lvlup.DataFerry.Properties;
using lvlup.DataFerry.Utilities;

using Microsoft.Extensions.Logging;
using Polly;

namespace lvlup.DataFerry.Orchestrators;

/// <summary>
/// A background task scheduler using System.Threading.Channels and Polly for resilience.
/// </summary>
public sealed class TaskOrchestrator : ITaskOrchestrator
{
    // Defaults
    private const int DefaultMaxBacklog = 128;
    private const int DefaultWorkerCount = 2;
    private static readonly TimeSpan DefaultShutdownTimeout = TimeSpan.FromSeconds(30);

    // Dependencies
    private readonly Channel<Func<Task>> _workChannel;
    private readonly CancellationTokenSource _internalCts = new();
    private readonly Task[] _workerTasks;
    private readonly ILogger<TaskOrchestrator> _logger;
    private readonly ISyncPolicy _syncEnqueuePolicy;
    private readonly IAsyncPolicy _asyncExecutionPolicy;
    private readonly TaskOrchestratorFeatures _features;

    // Variables
    private readonly int _maxBacklog;
    private long _queuedCount;
    private volatile bool _stopCalled;
    private Task? _shutdownTask;

    /// <summary>
    /// Feature flags for the task orchestrator.
    /// </summary>
    [Flags]
    public enum TaskOrchestratorFeatures
    {
        /// <summary>
        /// Wait for space if the queue is full when adding tasks.
        /// </summary>
        BlockOnFullQueue = 0,

        /// <summary>
        /// Drop the task if the queue is full when adding tasks using the synchronous Run method. WriteAsync will still wait.
        /// </summary>
        AllowSyncTaskDrop = 1,
    }

    /// <summary>
    /// Gets default resiliency settings for the TaskOrchestrator's background task policy.
    /// Uses Basic pattern, reasonable retries, backoff, and timeout.
    /// </summary>
    private static ResiliencySettings GetDefaultResiliencySetting()
    {
        return new ResiliencySettings
        {
            DesiredPolicy = ResiliencyPatterns.Basic,
            RetryCount = 3,
            RetryIntervalSeconds = 2,
            UseExponentialBackoff = true,
            TimeoutIntervalSeconds = 60,
        };
    }

    /// <summary>
    /// Gets the default predicate function to identify transient exceptions for retries.
    /// </summary>
    private static Func<Exception, bool> GetDefaultExceptionPredicate()
    {
        // Handle common transient errors
        return ex => ex is TimeoutException or HttpRequestException or IOException or DbException;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TaskOrchestrator"/> class with default Polly policies.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="features">Feature flags to control behavior.</param>
    /// <param name="workerCount">The number of concurrent worker tasks.</param>
    /// <param name="maxBacklog">The maximum number of items to hold in the queue.</param>
    public TaskOrchestrator(
        ILogger<TaskOrchestrator> logger,
        TaskOrchestratorFeatures features = TaskOrchestratorFeatures.BlockOnFullQueue,
        int workerCount = DefaultWorkerCount,
        int maxBacklog = DefaultMaxBacklog)
        : this(
            logger,
            Policy.NoOp(),
            PollyPolicyGenerator.GetAsyncBackgroundTaskPattern(
                logger,
                GetDefaultResiliencySetting(),
                GetDefaultExceptionPredicate()),
            features,
            workerCount,
            maxBacklog)
    {
        _logger.LogInformation("TaskOrchestrator constructed with default Polly policies.");
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TaskOrchestrator"/> class with provided Polly policies.
    /// </summary>
    /// <param name="logger">The logger instance.</param>
    /// <param name="syncEnqueuePolicy">Synchronous Polly policy for the Run method's queuing operation.</param>
    /// <param name="asyncExecutionPolicy">Asynchronous Polly policy applied to task execution in workers.</param>
    /// <param name="features">Feature flags to control behavior.</param>
    /// <param name="workerCount">The number of concurrent worker tasks.</param>
    /// <param name="maxBacklog">The maximum number of items to hold in the queue.</param>
    public TaskOrchestrator(
        ILogger<TaskOrchestrator> logger,
        ISyncPolicy syncEnqueuePolicy,
        IAsyncPolicy asyncExecutionPolicy,
        TaskOrchestratorFeatures features = TaskOrchestratorFeatures.BlockOnFullQueue,
        int workerCount = DefaultWorkerCount,
        int maxBacklog = DefaultMaxBacklog)
    {
        ArgumentNullException.ThrowIfNull(logger, nameof(logger));
        ArgumentNullException.ThrowIfNull(syncEnqueuePolicy, nameof(syncEnqueuePolicy));
        ArgumentNullException.ThrowIfNull(asyncExecutionPolicy, nameof(asyncExecutionPolicy));
        if (workerCount <= 0) throw new ArgumentOutOfRangeException(nameof(workerCount), "Worker count must be positive.");
        if (maxBacklog <= 0) throw new ArgumentOutOfRangeException(nameof(maxBacklog), "Maximum backlog must be positive.");

        _logger = logger;
        _syncEnqueuePolicy = syncEnqueuePolicy;
        _asyncExecutionPolicy = asyncExecutionPolicy;
        _features = features;
        _maxBacklog = maxBacklog;

        var channelOptions = new BoundedChannelOptions(_maxBacklog)
        {
            // Use Wait for async RunAsync; Sync Run handles AllowSyncTaskDrop logic explicitly
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = workerCount == 1,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        };
        _workChannel = Channel.CreateBounded<Func<Task>>(channelOptions);

        _workerTasks = Enumerable.Range(0, workerCount)
            .Select(i => Task.Factory.StartNew(
                () => WorkerAsync($"Worker-{i}", _internalCts.Token),
                _internalCts.Token,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default)
            .Unwrap())
            .ToArray();

        _logger.LogInformation(
            "TaskOrchestrator started (Workers: {WorkerCount}, Backlog: {MaxBacklog}). Policies (Sync: {SyncPolicyType}, Async: {AsyncPolicyType})",
            workerCount,
            _maxBacklog,
            _syncEnqueuePolicy.GetType().Name,
            _asyncExecutionPolicy.GetType().Name);
    }

    /// <inheritdoc/>
    public long QueuedCount => Interlocked.Read(ref _queuedCount);

    /// <inheritdoc/>
    public void Run(Func<Task> action)
    {
        ArgumentNullException.ThrowIfNull(action, nameof(action));
        ObjectDisposedException.ThrowIf(_internalCts.IsCancellationRequested, this);
        if (_stopCalled) throw new InvalidOperationException("Orchestrator is shutting down and cannot accept new work.");

        try
        {
            _syncEnqueuePolicy.Execute(
                _ =>
            {
                if (_workChannel.Writer.TryWrite(action))
                {
                    Interlocked.Increment(ref _queuedCount);
                }
                else
                {
                    // TryWrite failed; channel is full
                    if (!_features.HasFlag(TaskOrchestratorFeatures.AllowSyncTaskDrop))
                    {
                        // If dropping isn't allowed, throw; policy might handle this.
                        _logger.LogError("Failed to write to the task backlog (queue full) and dropping is disallowed.");
                        throw new InvalidOperationException("Failed to write to the task backlog (queue full).");
                    }

                    // Dropping is allowed for sync Run; log but do not increment count
                    _logger.LogWarning("Failed to write to the task backlog (queue full). Dropping task.");
                }
            }, CreatePollyContext($"{nameof(Run)}"));
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            _logger.LogError(ex.GetBaseException(), "Error occurred applying policy while trying to enqueue action synchronously.");
        }
    }

    /// <inheritdoc/>
    public async ValueTask RunAsync(Func<Task> action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(action, nameof(action));
        ObjectDisposedException.ThrowIf(_internalCts.IsCancellationRequested, this);
        if (_stopCalled) throw new InvalidOperationException("Orchestrator is shutting down and cannot accept new work.");

        try
        {
            // WriteAsync waits if the channel is full (due to BoundedChannelFullMode.Wait)
            await _workChannel.Writer.WriteAsync(action, cancellationToken).ConfigureAwait(false);
            Interlocked.Increment(ref _queuedCount);
        }
        catch (ChannelClosedException ex)
        {
            _logger.LogWarning(ex, "Failed to write to the task backlog asynchronously (channel closed). Orchestrator might be shutting down.");
            throw new ObjectDisposedException("Cannot enqueue work, the orchestrator is shutting down or disposed.", ex);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Asynchronous enqueue operation was canceled by the caller.");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex.GetBaseException(), "Unexpected error occurred while trying to enqueue action asynchronously.");
            throw;
        }
    }

    /// <summary>
    /// Worker task which continuously processes work items from the channel.
    /// Each worker runs this method until the orchestrator is stopped or disposed.
    /// </summary>
    /// <param name="workerId">A unique identifier for this worker instance, primarily used for logging.</param>
    /// <param name="cancellationToken">A token that signals when the orchestrator is shutting down, allowing the worker to exit gracefully.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous lifetime of the worker loop.</returns>
    internal async Task WorkerAsync(string workerId, CancellationToken cancellationToken)
    {
        // Todo: consider adding a dead-letter queue for error handling
        _logger.LogDebug("[{WorkerId}] Worker started.", workerId);

        try
        {
            // ReadAllAsync completes when the channel is marked Complete() and empty
            await foreach (var action in _workChannel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    // Apply the execution policy to the action
                    await _asyncExecutionPolicy.ExecuteAsync(
                        (_, _) => action(),
                        CreatePollyContext($"ExecuteAction-{workerId}"),
                        cancellationToken)
                    .ConfigureAwait(false);
                }
                catch (OperationCanceledException ex) when (cancellationToken.IsCancellationRequested)
                {
                    _logger.LogWarning(ex, "[{WorkerId}] Action execution canceled by orchestrator shutdown.", workerId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex.GetBaseException(), "[{WorkerId}] Error occurred executing background action.", workerId);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Expected when StopAsync/DisposeAsync calls _internalCts.Cancel()
            _logger.LogDebug("[{WorkerId}] Worker received cancellation signal and is stopping.", workerId);
        }
        catch (Exception ex)
        {
            // Unexpected error in the worker loop itself (e.g., reading from channel)
            // This worker will terminate, but other workers might continue
            _logger.LogCritical(ex.GetBaseException(), "[{WorkerId}] Unhandled error occurred in worker loop. Worker terminating.", workerId);
        }
        finally
        {
            _logger.LogDebug("[{WorkerId}] Worker exiting.", workerId);
        }
    }

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        // Prevent multiple calls to StopAsync/DisposeAsync from interfering
        if (!_stopCalled)
        {
            // Signal that shutdown has started
            _stopCalled = true;
            _logger.LogInformation("StopAsync called. Initiating shutdown...");

            // Complete the channel writer. This prevents new items via Run/RunAsync (after check)
            // and signals workers using ReadAllAsync to complete once the queue is empty
            _workChannel.Writer.Complete();

            // Create the shutdown task only once
            _shutdownTask = PerformShutdownAsync(cancellationToken);
        }
        else
        {
             _logger.LogDebug("StopAsync called again or after DisposeAsync. Returning existing shutdown task.");
        }

        // Return the task representing the shutdown process
        // If called multiple times, returns the same task instance
        return _shutdownTask ?? Task.CompletedTask;
    }

    private async Task PerformShutdownAsync(CancellationToken externalCancellationToken)
    {
        // Create a linked token source: combines internal cancellation (Dispose)
        // with the external token provided to StopAsync
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(_internalCts.Token, externalCancellationToken);
        var effectiveToken = linkedCts.Token;

        try
        {
            // Wait for all worker tasks to complete
            var allWorkersCompleteTask = Task.WhenAll(_workerTasks);

            // Create a task representing the timeout.
            var timeoutTask = Task.Delay(DefaultShutdownTimeout, effectiveToken);

            // Wait for either all workers to complete or the timeout/cancellation occurs
            var completedTask = await Task.WhenAny(allWorkersCompleteTask, timeoutTask).ConfigureAwait(false);

            if (completedTask == timeoutTask || effectiveToken.IsCancellationRequested)
            {
                 _logger.LogWarning("Shutdown timed out ({Timeout}ms) or was cancelled. Attempting forceful worker termination.", DefaultShutdownTimeout.TotalMilliseconds);

                 // Cancel the internal token source FIRST to signal workers directly
                 await _internalCts.CancelAsync().ConfigureAwait(false);

                 // Now re-throw if cancellation was requested externally
                 externalCancellationToken.ThrowIfCancellationRequested();
            }
            else
            {
                 _logger.LogInformation("All worker tasks completed gracefully.");

                 // Propagate potential exceptions from worker tasks if needed (e.g., critical failures)
                 await allWorkersCompleteTask.ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (externalCancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Shutdown process was canceled by the external cancellation token.");

            // Ensure internal cancellation is also signaled
            await _internalCts.CancelAsync().ConfigureAwait(false);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex.GetBaseException(), "Error occurred during TaskOrchestrator shutdown waiting for workers.");
            await _internalCts.CancelAsync().ConfigureAwait(false);
        }
        finally
        {
             // Ensure internal token is cancelled if PerformShutdownAsync completes,
             // regardless of how it exited, unless already disposed
            if (!_internalCts.IsCancellationRequested)
            {
                try
                {
                    await _internalCts.CancelAsync().ConfigureAwait(false);
                }
                catch (ObjectDisposedException)
                {
                    // We've already disposed the object; there is nothing else we need to do
                }
            }

            _logger.LogInformation("Shutdown process complete.");
        }
    }

    /// <summary>
    /// Asynchronously stops the orchestrator and releases resources.
    /// </summary>
    /// <returns>A task representing the asynchronous disposal operation.</returns>
    public async ValueTask DisposeAsync()
    {
        // Ensure StopAsync logic runs, handles completing the writer, etc.
        // We use a CancellationToken.None here as DisposeAsync shouldn't typically be cancellable itself
        await StopAsync(CancellationToken.None).ConfigureAwait(false);

        try
        {
             _internalCts.Dispose();
        }
        catch (Exception ex)
        {
             _logger.LogError(ex.GetBaseException(), "Error disposing internal CancellationTokenSource.");
        }

        _logger.LogInformation("TaskOrchestrator disposed.");
    }

    /// <summary>
    /// Creates a Polly Context including a unique operation ID and relevant orchestrator state.
    /// </summary>
    /// <param name="operationName">Name of the operation (e.g., "Run", "ExecuteAction").</param>
    /// <returns>A Polly Context dictionary.</returns>
    private Context CreatePollyContext(string operationName)
    {
        // Prepare context data
        var contextData = new Dictionary<string, object>
        {
            { "Orchestrator.MaxBacklog", this._maxBacklog },
            { "Orchestrator.WorkerCount", this._workerTasks.Length },
        };

        // Create the context with a unique key including the operation name and a Guid
        return new Context($"{operationName}-{Guid.NewGuid()}", contextData);
    }
}