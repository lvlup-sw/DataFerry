using lvlup.DataFerry.Orchestrators.Contracts;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Wrap;
using System.Threading.Channels;

namespace lvlup.DataFerry.Orchestrators;

public sealed class TaskOrchestrator : ITaskOrchestrator, IDisposable
{
    /// <summary>
    /// The maximum number of work items to store.
    /// </summary>
    private const int MaxBacklog = 32;

    // Backers
    private readonly Channel<Func<Task>> _workChannel;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task[] _workerTasks;
    private Exception? _lastException;

    // Inputs
    private readonly ILogger<TaskOrchestrator> _logger;
    private readonly PolicyWrap<object> _syncPolicy;
    private readonly AsyncPolicyWrap<object> _asyncPolicy;
    private long _runCount;
    
    // Policy settings
    private readonly CacheSettings _settings = new()
    {
        DesiredPolicy = ResiliencyPatterns.Basic
    };

    /// <summary>
    /// Feature flags for the task orchestrator.
    /// </summary>
    public TaskOrchestratorFeatures Features { get; set; }
    
    /// <summary>
    /// Initializes a new instance of the TaskOrchestrator class.
    /// </summary>
    public TaskOrchestrator(ILogger<TaskOrchestrator> logger, TaskOrchestratorFeatures features = TaskOrchestratorFeatures.None, int workerCount = 2)
    {
        _logger = logger;
        Features = features;
        _syncPolicy = PollyPolicyGenerator.GenerateSyncPolicy(_logger, _settings, string.Empty);
        _asyncPolicy = PollyPolicyGenerator.GenerateAsyncPolicy(_logger, _settings, string.Empty);

        _workChannel = Channel.CreateBounded<Func<Task>>(new BoundedChannelOptions(MaxBacklog)
        {
            FullMode = Features.HasFlag(TaskOrchestratorFeatures.AllowTaskDrop) 
                ? BoundedChannelFullMode.DropWrite 
                : BoundedChannelFullMode.Wait,
            SingleReader = workerCount == 1,
            SingleWriter = false
        });
        
        _workerTasks = Enumerable.Range(0, workerCount)
            .Select(_ => Task.Factory.StartNew(
                () => Worker(_cts.Token),
                _cts.Token,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default))
            .ToArray<Task>();
    }

    /// <summary>
    /// Completes all the existing queued tasks.
    /// </summary>
    /// <remarks>
    /// Usually unnecessary.
    /// </remarks>
    public Task CompleteAll => Task.WhenAll(_workerTasks);

    /// <inheritdoc/>
    public bool IsBackground => true;

    /// <inheritdoc/>
    public long RunCount => _runCount;

    /// <inheritdoc/>
    public void Run(Func<Task> action)
    {
        try
        {
            _syncPolicy.Execute(ctx =>
            {
                if (_workChannel.Writer.TryWrite(action))
                {
                    return Task.CompletedTask;
                }

                if (!Features.HasFlag(TaskOrchestratorFeatures.AllowTaskDrop))
                {
                    throw new InvalidOperationException("Failed to write to the task backlog.");
                }

                _logger.LogWarning("Failed to write to the task backlog. Dropping task.");
                return Task.CompletedTask;
            }, new Context($"TaskOrchestrator.Run_{Environment.CurrentManagedThreadId}"));

            Interlocked.Increment(ref _runCount);
        }
        catch (Exception ex)
        {
            _lastException = ex;
            _logger.LogError(ex, "Error occurred while trying to enqueue action.");
        }
    }

    /// <summary>
    /// Worker task which continuously processes work items as they are published to the channel.
    /// </summary>
    /// <param name="cancellationToken"></param>
    private async Task Worker(CancellationToken cancellationToken)
    {
        _logger.LogDebug("Worker thread started with ID: {ThreadId}", Environment.CurrentManagedThreadId);

        try
        {
            await foreach (var action in _workChannel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                await ExecuteActionAsync(action).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Worker thread received cancellation signal with ID: {ThreadId}", Environment.CurrentManagedThreadId);
        }
        catch (Exception ex)
        {
            _lastException = ex;
            _logger.LogError(ex.GetBaseException(), "Error occurred in worker thread.");
        }

        _logger.LogDebug("Worker thread exiting with ID: {ThreadId}", Environment.CurrentManagedThreadId);
    }
    
    private async Task ExecuteActionAsync(Func<Task> action)
    {
        await _asyncPolicy.ExecuteAsync((ctx) =>
        {
            action();
            return Task.CompletedTask as Task<object>;
        }, new Context($"TaskOrchestrator.Worker_{Environment.CurrentManagedThreadId}"));
    }

    /// <summary>
    /// Get the last recorded exception.
    /// </summary>
    public Exception? GetLastException() => _lastException;

    /// <summary>
    /// Terminate the background thread.
    /// </summary>
    public void Dispose()
    {
        _cts.Cancel();
        _workChannel.Writer.Complete();
        try
        {
            if (!CompleteAll.Wait(TimeSpan.FromSeconds(20)))
            {
                _logger.LogError("Timeout triggered while waiting for worker tasks to complete during TaskOrchestrator shutdown.");
            };
        }
        catch (AggregateException ex)
        {
            _lastException = ex;
            _logger.LogError(ex, "Error during TaskOrchestrator shutdown.");
        }
        finally
        {
            _cts.Dispose();
        }
    }
    
    [Flags]
    public enum TaskOrchestratorFeatures
    {
        None = 0,
        AllowTaskDrop = 1,
    }    
}