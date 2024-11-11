using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using lvlup.DataFerry.Collections;
using lvlup.DataFerry.Orchestrators;
using lvlup.DataFerry.Orchestrators.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;


namespace lvlup.DataFerry.Tests
{
    [ShortRunJob]
    [MemoryDiagnoser]
    public class ConcurrentPriorityQueueBenchmarks
    {
        private readonly ConcurrentPriorityQueue<int, string> _frequencyQueue;
        private readonly ITaskOrchestrator _taskOrchestrator;
        private readonly int[] _values;

        public ConcurrentPriorityQueueBenchmarks()
        {
            _taskOrchestrator = new TaskOrchestrator(
                LoggerFactory.Create(builder => builder.Services.AddLogging())
                             .CreateLogger<TaskOrchestrator>());

            _frequencyQueue = new(
                _taskOrchestrator,
                Comparer<int>.Create((x, y) => x.CompareTo(y)),
                maxSize: 1000);

            // /*
            // Enqueue the values into the queue
            _values = GetPriorities();
            foreach (var value in _values)
            {
                _frequencyQueue.TryAdd(value, TestUtils.GenerateRandomString(100));
            }
            // */
        }

        public static void Main(string[] args)
        {
            // Uncomment to debug
            //_ = BenchmarkSwitcher.FromAssembly(typeof(ConcurrentPriorityQueueBenchmarks).Assembly).Run(args, new DebugInProcessConfig());
            _ = BenchmarkRunner.Run<ConcurrentPriorityQueueBenchmarks>();
        }

        /*
        [Benchmark(OperationsPerInvoke = 100)]
        public void ConcurrentPriorityQueue_TryAdd()
        {
            // Enqueue the values into the queue
            foreach (var value in GetPriorities())
            {
                _frequencyQueue.TryAdd(value, TestUtils.GenerateRandomString(100));
            }
        }
        */

        [Benchmark(OperationsPerInvoke = 100)]
        public void ConcurrentPriorityQueue_TryRemoveMin()
        {
            var count = _frequencyQueue.GetCount();
            for (int i = 0; i < count; i++)
            {
                _frequencyQueue.TryDeleteMin(out _);
            }
        }

        [Benchmark(OperationsPerInvoke = 100)]
        public void ConcurrentPriorityQueue_TryRemoveItemWithPriority()
        {
            foreach (var value in _values)
            {
                _frequencyQueue.TryDelete(value);
            }
        }

        public static int[] GetPriorities()
        {
            // Generate a sequence of numbers with a distribution that favors lower numbers
            // but still includes a good spread of higher numbers
            return [.. Enumerable.Range(1, 1000)  // Adjust the exponent (1.5) to control the distribution
                .SelectMany(x => Enumerable.Repeat(x, (int)(1000 / Math.Pow(x, 1.5)))) 
                .OrderBy(x => Guid.NewGuid())];   // Shuffle the values randomly
        }
    }
}
