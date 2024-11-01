/*
using lvlup.DataFerry.Buffers;
using lvlup.DataFerry.Orchestrators.Abstractions;

namespace lvlup.DataFerry.Orchestrators
{
    public sealed class TaskOrchestrator : ITaskOrchestrator, IDisposable
    {
        /// <summary>
        /// The maximum number of work items to store.
        /// </summary>
        public const int MaxBacklog = 16;

        private int count;
        private readonly CancellationTokenSource cts = new();
        private readonly SemaphoreSlim semaphore = new(0, MaxBacklog);
        private readonly ConcurrentSkipList<Action> work = new(MaxBacklog);
        readonly TaskCompletionSource<bool> completion = new();

        /// <summary>
        /// Initializes a new instance of the BackgroundThreadScheduler class.
        /// </summary>
        public BackgroundThreadScheduler()
        {
            // dedicated thread
            _ = Task.Factory.StartNew(async () => await Background().ConfigureAwait(false), cts.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        ///<inheritdoc/>
        public Task Completion => completion.Task;

        ///<inheritdoc/>
        public bool IsBackground => true;

        ///<inheritdoc/>
        public long RunCount => count;


        ///<inheritdoc/>
        public void Run(Action action)
        {
            if (work.TryAdd(action) == BufferStatus.Success)
            {
                semaphore.Release();
                count++;
            }
        }

        private async Task Background()
        {
            var spinner = new SpinWait();

            while (true)
            {
                try
                {
                    await semaphore.WaitAsync(cts.Token).ConfigureAwait(false);

                    BufferStatus s;
                    do
                    {
                        s = work.TryTake(out var action);

                        if (s == BufferStatus.Success)
                        {
                            action!();
                        }
                        else
                        {
                            spinner.SpinOnce();
                        }
                    }
                    while (s == BufferStatus.Contended);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    this.lastException = new Optional<Exception>(ex);
                }

                spinner.SpinOnce();
            }

            completion.SetResult(true);
        }

        /// <summary>
        /// Terminate the background thread.
        /// </summary>
        public void Dispose()
        {
            // prevent hang when cancel runs on the same thread
            this.cts.CancelAfter(TimeSpan.Zero);
        }
    }
}
*/