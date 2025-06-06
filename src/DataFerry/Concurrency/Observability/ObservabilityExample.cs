// ===========================================================================
// <copyright file="ObservabilityExample.cs" company="Level Up Software">
// Copyright (c) Level Up Software. All rights reserved.
// </copyright>
// ===========================================================================

using System.Diagnostics;
using System.Diagnostics.Metrics;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

using lvlup.DataFerry.Orchestrators.Contracts;

namespace lvlup.DataFerry.Concurrency.Observability;

/// <summary>
/// Demonstrates usage of observability features with ConcurrentPriorityQueue.
/// </summary>
/// <remarks>
/// This example shows how to:
/// - Configure observability through dependency injection
/// - Monitor queue operations with metrics
/// - Trace operations with distributed tracing
/// - Analyze performance with detailed statistics
/// </remarks>
public static class ObservabilityExample
{
    /// <summary>
    /// Demonstrates basic observability setup and usage.
    /// </summary>
    public static void RunExample()
    {
        // Setup dependency injection
        var services = new ServiceCollection();
        
        // Add logging
        services.AddLogging(builder =>
        {
            builder.AddSimpleConsole(options =>
            {
                options.SingleLine = true;
                options.TimestampFormat = "HH:mm:ss ";
            });
            builder.SetMinimumLevel(LogLevel.Debug);
        });
        
        // Add metrics
        services.AddSingleton<IMeterFactory, DefaultMeterFactory>();
        
        // Add task orchestrator (required for queue)
        services.AddSingleton<ITaskOrchestrator, SimpleTaskOrchestrator>();
        
        var serviceProvider = services.BuildServiceProvider();
        
        // Configure queue with observability
        var options = new ConcurrentPriorityQueueOptions
        {
            QueueName = "ExampleQueue",
            MaxSize = 1000,
            EnableStatistics = true,
            EnableTracing = true,
            EnableNodePooling = true,
            EnableAdaptiveSpray = true
        };
        
        // Create queue with full observability
        var queue = new ConcurrentPriorityQueue<int, string>(
            serviceProvider.GetRequiredService<ITaskOrchestrator>(),
            Comparer<int>.Default,
            serviceProvider,
            options);
            
        // Setup activity listener for tracing
        using var activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name.Contains("DataFerry"),
            Sample = (ref ActivityCreationOptions<ActivityContext> options) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = activity =>
            {
                Console.WriteLine($"Activity started: {activity.OperationName}");
                foreach (var tag in activity.Tags)
                {
                    Console.WriteLine($"  {tag.Key}: {tag.Value}");
                }
            },
            ActivityStopped = activity =>
            {
                Console.WriteLine($"Activity stopped: {activity.OperationName} (Duration: {activity.Duration.TotalMilliseconds}ms)");
            }
        };
        
        ActivitySource.AddActivityListener(activityListener);
        
        // Setup metrics listener
        using var meterListener = new MeterListener
        {
            InstrumentPublished = (instrument, listener) =>
            {
                if (instrument.Meter.Name.Contains("DataFerry"))
                {
                    listener.EnableMeasurementEvents(instrument);
                }
            }
        };
        
        meterListener.SetMeasurementEventCallback<long>((instrument, measurement, tags, state) =>
        {
            Console.WriteLine($"Metric: {instrument.Name} = {measurement}");
        });
        
        meterListener.SetMeasurementEventCallback<double>((instrument, measurement, tags, state) =>
        {
            Console.WriteLine($"Metric: {instrument.Name} = {measurement:F2}");
        });
        
        meterListener.Start();
        
        // Perform operations
        Console.WriteLine("\n=== Adding items to queue ===");
        for (int i = 0; i < 10; i++)
        {
            if (queue.TryAdd(i, $"Item {i}"))
            {
                Console.WriteLine($"Added: Priority={i}, Value=Item {i}");
            }
        }
        
        Console.WriteLine($"\nQueue count: {queue.GetCount()}");
        
        Console.WriteLine("\n=== Removing items from queue ===");
        for (int i = 0; i < 5; i++)
        {
            if (queue.TryDeleteMin(out var element))
            {
                Console.WriteLine($"Removed: {element}");
            }
        }
        
        Console.WriteLine($"\nQueue count after removals: {queue.GetCount()}");
        
        // Demonstrate high contention scenario
        Console.WriteLine("\n=== Simulating concurrent operations ===");
        var tasks = new List<Task>();
        
        // Multiple producers
        for (int i = 0; i < 3; i++)
        {
            var producerId = i;
            tasks.Add(Task.Run(() =>
            {
                for (int j = 0; j < 20; j++)
                {
                    var priority = producerId * 100 + j;
                    queue.TryAdd(priority, $"Producer{producerId}-Item{j}");
                    Thread.Sleep(10); // Simulate work
                }
            }));
        }
        
        // Multiple consumers
        for (int i = 0; i < 2; i++)
        {
            var consumerId = i;
            tasks.Add(Task.Run(() =>
            {
                for (int j = 0; j < 30; j++)
                {
                    if (queue.TryDeleteMin(out var element))
                    {
                        Console.WriteLine($"Consumer{consumerId} got: {element}");
                    }
                    Thread.Sleep(15); // Simulate processing
                }
            }));
        }
        
#pragma warning disable VSTHRD002 // Avoid problematic synchronous waits
        Task.WaitAll(tasks.ToArray());
#pragma warning restore VSTHRD002 // Avoid problematic synchronous waits
        
        Console.WriteLine($"\nFinal queue count: {queue.GetCount()}");
        
        // Cleanup
        queue.Dispose();
        serviceProvider.Dispose();
    }
    
    /// <summary>
    /// Simple task orchestrator implementation for the example.
    /// </summary>
    private class SimpleTaskOrchestrator : ITaskOrchestrator
    {
        private long _queuedCount;
        
        public long QueuedCount => _queuedCount;
        
        public void Run(Func<Task> action)
        {
            Interlocked.Increment(ref _queuedCount);
            Task.Run(async () => await action().ConfigureAwait(false));
        }
        
        public ValueTask RunAsync(Func<Task> action, CancellationToken cancellationToken = default)
        {
            Run(action);
            return ValueTask.CompletedTask;
        }
        
        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
        
        public ValueTask DisposeAsync()
        {
            return ValueTask.CompletedTask;
        }
    }
    
    /// <summary>
    /// Default meter factory implementation using System.Diagnostics.Metrics.
    /// </summary>
    private class DefaultMeterFactory : IMeterFactory
    {
        private readonly List<Meter> _meters = new();
        
        public Meter Create(MeterOptions options)
        {
            var meter = new Meter(options.Name ?? "DefaultMeter", options.Version);
            _meters.Add(meter);
            return meter;
        }
        
        public void Dispose()
        {
            foreach (var meter in _meters)
            {
                meter.Dispose();
            }
        }
    }
}