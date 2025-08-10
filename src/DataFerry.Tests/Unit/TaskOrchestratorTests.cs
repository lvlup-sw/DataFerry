using Microsoft.Extensions.Logging;
using Moq;
using Polly;
using lvlup.DataFerry.Concurrency;

namespace lvlup.DataFerry.Tests.Unit;

[TestClass]
public class TaskOrchestratorTests
{
    #pragma warning disable CS8618
    private Mock<ILogger<TaskOrchestrator>> _mockLogger;
    private ISyncPolicy _syncPolicy;
    private IAsyncPolicy _asyncPolicy;
    #pragma warning disable CS8618

    [TestInitialize]
    public void TestInitialize()
    {
        _mockLogger = new Mock<ILogger<TaskOrchestrator>>();
        _syncPolicy = Policy.NoOp();
        _asyncPolicy = Policy.NoOpAsync();
    }

    #region Test Helpers
    
    private void VerifyLog<TState>(LogLevel level, Times times, string? messageContains = null)
    {
        _mockLogger.Verify(
            x => x.Log(
                level,
                It.IsAny<EventId>(),
                It.Is<TState>(v => messageContains == null || v!.ToString()!.Contains(messageContains)),
                It.IsAny<Exception>(),
                It.IsAny<Func<TState, Exception?, string>>()),
            times);
    }

    #endregion
    #region Constructor Tests

    [TestMethod]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        // Arrange
        ISyncPolicy syncPolicy = Policy.NoOp();
        IAsyncPolicy asyncPolicy = Policy.NoOpAsync();

        // Act & Assert
        Assert.ThrowsException<ArgumentNullException>(() =>
            new TaskOrchestrator(null!, syncPolicy, asyncPolicy));
    }

    [TestMethod]
    public void Constructor_WithNullSyncPolicy_ShouldThrowArgumentNullException()
    {
        // Arrange
        IAsyncPolicy asyncPolicy = Policy.NoOpAsync();

        // Act & Assert
        Assert.ThrowsException<ArgumentNullException>(() =>
            new TaskOrchestrator(_mockLogger.Object, null!, asyncPolicy));
    }

    [TestMethod]
    public void Constructor_WithNullAsyncPolicy_ShouldThrowArgumentNullException()
    {
        // Arrange
        ISyncPolicy syncPolicy = Policy.NoOp();

        // Act & Assert
        Assert.ThrowsException<ArgumentNullException>(() =>
            new TaskOrchestrator(_mockLogger.Object, syncPolicy, null!));
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    public void Constructor_WithInvalidWorkerCount_ShouldThrowArgumentOutOfRangeException(int workerCount)
    {
        // Arrange
        ISyncPolicy syncPolicy = Policy.NoOp();
        IAsyncPolicy asyncPolicy = Policy.NoOpAsync();

        // Act & Assert
        Assert.ThrowsException<ArgumentOutOfRangeException>(() =>
            new TaskOrchestrator(_mockLogger.Object, syncPolicy, asyncPolicy, workerCount: workerCount));
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    public void Constructor_WithInvalidMaxBacklog_ShouldThrowArgumentOutOfRangeException(int maxBacklog)
    {
        // Arrange
        ISyncPolicy syncPolicy = Policy.NoOp();
        IAsyncPolicy asyncPolicy = Policy.NoOpAsync();

        // Act & Assert
        Assert.ThrowsException<ArgumentOutOfRangeException>(() =>
            new TaskOrchestrator(_mockLogger.Object, syncPolicy, asyncPolicy, maxBacklog: maxBacklog));
    }

    [TestMethod]
    public void Constructor_DefaultPolicies_ShouldConstructAndLog()
    {
        // Arrange
        int workerCount = 3;
        int maxBacklog = 50;

        // Act
        var orchestrator = new TaskOrchestrator(_mockLogger.Object, workerCount: workerCount, maxBacklog: maxBacklog);

        // Assert
        Assert.IsNotNull(orchestrator);
        Assert.AreEqual(0, orchestrator.QueuedCount);
        VerifyLog<object>(LogLevel.Information, Times.Once(), "TaskOrchestrator constructed with default Polly policies.");
        VerifyLog<object>(LogLevel.Information, Times.Once(), $"TaskOrchestrator started (Workers: {workerCount}, Backlog: {maxBacklog})");
    }

    [TestMethod]
    public void Constructor_CustomPolicies_ShouldConstructAndLog()
    {
        // Arrange
        int workerCount = 1;
        int maxBacklog = 10;
        var syncPolicy = Policy.Handle<Exception>().Retry(1);
        var asyncPolicy = Policy.Handle<Exception>().RetryAsync(2);

        // Act
        var orchestrator = new TaskOrchestrator(_mockLogger.Object, syncPolicy, asyncPolicy, workerCount: workerCount, maxBacklog: maxBacklog);

        // Assert
        Assert.IsNotNull(orchestrator);
        Assert.AreEqual(0, orchestrator.QueuedCount);
        VerifyLog<object>(LogLevel.Information, Times.Once(), $"TaskOrchestrator started (Workers: {workerCount}, Backlog: {maxBacklog}). Policies (Sync: {syncPolicy.GetType().Name}, Async: {asyncPolicy.GetType().Name})");
    }

    #endregion

    #region Run Tests

    [TestMethod]
    public async Task Run_NullAction_ShouldThrowArgumentNullException()
    {
        // Arrange
        await using var orchestrator = new TaskOrchestrator(_mockLogger.Object, _syncPolicy, _asyncPolicy);

        // Act & Assert
        Assert.ThrowsException<ArgumentNullException>(() => orchestrator.Run(null!));
    }

    [TestMethod]
    public async Task Run_AfterStop_ShouldThrowInvalidOperationException()
    {
        // Arrange
        await using var orchestrator = new TaskOrchestrator(_mockLogger.Object, _syncPolicy, _asyncPolicy);
        await orchestrator.StopAsync(); // Initiate stop

        // Act & Assert
        Assert.ThrowsException<InvalidOperationException>(() => orchestrator.Run(async () => await Task.Delay(1)), "Orchestrator is shutting down");
    }

     [TestMethod]
    public async Task Run_AfterDispose_ShouldThrowObjectDisposedException()
    {
        // Arrange
        var orchestrator = new TaskOrchestrator(_mockLogger.Object, _syncPolicy, _asyncPolicy);
        await orchestrator.DisposeAsync(); // Dispose fully

        // Act & Assert
        Assert.ThrowsException<ObjectDisposedException>(() => orchestrator.Run(async () => await Task.Delay(1)));
    }

    [TestMethod]
    public async Task Run_BlockOnFullQueue_SuccessfulEnqueue_ShouldIncrementCount()
    {
        // Arrange
        int maxBacklog = 1;
        var tcs = new TaskCompletionSource<bool>();
        await using var orchestrator = new TaskOrchestrator(_mockLogger.Object, _syncPolicy, _asyncPolicy,
            features: TaskOrchestrator.TaskOrchestratorFeatures.BlockOnFullQueue, maxBacklog: maxBacklog, workerCount: 1);

        // Act
        orchestrator.Run(async () => { await tcs.Task; }); // Fill the queue
        Assert.AreEqual(1, orchestrator.QueuedCount);

        // Let worker process the item
        tcs.SetResult(true);
        await Task.Delay(50); // Give worker time

        // Assert
        // QueuedCount is decremented implicitly by the worker consuming the item
        // We expect it to be 0 after the worker finishes.
        Assert.AreEqual(0, orchestrator.QueuedCount);
    }

    [TestMethod]
    public async Task Run_AllowSyncTaskDrop_QueueFull_ShouldLogWarningAndDrop()
    {
        // Arrange
        int maxBacklog = 1; // Tiny backlog
        var workerBlockTcs = new TaskCompletionSource(); // To keep the worker busy
        await using var orchestrator = new TaskOrchestrator(_mockLogger.Object, _syncPolicy, _asyncPolicy,
            features: TaskOrchestrator.TaskOrchestratorFeatures.AllowSyncTaskDrop, maxBacklog: maxBacklog, workerCount: 1);

        // Fill the queue
        orchestrator.Run(async () => await workerBlockTcs.Task);
        Assert.AreEqual(1, orchestrator.QueuedCount);

        // Act: Try to add another task synchronously when queue is full and dropping is allowed
        orchestrator.Run(async () => await Task.Delay(1)); // This should be dropped

        // Assert
        Assert.AreEqual(1, orchestrator.QueuedCount, "Count should not increase as task was dropped.");
        VerifyLog<object>(LogLevel.Warning, Times.Once(), "Failed to write to the task backlog (queue full). Dropping task.");

        workerBlockTcs.SetResult(); // Allow worker to finish
        await Task.Delay(50); // Give worker time
        Assert.AreEqual(0, orchestrator.QueuedCount);
    }

    [TestMethod]
    public async Task Run_BlockOnFullQueue_QueueFull_ShouldThrowInvalidOperationException()
    {
        // Arrange
        int maxBacklog = 1; // Tiny backlog
        var workerBlockTcs = new TaskCompletionSource(); // To keep the worker busy
        // Use a policy that doesn't retry/handle InvalidOperationException for this test
        var syncPolicy = Policy.NoOp();
         await using var orchestrator = new TaskOrchestrator(_mockLogger.Object, syncPolicy, _asyncPolicy,
            features: TaskOrchestrator.TaskOrchestratorFeatures.BlockOnFullQueue, maxBacklog: maxBacklog, workerCount: 1);

        // Fill the queue
        orchestrator.Run(async () => await workerBlockTcs.Task);
        Assert.AreEqual(1, orchestrator.QueuedCount);

        // Act & Assert: Try to add another task synchronously when queue is full and dropping is disallowed
        Assert.ThrowsException<InvalidOperationException>(() => orchestrator.Run(async () => await Task.Delay(1)), "Failed to write to the task backlog (queue full).");
        VerifyLog<object>(LogLevel.Error, Times.Once(), "Failed to write to the task backlog (queue full) and dropping is disallowed.");

        workerBlockTcs.SetResult(); // Allow worker to finish
        await Task.Delay(50);
    }

     [TestMethod]
    public async Task Run_PolicyHandlesException_ShouldLogAndNotThrow()
    {
        // Arrange
        var exceptionToThrow = new InvalidOperationException("Simulated enqueue failure");
        var handled = false;
        var policy = Policy.Handle<InvalidOperationException>()
                           .Retry(1, onRetry: (ex, i) => handled = true);

        await using var orchestrator = new TaskOrchestrator(_mockLogger.Object, policy, _asyncPolicy,
            features: TaskOrchestrator.TaskOrchestratorFeatures.BlockOnFullQueue, // Force throw scenario
            maxBacklog: 1, workerCount: 1);

         // Simulate TryWrite failing and throwing the exception
         // This requires a more complex setup or mocking the ChannelWriter, which is difficult.
         // Alternative: Test that Run *catches* exceptions thrown *by the policy itself* (not the enqueue logic).
         var policyThrowing = Policy.Handle<Exception>().CircuitBreaker(1, TimeSpan.FromSeconds(1));
         policyThrowing.Isolate(); // Open the circuit breaker

         await using var orchestratorPolicyThrowing = new TaskOrchestrator(_mockLogger.Object, policyThrowing, _asyncPolicy);

        // Act
        orchestratorPolicyThrowing.Run(() => Task.CompletedTask); // This will throw BrokenCircuitException inside Execute

        // Assert
        VerifyLog<object>(LogLevel.Error, Times.Once(), "Error occurred applying policy while trying to enqueue action synchronously.");
        // No exception should escape the Run method itself due to the catch block
    }


    #endregion

    #region RunAsync Tests

    [TestMethod]
    public async Task RunAsync_NullAction_ShouldThrowArgumentNullException()
    {
        // Arrange
        await using var orchestrator = new TaskOrchestrator(_mockLogger.Object, _syncPolicy, _asyncPolicy);

        // Act & Assert
        await Assert.ThrowsExceptionAsync<ArgumentNullException>(async () => await orchestrator.RunAsync(null!));
    }

     [TestMethod]
    public async Task RunAsync_AfterStop_ShouldThrowInvalidOperationException()
    {
        // Arrange
        await using var orchestrator = new TaskOrchestrator(_mockLogger.Object, _syncPolicy, _asyncPolicy);
        await orchestrator.StopAsync(); // Initiate stop

        // Act & Assert
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(async () => await orchestrator.RunAsync(async () => await Task.Delay(1)), "Orchestrator is shutting down");
    }

    [TestMethod]
    public async Task RunAsync_AfterDispose_ShouldThrowObjectDisposedException()
    {
        // Arrange
        var orchestrator = new TaskOrchestrator(_mockLogger.Object, _syncPolicy, _asyncPolicy);
        await orchestrator.DisposeAsync(); // Dispose fully

        // Act & Assert
        await Assert.ThrowsExceptionAsync<ObjectDisposedException>(async () => await orchestrator.RunAsync(async () => await Task.Delay(1)));
    }

    [TestMethod]
    public async Task RunAsync_SuccessfulEnqueue_ShouldIncrementCountAndWaitForWorker()
    {
        // Arrange
        int count = 0;
        var tcs = new TaskCompletionSource();
        await using var orchestrator = new TaskOrchestrator(_mockLogger.Object, _syncPolicy, _asyncPolicy, workerCount: 1);

        // Act
        await orchestrator.RunAsync(async () =>
        {
            Interlocked.Increment(ref count);
            await tcs.Task; // Wait for signal
        });
        Assert.AreEqual(1, orchestrator.QueuedCount);

        // Assert work hasn't completed yet
        Assert.AreEqual(0, count);

        // Allow worker to proceed
        tcs.SetResult();
        await Task.Delay(100); // Give worker time

        // Assert
        Assert.AreEqual(1, count, "Work item should have executed.");
        Assert.AreEqual(0, orchestrator.QueuedCount, "Queue should be empty after worker finishes.");
    }

    [TestMethod]
    public async Task RunAsync_EnqueueWhenFull_ShouldWait()
    {
        // Arrange
        int maxBacklog = 1;
        var workerBusyTcs = new TaskCompletionSource(); // Keep worker busy
        var secondTaskEnqueuedTcs = new TaskCompletionSource(); // Signal when second task is enqueued
        await using var orchestrator = new TaskOrchestrator(_mockLogger.Object, _syncPolicy, _asyncPolicy, maxBacklog: maxBacklog, workerCount: 1);

        // Fill the queue
        await orchestrator.RunAsync(async () => await workerBusyTcs.Task);
        Assert.AreEqual(1, orchestrator.QueuedCount);

        // Act: Enqueue second task, should block (wait)
        var runAsyncTask = orchestrator.RunAsync(async () => {
            await Task.Delay(1); // Dummy work
            secondTaskEnqueuedTcs.TrySetResult(); // Signal completion
        });

        // Assert: Second task shouldn't complete immediately
        await Task.Delay(50); // Give some time
        Assert.IsFalse(runAsyncTask.IsCompleted, "RunAsync should be waiting as the queue is full.");
        Assert.AreEqual(1, orchestrator.QueuedCount); // Still 1, as the second task hasn't been fully added yet

        // Allow worker to finish first task
        workerBusyTcs.SetResult();

        // Wait for the second task to complete its enqueue and execution
        await secondTaskEnqueuedTcs.Task.WaitAsync(TimeSpan.FromSeconds(1)); // Wait for signal
        await Task.Delay(50); // Give worker time for the second task

        // Assert: Second task completed, queue empty
        Assert.IsTrue(runAsyncTask.IsCompletedSuccessfully, "RunAsync task should complete successfully after space is available.");
        Assert.AreEqual(0, orchestrator.QueuedCount);
    }

     [TestMethod]
    public async Task RunAsync_CancelledByCaller_ShouldThrowOperationCanceledExceptionAndLog()
    {
        // Arrange
        await using var orchestrator = new TaskOrchestrator(_mockLogger.Object, _syncPolicy, _asyncPolicy, maxBacklog: 1, workerCount: 1);
        var cts = new CancellationTokenSource();
        var workerBusyTcs = new TaskCompletionSource();

        // Fill the queue
        await orchestrator.RunAsync(async () => await workerBusyTcs.Task);

        // Act: Start RunAsync but cancel it before space becomes available
        var runAsyncTask = orchestrator.RunAsync(async () => await Task.Delay(1), cts.Token);
        await Task.Delay(10); // Ensure task starts waiting
        cts.Cancel();

        // Assert
        await Assert.ThrowsExceptionAsync<OperationCanceledException>(() => runAsyncTask.AsTask());
        VerifyLog<object>(LogLevel.Warning, Times.Once(), "Asynchronous enqueue operation was canceled by the caller.");

        // Cleanup
        workerBusyTcs.SetResult();
    }

    [TestMethod]
    public async Task RunAsync_EnqueueAfterStopBegins_ShouldThrowObjectDisposedException()
    {
        // Arrange
        await using var orchestrator = new TaskOrchestrator(_mockLogger.Object, _syncPolicy, _asyncPolicy, workerCount: 1);
        var workerBusyTcs = new TaskCompletionSource();

        // Add a task to keep worker busy
        await orchestrator.RunAsync(async () => await workerBusyTcs.Task);

        // Act: Initiate stop, which completes the writer
        var stopTask = orchestrator.StopAsync();

        // Try to enqueue after writer is completed
        await Assert.ThrowsExceptionAsync<ObjectDisposedException>(async () => await orchestrator.RunAsync(async () => await Task.Delay(1)));

        // Cleanup
        workerBusyTcs.SetResult();
        await stopTask;
    }


    #endregion

    #region Worker Tests

    [TestMethod]
    public async Task WorkerAsync_ExecutesEnqueuedActions_WithPolicy()
    {
        // Arrange
        int executionCount = 0;
        var policyMock = new Mock<IAsyncPolicy>();
        var executedActions = new List<int>();
        var tcs1 = new TaskCompletionSource();
        var tcs2 = new TaskCompletionSource();


        policyMock.Setup(p => p.ExecuteAsync(It.IsAny<Func<Context, CancellationToken, Task>>(), It.IsAny<Context>(), It.IsAny<CancellationToken>()))
            .Returns<Func<Context, CancellationToken, Task>, Context, CancellationToken>(async (action, ctx, ct) =>
            {
                Interlocked.Increment(ref executionCount);
                await action(ctx, ct); // Execute the original action
            });

        await using var orchestrator = new TaskOrchestrator(_mockLogger.Object, _syncPolicy, policyMock.Object, workerCount: 1);

        // Act
        await orchestrator.RunAsync(async () => { executedActions.Add(1); await tcs1.Task; });
        await orchestrator.RunAsync(async () => { executedActions.Add(2); await tcs2.Task; });

        // Assert: Action 1 not yet fully executed by policy
        await Task.Delay(50); // Give worker time to pick up item 1
        policyMock.Verify(p => p.ExecuteAsync(It.IsAny<Func<Context, CancellationToken, Task>>(), It.IsAny<Context>(), It.IsAny<CancellationToken>()), Times.Exactly(1));
        Assert.AreEqual(0, executedActions.Count); // Action hasn't run yet inside policy execution lambda

        // Allow action 1 to complete
        tcs1.SetResult();
        await Task.Delay(100); // Give worker time to finish 1 and pick up 2

        // Assert: Action 1 completed, Action 2 picked up by policy
         policyMock.Verify(p => p.ExecuteAsync(It.IsAny<Func<Context, CancellationToken, Task>>(), It.IsAny<Context>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        Assert.AreEqual(1, executedActions.Count);
        CollectionAssert.AreEqual(new List<int> { 1 }, executedActions);

        // Allow action 2 to complete
        tcs2.SetResult();
        await Task.Delay(100); // Give worker time to finish 2

        // Assert: Action 2 completed
        Assert.AreEqual(2, executionCount);
        Assert.AreEqual(2, executedActions.Count);
        CollectionAssert.AreEqual(new List<int> { 1, 2 }, executedActions);
        Assert.AreEqual(0, orchestrator.QueuedCount);
    }

     [TestMethod]
    public async Task WorkerAsync_ActionThrowsException_ShouldBeCaughtByPolicyAndLogged()
    {
        // Arrange
        var exceptionToThrow = new InvalidOperationException("Test Action Exception");
        var policyHandled = false;
        var policy = Policy.Handle<InvalidOperationException>().RetryAsync(0, async (ex, i, ctx) => // No retries, just handle
        {
            policyHandled = true;
            await Task.CompletedTask;
        });
        var actionExecuted = false;

        await using var orchestrator = new TaskOrchestrator(_mockLogger.Object, _syncPolicy, policy, workerCount: 1);

        // Act
        await orchestrator.RunAsync(() =>
        {
            actionExecuted = true;
            throw exceptionToThrow;
        });

        await Task.Delay(100); // Give worker time

        // Assert
        Assert.IsTrue(actionExecuted, "Action should have been executed.");
        Assert.IsTrue(policyHandled, "Policy should have handled the exception.");
        // Polly logs the exception via its own mechanisms if configured,
        // TaskOrchestrator only logs if the exception *escapes* the policy.
         VerifyLog<object>(LogLevel.Error, Times.Never(), "[Worker-0] Error occurred executing background action.");
        Assert.AreEqual(0, orchestrator.QueuedCount);
    }

    [TestMethod]
    public async Task WorkerAsync_ActionThrowsUnhandledException_ShouldBeCaughtByWorkerAndLogged()
    {
        // Arrange
        var exceptionToThrow = new ApplicationException("Unhandled Test Exception"); // Assume policy doesn't handle this
        var actionExecuted = false;

        await using var orchestrator = new TaskOrchestrator(_mockLogger.Object, _syncPolicy, _asyncPolicy, workerCount: 1); // Default policy doesn't handle ApplicationException

        // Act
        await orchestrator.RunAsync(() =>
        {
            actionExecuted = true;
            throw exceptionToThrow;
        });

        await Task.Delay(100); // Give worker time

        // Assert
        Assert.IsTrue(actionExecuted, "Action should have been executed.");
        VerifyLog<object>(LogLevel.Error, Times.Once(), "[Worker-0] Error occurred executing background action.");
        Assert.AreEqual(0, orchestrator.QueuedCount);
    }

    [TestMethod]
    public async Task WorkerAsync_CancellationDuringActionExecution_ShouldLogWarning()
    {
        // Arrange
        var actionStartedTcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var actionCancelled = false;
        var policy = Policy.Handle<Exception>() // Example policy
                           .RetryAsync(0);      // No retries for simplicity

        // Use a real logger factory for potentially better async context handling if needed
        // var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
        // var logger = loggerFactory.CreateLogger<TaskOrchestrator>();
        var logger = _mockLogger.Object; // Using the mock as before

        await using var orchestrator = new TaskOrchestrator(logger, _syncPolicy, policy, workerCount: 1);
        var internalTokenField = typeof(TaskOrchestrator).GetField("_internalCts", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var internalCts = (CancellationTokenSource)internalTokenField!.GetValue(orchestrator)!;


        // Act
        // Corrected: Pass Func<Task> lambda
        await orchestrator.RunAsync(async () =>
        {
            actionStartedTcs.SetResult();
            try
            {
                // Use the orchestrator's internal token for the delay check,
                // simulating work that respects cancellation.
                await Task.Delay(Timeout.Infinite, internalCts.Token);
            }
            catch (OperationCanceledException)
            {
                actionCancelled = true;
                throw; // Re-throw so the worker loop handles it as cancelled
            }
        });

        await actionStartedTcs.Task; // Wait for action to start inside the worker
        await orchestrator.StopAsync(); // Initiate stop which cancels internal token

        // Assert
        // Give a moment for cancellation to propagate through the worker's loop and policy execution
        await Task.Delay(200);

        Assert.IsTrue(actionCancelled, "Action's Task.Delay should have been cancelled.");

        // Verify the worker logged the cancellation initiated by StopAsync/DisposeAsync
        // Note: Polly might also log cancellation depending on its setup.
        // We primarily care that the worker recognizes it as cancellation, not a general error.
        VerifyLog<object>(LogLevel.Warning, Times.AtLeastOnce(), "[Worker-0] Action execution canceled by orchestrator shutdown.");

        // Ensure it wasn't logged as an unexpected error by the worker's final catch block
         VerifyLog<object>(LogLevel.Error, Times.Never(), "[Worker-0] Error occurred executing background action.");
    }


    #endregion

    #region StopAsync / DisposeAsync Tests

    [TestMethod]
    public async Task StopAsync_CompletesWriterAndWaitsForWorkers()
    {
        // Arrange
        int workerCount = 2;
        int taskCount = 5;
        var tasksProcessed = 0;
        var taskTcsList = Enumerable.Range(0, taskCount).Select(_ => new TaskCompletionSource()).ToList();
        await using var orchestrator = new TaskOrchestrator(_mockLogger.Object, _syncPolicy, _asyncPolicy, workerCount: workerCount, maxBacklog: taskCount + 1);

        // Act: Enqueue tasks
        for (int i = 0; i < taskCount; i++)
        {
            int taskId = i; // Capture loop variable
            await orchestrator.RunAsync(async () =>
            {
                await taskTcsList[taskId].Task;
                Interlocked.Increment(ref tasksProcessed);
            });
        }
        Assert.AreEqual(taskCount, orchestrator.QueuedCount);

        // Initiate stop
        var stopTask = orchestrator.StopAsync();

        // Assert: Stop shouldn't complete yet, writer should be complete (no new tasks)
        await Task.Delay(50);
        Assert.IsFalse(stopTask.IsCompleted);
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => orchestrator.RunAsync(() => Task.CompletedTask).AsTask());

        // Allow workers to finish
        foreach (var tcs in taskTcsList)
        {
            tcs.SetResult();
        }

        // Wait for stop to complete
        await stopTask;

        // Assert
        Assert.AreEqual(taskCount, tasksProcessed);
        Assert.AreEqual(0, orchestrator.QueuedCount);
        VerifyLog<object>(LogLevel.Information, Times.Once(), "StopAsync called. Initiating shutdown...");
        VerifyLog<object>(LogLevel.Information, Times.Once(), "All worker tasks completed gracefully.");
        VerifyLog<object>(LogLevel.Information, Times.Once(), "Shutdown process complete.");
    }

    [TestMethod]
    public async Task StopAsync_CalledMultipleTimes_ReturnsSameTask()
    {
        // Arrange
        await using var orchestrator = new TaskOrchestrator(_mockLogger.Object, _syncPolicy, _asyncPolicy);
        var tcs = new TaskCompletionSource();
        await orchestrator.RunAsync(() => tcs.Task); // Keep worker busy

        // Act
        var stopTask1 = orchestrator.StopAsync();
        var stopTask2 = orchestrator.StopAsync();

        // Assert
        Assert.AreSame(stopTask1, stopTask2);
        VerifyLog<object>(LogLevel.Information, Times.Once(), "StopAsync called. Initiating shutdown...");
        VerifyLog<object>(LogLevel.Debug, Times.Once(), "StopAsync called again or after DisposeAsync. Returning existing shutdown task.");

        // Cleanup
        tcs.SetResult();
        await stopTask1;
    }

    [TestMethod]
    public async Task StopAsync_WithTimeout_LogsWarningAndCancelsInternalToken()
    {
        // Arrange
        var workerBusyTcs = new TaskCompletionSource(); // Task that never completes
        // Need to mock or adjust DefaultShutdownTimeout for testing, which isn't easy directly.
        // We'll simulate the timeout condition externally.
        // Instead of relying on internal timeout, we cancel externally after a short delay.
        var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200)); // External cancellation simulates timeout

        await using var orchestrator = new TaskOrchestrator(_mockLogger.Object, _syncPolicy, _asyncPolicy, workerCount: 1);
        await orchestrator.RunAsync(() => workerBusyTcs.Task); // Keep worker busy

        // Act & Assert
        await Assert.ThrowsExceptionAsync<OperationCanceledException>(() => orchestrator.StopAsync(cts.Token));

        // Assert Logs
        VerifyLog<object>(LogLevel.Information, Times.Once(), "StopAsync called. Initiating shutdown...");
        // Check for timeout/cancellation log (message varies slightly based on which token cancelled)
         _mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Shutdown timed out") || v.ToString()!.Contains("Shutdown process was canceled")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.AtLeastOnce()); // It might log timeout *and* external cancellation warning

        VerifyLog<object>(LogLevel.Warning, Times.Once(), "Shutdown process was canceled by the external cancellation token.");
        VerifyLog<object>(LogLevel.Information, Times.Once(), "Shutdown process complete.");

        // Cleanup - Although task is stuck, orchestrator should be stopped.
        // No need to complete workerBusyTcs as the test focuses on shutdown behavior during blockage.
    }


    [TestMethod]
    public async Task DisposeAsync_CallsStopAsyncAndDisposesCts()
    {
        // Arrange
        var tcs = new TaskCompletionSource();
        var orchestrator = new TaskOrchestrator(_mockLogger.Object, _syncPolicy, _asyncPolicy, workerCount: 1);
        await orchestrator.RunAsync(() => tcs.Task);

        // Act
        var disposeTask = orchestrator.DisposeAsync();
        Assert.IsFalse(disposeTask.IsCompleted); // Should wait for worker

        tcs.SetResult(); // Allow worker to finish
        await disposeTask; // Wait for dispose to complete

        // Assert
        VerifyLog<object>(LogLevel.Information, Times.Once(), "StopAsync called. Initiating shutdown...");
        VerifyLog<object>(LogLevel.Information, Times.Once(), "All worker tasks completed gracefully.");
        VerifyLog<object>(LogLevel.Information, Times.Once(), "Shutdown process complete.");
        VerifyLog<object>(LogLevel.Information, Times.Once(), "TaskOrchestrator disposed.");

        // Assert internal CTS is disposed - tricky to check directly, but further calls should fail
        Assert.ThrowsException<ObjectDisposedException>(() => orchestrator.Run(() => Task.CompletedTask));
    }

    #endregion

    #region QueuedCount Tests

    [TestMethod]
    public async Task QueuedCount_ReflectsEnqueuedAndDequeuedItems()
    {
        // Arrange
        int tasksToRun = 3;
        var workerSync = new SemaphoreSlim(0, tasksToRun); // Control worker execution
        await using var orchestrator = new TaskOrchestrator(_mockLogger.Object, _syncPolicy, _asyncPolicy, workerCount: 1);

        // Act & Assert: Enqueue tasks
        for (int i = 0; i < tasksToRun; i++)
        {
            await orchestrator.RunAsync(async () => await workerSync.WaitAsync());
            Assert.AreEqual(i + 1, orchestrator.QueuedCount, $"Count after enqueueing task {i+1}");
        }

        // Act & Assert: Dequeue tasks (by allowing workers to run)
        for (int i = 0; i < tasksToRun; i++)
        {
            long countBeforeRelease = orchestrator.QueuedCount;
            workerSync.Release(); // Let one worker finish
            await Task.Delay(50); // Give worker time
            Assert.AreEqual(countBeforeRelease - 1, orchestrator.QueuedCount, $"Count after releasing worker for task {i+1}");
        }

        Assert.AreEqual(0, orchestrator.QueuedCount, "Final count should be 0");
    }

    #endregion
}