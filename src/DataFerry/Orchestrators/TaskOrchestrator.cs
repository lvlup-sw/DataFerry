using lvlup.DataFerry.Orchestrators.Contracts;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Wrap;
using System.Threading.Channels;

namespace lvlup.DataFerry.Orchestrators
{
    public sealed class TaskOrchestrator : ITaskOrchestrator, IDisposable
    {
        /// <summary>
        /// The maximum number of work items to store.
        /// </summary>
        public const int MaxBacklog = 32;

        // Backers
        private readonly Channel<Action> _workChannel = Channel.CreateBounded<Action>(MaxBacklog);
        private readonly CancellationTokenSource _cts = new();
        private readonly Task[] _workerTasks;
        private Exception? _lastException;

        // Inputs
        private readonly ILogger<TaskOrchestrator> _logger;
        private readonly PolicyWrap<object> _retryPolicy;
        private long _runCount;

        /// <summary>
        /// Initializes a new instance of the TaskOrchestrator class.
        /// </summary>
        public TaskOrchestrator(ILogger<TaskOrchestrator> logger, int workerCount = 2)
        {
            _logger = logger;
            _retryPolicy = GeneratePolicy();

            _workerTasks = Enumerable.Range(0, workerCount)
                .Select(_ => Task.Factory.StartNew(
                    () => Worker(_cts.Token),
                    _cts.Token,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default))
                .ToArray();
        }

        /// <summary>
        /// Completes all the existing queued tasks.
        /// </summary>
        public Task CompleteAll => Task.WhenAll(_workerTasks);

        /// <inheritdoc/>
        public bool IsBackground => true;

        /// <inheritdoc/>
        public long RunCount => _runCount;

        /// <inheritdoc/>
        public void Run(Action action)
        {
            _ = _retryPolicy.Execute((ctx) =>
            {
                if (!_workChannel.Writer.TryWrite(action))
                {
                    _lastException = new InvalidOperationException("Task backlog full.");
                    _logger.LogError(_lastException, _lastException.Message);
                    throw _lastException;
                };

                return Task.CompletedTask;
            }, new Context($"TaskOrchestrator.Run_{Environment.CurrentManagedThreadId}"));

            Interlocked.Increment(ref _runCount);
        }

        /// <summary>
        /// Worker task which continuously processes work items as they are published to the channel.
        /// </summary>
        /// <param name="cancellationToken"></param>
        /// <returns></returns>
        private async Task Worker(CancellationToken cancellationToken)
        {
            _logger.LogDebug("Worker thread started with ID: {ThreadId}", Environment.CurrentManagedThreadId);

            await foreach (var action in _workChannel.Reader.ReadAllAsync(cancellationToken))
            {
                try
                {
                    _ = _retryPolicy.Execute((ctx) =>
                    {
                        action();
                        return Task.CompletedTask;
                    }, new Context($"TaskOrchestrator.Worker_{Environment.CurrentManagedThreadId}"));
                }
                catch (Exception ex)
                {
                    _lastException = ex;
                    _logger.LogError(ex.GetBaseException(), "Error occurred in worker thread.");
                }
            }

            _logger.LogDebug("Worker thread exiting with ID: {ThreadId}", Environment.CurrentManagedThreadId);
        }

        /// <summary>
        /// Get the last recorded exception.
        /// </summary>
        public Exception? GetLastException() => _lastException;


        /// <summary>
        /// Generate a synchronous Polly retry policy.
        /// </summary>
        private static PolicyWrap<object> GeneratePolicy()
        {
            // First layer: timeouts
            var timeoutPolicy = Policy.Timeout(
                TimeSpan.FromMilliseconds(50));

            // Last resort: return default value
            var fallbackPolicy = Policy<object>
                .Handle<Exception>()
                .Fallback(
                    fallbackValue: string.Empty,
                    onFallback: (exception, context) => { });

            return fallbackPolicy.Wrap(timeoutPolicy);
        }

        /// <summary>
        /// Terminate the background thread.
        /// </summary>
        public void Dispose()
        {
            _cts.Cancel();
            _workChannel.Writer.Complete();
        }
    }
}
