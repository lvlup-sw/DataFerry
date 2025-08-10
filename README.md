# DataFerry: A High-Performance Concurrent Priority Queue for .NET

DataFerry is a .NET library focused on providing high-performance, thread-safe data structures for demanding concurrent programming scenarios.

The primary component is the `ConcurrentPriorityQueue<TPriority, TElement>`.

## `ConcurrentPriorityQueue<TPriority, TElement>`

This is a state-of-the-art concurrent priority queue engineered for high throughput and low latency in multi-threaded environments. It is based on the Lotan-Shavit algorithm for lock-based SkipLists.

### Key Features

-   **High-Throughput, Low-Latency Design:** Built on a lock-based **SkipList**, providing expected O(log n) performance for all major operations.
-   **Fine-Grained Concurrency:** Employs node-level locking instead of collection-wide locks, maximizing parallelism by allowing multiple threads to operate on the queue simultaneously.
-   **Advanced Contention Management:** Implements the **SprayList** algorithm for near-minimum deletions. This distributes write pressure away from the head of the queue, a common bottleneck, significantly improving performance under contention.
-   **Optimized Memory Usage:** Aggressively minimizes GC pressure by using `ObjectPool` for node recycling and `ArrayPool` for internal data structures, making it suitable for high-throughput scenarios.
-   **Non-Blocking Operations:** Features logical deletion, where nodes are marked for deletion instantly. The actual memory reclamation is offloaded to a background `TaskOrchestrator`, preventing application threads from being blocked by cleanup work.
-   **Lock-Free Reads:** `ContainsPriority`, `GetCount`, and enumeration are lock-free, providing fast and non-intrusive observation of the queue's state.

For a complete technical breakdown, please see the **[ConcurrentPriorityQueue Deep Dive](docs/concurrent-priority-queue.md)**.

## Getting Started

1.  **Install the NuGet package:**

    ```bash
    Install-Package lvlup.DataFerry
    ```

2.  **Register Components with Dependency Injection:**

    In your `Program.cs` or `Startup.cs`, register the `TaskOrchestrator` and `ConcurrentPriorityQueue`. The queue can be registered as a singleton to be shared across your application.

    ```csharp
    using lvlup.DataFerry.Concurrency;
    using lvlup.DataFerry.Concurrency.Contracts;
    using Microsoft.Extensions.DependencyInjection;
    using Microsoft.Extensions.DependencyInjection.Extensions;
    using System.Diagnostics.Metrics;

    public static void ConfigureServices(IServiceCollection services)
    {
        // Add the required TaskOrchestrator as a singleton
        services.AddSingleton<ITaskOrchestrator, TaskOrchestrator>();

        // Add support for metrics
        services.TryAddSingleton<MeterFactory>();

        // Register the ConcurrentPriorityQueue as a singleton instance
        // Here, we register a queue of <int, string>
        services.AddSingleton<IConcurrentPriorityQueue<int, string>>(provider =>
        {
            // You can configure options here or via the IOptions pattern
            var options = new ConcurrentPriorityQueueOptions();

            return new ConcurrentPriorityQueue<int, string>(
                provider.GetRequiredService<ITaskOrchestrator>(),
                Comparer<int>.Default,
                provider.GetRequiredService<ILoggerFactory>(),
                provider.GetRequiredService<MeterFactory>(),
                Microsoft.Extensions.Options.Options.Create(options)
            );
        });
    }
    ```

3.  **Inject and Use the Queue:**

    Inject `IConcurrentPriorityQueue<TPriority, TElement>` into your services and use it.

    ```csharp
    public class MyService
    {
        private readonly IConcurrentPriorityQueue<int, string> _queue;

        public MyService(IConcurrentPriorityQueue<int, string> queue)
        {
            _queue = queue;
        }

        public void ProcessHighPriorityJob(string job)
        {
            // Add a job with priority 1 (higher priority)
            _queue.TryAdd(priority: 1, element: job);
        }

        public string? GetNextJob()
        {
            // Dequeue the highest-priority item
            if (_queue.TryDeleteAbsoluteMin(out string? element))
            {
                return element;
            }
            return null;
        }
    }
    ```

## Roadmap

-   **v1.0:** Initial release of `ConcurrentPriorityQueue` and the supporting `TaskOrchestrator`.
-   **v1.x:**
    -   Develop `LfuMemCache`, a high-performance, in-memory cache.
    -   Integrate `ConcurrentPriorityQueue` as a core component for managing cache eviction strategies (e.g., tracking item priority based on frequency or recency).
-   **v2.x:**
    -   Introduce a `DistributedCache` client optimized for low-allocation communication with Redis.
    -   Explore strategies for coupling `LfuMemCache` and `DistributedCache` for tiered caching.

## Contributing

Contributions are welcome. Please open an issue or submit a pull request to discuss changes.

## License

This project is licensed under the **Apache License 2.0**. See the [LICENSE](LICENSE) file for details.
