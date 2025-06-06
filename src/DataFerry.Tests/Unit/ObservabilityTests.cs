// ===========================================================================
// <copyright file="ObservabilityTests.cs" company="Level Up Software">
// Copyright (c) Level Up Software. All rights reserved.
// </copyright>
// ===========================================================================

using System.Diagnostics;
using System.Diagnostics.Metrics;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using lvlup.DataFerry.Concurrency;
using lvlup.DataFerry.Concurrency.Contracts;
using lvlup.DataFerry.Concurrency.Observability;
using lvlup.DataFerry.Orchestrators.Contracts;

namespace lvlup.DataFerry.Tests.Unit;

/// <summary>
/// Unit tests for observability features in ConcurrentPriorityQueue.
/// </summary>
[TestClass]
public class ObservabilityTests
{
    private IServiceProvider _serviceProvider = null!;
    private ITaskOrchestrator _taskOrchestrator = null!;
    private TestMeterFactory _meterFactory = null!;
    private TestLoggerFactory _loggerFactory = null!;

    [TestInitialize]
    public void Setup()
    {
        var services = new ServiceCollection();
        
        _meterFactory = new TestMeterFactory();
        _loggerFactory = new TestLoggerFactory();
        _taskOrchestrator = new TestTaskOrchestrator();
        
        services.AddSingleton<ILoggerFactory>(_loggerFactory);
        services.AddSingleton<IMeterFactory>(_meterFactory);
        
        _serviceProvider = services.BuildServiceProvider();
    }

    [TestMethod]
    public void TestObservability_RecordsMetricsCorrectly()
    {
        // Arrange
        var options = new ConcurrentPriorityQueueOptions
        {
            QueueName = "TestQueue",
            MaxSize = 100
        };
        
        var queue = new ConcurrentPriorityQueue<int, string>(
            _taskOrchestrator,
            Comparer<int>.Default,
            _serviceProvider,
            options);

        // Act
        var addSuccess = queue.TryAdd(1, "one");
        var deleteSuccess = queue.TryDeleteMin(out var element);
        
        // Assert
        Assert.IsTrue(addSuccess);
        Assert.IsTrue(deleteSuccess);
        Assert.AreEqual("one", element);
        
        // Verify metrics were recorded
        var addCounter = _meterFactory.GetCounter("queue.operations.count");
        Assert.IsNotNull(addCounter);
        Assert.AreEqual(2, addCounter.TotalCount); // One add, one delete
        
        var histogram = _meterFactory.GetHistogram("queue.operation.duration");
        Assert.IsNotNull(histogram);
        Assert.IsTrue(histogram.RecordedValues.Count > 0);
    }

    [TestMethod]
    public void TestStatistics_TracksOperationCounts()
    {
        // Arrange
        var observability = new QueueObservability(
            _loggerFactory,
            _meterFactory,
            "TestQueue",
            100);
            
        var statistics = observability as IQueueStatistics;

        // Act
        observability.RecordOperation("TryAdd", true, 10.5);
        observability.RecordOperation("TryAdd", true, 12.3);
        observability.RecordOperation("TryAdd", false, 15.0);
        observability.RecordOperation("TryDeleteMin", true, 8.7);

        // Assert
        Assert.AreEqual(4, statistics.TotalOperations);
        Assert.IsTrue(statistics.AverageOperationTime > 0);
        
        var counts = statistics.OperationCounts;
        Assert.AreEqual(3, counts["TryAdd"]);
        Assert.AreEqual(1, counts["TryDeleteMin"]);
    }

    [TestMethod]
    public void TestActivity_PropagatesContext()
    {
        // Arrange
        var activityListener = new ActivityListener
        {
            ShouldListenTo = source => source.Name.Contains("DataFerry"),
            Sample = (ref ActivityCreationOptions<ActivityContext> options) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStarted = activity => { /* Capture for assertions */ }
        };
        
        ActivitySource.AddActivityListener(activityListener);
        
        var observability = new QueueObservability(
            _loggerFactory,
            _meterFactory,
            "TestQueue",
            100);

        // Act
        Activity? capturedActivity = null;
        activityListener.ActivityStarted = activity => capturedActivity = activity;
        
        using (var activity = observability.StartActivity("TestOperation"))
        {
            Assert.IsNotNull(activity);
            activity.AddTag("test", "value");
        }

        // Assert
        Assert.IsNotNull(capturedActivity);
        Assert.AreEqual("DataFerry.ConcurrentPriorityQueue.TestQueue", capturedActivity.Source.Name);
        Assert.IsTrue(capturedActivity.Tags.Any(t => t.Key == "queue.name" && t.Value == "TestQueue"));
    }

    [TestMethod]
    public void TestMetrics_HandlesHighThroughput()
    {
        // Arrange
        var metrics = new QueueMetrics(
            _meterFactory,
            "HighThroughputQueue",
            () => 50,
            100);
            
        var tasks = new List<Task>();
        var operationCount = 10000;

        // Act
        for (int i = 0; i < operationCount; i++)
        {
            var index = i;
            tasks.Add(Task.Run(() =>
            {
                var latency = Random.Shared.Next(1, 20);
                if (index % 10 == 0)
                {
                    metrics.RecordFailure("TryAdd", latency, "QueueFull");
                }
                else
                {
                    metrics.RecordSuccess("TryAdd", latency);
                }
                
                if (index % 100 == 0)
                {
                    metrics.RecordContention("TryAdd", "lock_wait", Random.Shared.Next(5, 50));
                }
            }));
        }
        
        Task.WaitAll(tasks.ToArray());

        // Assert
        var addCounter = _meterFactory.GetCounter("dataferry.queue.add.count");
        Assert.IsNotNull(addCounter);
        Assert.IsTrue(addCounter.TotalCount > 0);
        
        var failureCounter = _meterFactory.GetCounter("dataferry.queue.failures.count");
        Assert.IsNotNull(failureCounter);
        Assert.AreEqual(operationCount / 10, (int)failureCounter.TotalCount);
        
        var contentionCounter = _meterFactory.GetCounter("dataferry.queue.contention.count");
        Assert.IsNotNull(contentionCounter);
        Assert.IsTrue(contentionCounter.TotalCount > 0);
    }

    [TestMethod]
    public void TestStatistics_WindowedAnalysis()
    {
        // Arrange
        var stats = new QueueStatistics("TestQueue", TimeSpan.FromMinutes(5));
        var now = DateTime.UtcNow;

        // Act - Record operations over time
        for (int i = 0; i < 100; i++)
        {
            var timestamp = now.AddSeconds(-i);
            stats.RecordOperation("TryAdd", success: i % 5 != 0, latencyMs: 10 + i % 20, timestamp);
        }

        // Get statistics for different windows
        var lastMinute = stats.GetWindowedStatistics("TryAdd", TimeSpan.FromMinutes(1));
        var last5Minutes = stats.GetWindowedStatistics("TryAdd", TimeSpan.FromMinutes(5));
        var allOps = stats.GetWindowedStatistics(null, TimeSpan.FromMinutes(5));

        // Assert
        Assert.IsTrue(lastMinute.Count < last5Minutes.Count);
        Assert.AreEqual(100, allOps.Count);
        Assert.IsTrue(lastMinute.SuccessRate > 0 && lastMinute.SuccessRate < 1);
        Assert.IsTrue(lastMinute.AverageLatency > 0);
        Assert.IsTrue(lastMinute.LatencyP99 >= lastMinute.LatencyP50);
    }

    [TestMethod]
    public void TestObservability_IntegrationWithQueue()
    {
        // Arrange
        var options = new ConcurrentPriorityQueueOptions
        {
            QueueName = "IntegrationTestQueue",
            MaxSize = 10,
            EnableStatistics = true,
            EnableTracing = true
        };
        
        var queue = new ConcurrentPriorityQueue<int, string>(
            _taskOrchestrator,
            Comparer<int>.Default,
            _serviceProvider,
            options);

        // Act - Perform various operations
        for (int i = 0; i < 15; i++)
        {
            queue.TryAdd(i, $"item{i}");
        }
        
        for (int i = 0; i < 5; i++)
        {
            queue.TryDeleteMin(out _);
        }
        
        queue.TryDelete(7);
        
        // Assert - Check logs
        var logs = _loggerFactory.GetLogs();
        Assert.IsTrue(logs.Any(l => l.Contains("Queue initialized")));
        Assert.IsTrue(logs.Any(l => l.Contains("MaxSize exceeded")));
        
        // Check metrics
        var sizeGauge = _meterFactory.GetObservableGauge<double>("queue.utilization.ratio");
        Assert.IsNotNull(sizeGauge);
    }

    #region Test Infrastructure

    private class TestMeterFactory : IMeterFactory
    {
        private readonly Dictionary<string, TestMeter> _meters = new();

        public Meter Create(MeterOptions options)
        {
            var meter = new TestMeter(options.Name ?? "TestMeter");
            _meters[meter.Name] = meter;
            return meter;
        }

        public TestCounter<T>? GetCounter<T>(string name) where T : struct
        {
            return _meters.Values
                .SelectMany(m => m.Counters)
                .OfType<TestCounter<T>>()
                .FirstOrDefault(c => c.Name.Contains(name));
        }

        public TestHistogram<T>? GetHistogram<T>(string name) where T : struct
        {
            return _meters.Values
                .SelectMany(m => m.Histograms)
                .OfType<TestHistogram<T>>()
                .FirstOrDefault(h => h.Name.Contains(name));
        }

        public TestObservableGauge<T>? GetObservableGauge<T>(string name) where T : struct
        {
            return _meters.Values
                .SelectMany(m => m.ObservableGauges)
                .OfType<TestObservableGauge<T>>()
                .FirstOrDefault(g => g.Name.Contains(name));
        }

        public void Dispose() { }
    }

    private class TestMeter : Meter
    {
        public List<object> Counters { get; } = new();
        public List<object> Histograms { get; } = new();
        public List<object> ObservableGauges { get; } = new();

        public TestMeter(string name) : base(name) { }

        public override Counter<T> CreateCounter<T>(string name, string? unit = null, string? description = null)
        {
            var counter = new TestCounter<T>(Name, name);
            Counters.Add(counter);
            return counter;
        }

        public override Histogram<T> CreateHistogram<T>(string name, string? unit = null, string? description = null)
        {
            var histogram = new TestHistogram<T>(Name, name);
            Histograms.Add(histogram);
            return histogram;
        }

        public override ObservableGauge<T> CreateObservableGauge<T>(string name, Func<T> observeValue, string? unit = null, string? description = null)
        {
            var gauge = new TestObservableGauge<T>(Name, name, observeValue);
            ObservableGauges.Add(gauge);
            return gauge;
        }
    }

    private class TestCounter<T> : Counter<T> where T : struct
    {
        public long TotalCount { get; private set; }
        
        internal TestCounter(string meterName, string instrumentName) 
            : base(new TestMeter(meterName), instrumentName, null, null) { }

        public override void Add(T delta, params KeyValuePair<string, object?>[] tags)
        {
            TotalCount += Convert.ToInt64(delta);
        }
    }

    private class TestHistogram<T> : Histogram<T> where T : struct
    {
        public List<T> RecordedValues { get; } = new();
        
        internal TestHistogram(string meterName, string instrumentName) 
            : base(new TestMeter(meterName), instrumentName, null, null) { }

        public override void Record(T value, params KeyValuePair<string, object?>[] tags)
        {
            RecordedValues.Add(value);
        }
    }

    private class TestObservableGauge<T> : ObservableGauge<T> where T : struct
    {
        private readonly Func<T> _observeValue;
        
        internal TestObservableGauge(string meterName, string instrumentName, Func<T> observeValue) 
            : base(new TestMeter(meterName), instrumentName, null, null)
        {
            _observeValue = observeValue;
        }
        
        public T GetValue() => _observeValue();
    }

    private class TestLoggerFactory : ILoggerFactory
    {
        private readonly List<string> _logs = new();
        
        public void AddProvider(ILoggerProvider provider) { }
        
        public ILogger CreateLogger(string categoryName) => new TestLogger(_logs);
        
        public List<string> GetLogs() => _logs.ToList();
        
        public void Dispose() { }
    }

    private class TestLogger : ILogger
    {
        private readonly List<string> _logs;
        
        public TestLogger(List<string> logs) => _logs = logs;
        
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        
        public bool IsEnabled(LogLevel logLevel) => true;
        
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            _logs.Add($"[{logLevel}] {formatter(state, exception)}");
        }
    }

    private class TestTaskOrchestrator : ITaskOrchestrator
    {
        private long _queuedCount;
        
        public long QueuedCount => _queuedCount;
        
        public void Run(Func<Task> action)
        {
            Interlocked.Increment(ref _queuedCount);
            Task.Run(async () => await action());
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

    #endregion
}