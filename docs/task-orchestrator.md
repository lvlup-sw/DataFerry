# TaskOrchestrator: A Resilient Background Task Processor

## 1. Overview

The `TaskOrchestrator` is a high-performance, resilient background task processor. Its primary role is to decouple foreground operations from background work, ensuring that application threads are not blocked by tasks like cleanup, logging, or other asynchronous processing.

It is a required dependency for components like the `ConcurrentPriorityQueue`, which uses it to offload the physical deletion of nodes.

### Core Features

-   **Channel-Based:** Built on `System.Threading.Channels` for a highly efficient, allocation-friendly producer/consumer queue.
-   **Resilient by Design:** Integrates with the **Polly** library to automatically apply resilience policies (e.g., retry, timeout, circuit breaker) to the execution of background tasks.
-   **Configurable Concurrency:** Allows configuration of the number of concurrent worker threads and the maximum size of the work queue (backlog).
-   **Graceful Shutdown:** Provides a `StopAsync` method to ensure that all queued work is completed before the application exits.
-   **Backpressure Handling:** Can be configured to either block or drop tasks when the work queue is full, allowing developers to choose the appropriate strategy for their workload.

---

## 2. Architecture

The `TaskOrchestrator` consists of three main parts:

1.  **The Channel (`System.Threading.Channels.Channel<Func<Task>>`)**: This is the central queue. Producers (like the `ConcurrentPriorityQueue`) add work items (`Func<Task>`) to the channel's writer.
2.  **The Workers**: A configurable number of long-running `Task`s that act as consumers. Each worker continuously reads from the channel's reader in an asynchronous loop (`await foreach`).
3.  **The Execution Policy (`Polly.IAsyncPolicy`)**: When a worker dequeues a work item, it does not execute it directly. Instead, it executes it through a Polly policy. This wraps the task in resilience logic, automatically handling transient errors as configured.

```
+------------------+      +--------------------------------+      +----------------------+
|    Producer      |      |        TaskOrchestrator        |      |   Background Task    |
| (e.g., CPQ)      |----->| Channel<Func<Task>> (Backlog)  |----->| (Polly-wrapped)      |
+------------------+      +--------------------------------+      +----------------------+
                             |         ^
                             |         | Dequeue
                             |         |
                        +----v----+----+----+
                        | Worker 1| Worker 2| ...
                        +---------+---------+
```

---

## 3. Configuration and Behavior

The orchestrator's behavior is controlled at construction time.

### Key Parameters

-   `workerCount`: The number of concurrent tasks that will process items from the queue. Defaults to `2`.
-   `maxBacklog`: The maximum number of work items that can be waiting in the queue. Defaults to `128`.

### `TaskOrchestratorFeatures`

This `[Flags]` enum controls how the `Run` (synchronous enqueue) method behaves when the backlog is full:

-   **`BlockOnFullQueue` (Default):** This feature is not explicitly defined but is the default behavior when `AllowSyncTaskDrop` is not set. In reality, the synchronous `Run` method will throw an `InvalidOperationException` if the queue is full and dropping is disallowed. The asynchronous `RunAsync` method will always wait for space to become available.
-   **`AllowSyncTaskDrop`**: If the queue is full, the synchronous `Run` method will silently drop the work item and log a warning. This is a useful strategy for non-critical background work where dropping a task is preferable to slowing down or crashing the producer.

### Polly Integration

The `TaskOrchestrator` accepts a Polly `IAsyncPolicy` to wrap the execution of every task. This allows for powerful, centralized error handling. The library provides a default policy generator that can create policies for:
-   **Retry:** Automatically retries a failed task.
-   **Timeout:** Enforces a timeout on task execution.
-   **Exponential Backoff:** Increases the wait time between retries.

This is essential for the `ConcurrentPriorityQueue`, as it ensures that the background physical deletion tasks are robust against transient database or network errors if the element being stored has external dependencies.

---

## 4. Usage and Lifecycle

### Enqueuing Work

-   **`Run(Func<Task> action)`**: Synchronously attempts to enqueue a work item. Its behavior on a full queue is determined by the `TaskOrchestratorFeatures`.
-   **`RunAsync(Func<Task> action, CancellationToken ct)`**: Asynchronously enqueues a work item. If the queue is full, it will wait (block asynchronously) until space is available.

### Shutdown

-   **`StopAsync(CancellationToken ct)`**: Initiates a graceful shutdown. It first completes the channel, preventing any new items from being added. It then waits for the worker tasks to process all remaining items in the queue. A timeout is used to prevent the shutdown from hanging indefinitely.
-   **`DisposeAsync()`**: Calls `StopAsync` and releases all resources, including the internal `CancellationTokenSource`. It is essential to dispose of the orchestrator to ensure workers are stopped correctly.
