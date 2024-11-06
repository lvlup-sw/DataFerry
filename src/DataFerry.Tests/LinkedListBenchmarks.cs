using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Toolchains.InProcess.Emit;
using lvlup.DataFerry.Collections;

namespace lvlup.DataFerry.Tests
{
    [ShortRunJob]
    [MemoryDiagnoser]
    public class LinkedListBenchmarks
    {
        private readonly ConcurrentLinkedList<int> _concurrentList;
        private readonly LinkedList<int> _linkedList;
        private readonly int[] _items;

        public LinkedListBenchmarks()
        {
            _concurrentList = new ConcurrentLinkedList<int>();
            _linkedList = new LinkedList<int>();
            _items = Enumerable.Range(0, 1000).ToArray();

            // Pre-populate the lists
            foreach (var item in _items)
            {
                _concurrentList.TryInsert(item);
                _linkedList.AddLast(item);
            }
        }

        [Config(typeof(Config))]
        public class Config : ManualConfig
        {
            public Config()
            {
                AddJob(Job.ShortRun.WithToolchain(InProcessEmitToolchain.Instance));
            }
        }

        public static void Main(string[] args)
        {
            // Uncomment to debug
            //_ = BenchmarkSwitcher.FromAssembly(typeof(MemCacheBenchmark).Assembly).Run(args, new DebugInProcessConfig());
            _ = BenchmarkRunner.Run<LinkedListBenchmarks>();
        }

        [Benchmark]
        public void ConcurrentInsert()
        {
            for (int i = 0; i < 100; i++)
            {
                _concurrentList.TryInsert(i);
            }
        }

        [Benchmark]
        public void LinkedListInsert()
        {
            for (int i = 0; i < 100; i++)
            {
                _linkedList.AddLast(i);
            }
        }

        [Benchmark]
        public void ConcurrentRemove()
        {
            for (int i = 0; i < 100; i++)
            {
                _concurrentList.TryRemove(i);
            }
        }

        [Benchmark]
        public void LinkedListRemove()
        {
            for (int i = 0; i < 100; i++)
            {
                _linkedList.Remove(i);
            }
        }

        [Benchmark]
        public void ConcurrentContains()
        {
            for (int i = 0; i < 1000; i++)
            {
                _concurrentList.Contains(i);
            }
        }

        [Benchmark]
        public void LinkedListContains()
        {
            for (int i = 0; i < 1000; i++)
            {
                _linkedList.Contains(i);
            }
        }
    }
}
