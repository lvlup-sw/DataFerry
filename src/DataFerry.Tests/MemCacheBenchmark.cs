using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using lvlup.DataFerry.Tests.TestModels;
using Microsoft.Extensions.Caching.Memory;
using BitFaster.Caching.Lfu;
using lvlup.DataFerry.Caches;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Toolchains.InProcess.Emit;

namespace lvlup.DataFerry.Tests
{
    [ShortRunJob]
    [MemoryDiagnoser]
    public class MemCacheBenchmark
    {
        private const int CacheSize = 1000;
        private readonly List<Payload> _users;
        private readonly IMemoryCache _memoryCache;
        private readonly ConcurrentLfu<string, Payload> _bitfaster;
        private readonly LfuMemCache<string, Payload> _dataferry;
        private readonly BitfasterMemCache<string, Payload> _bfdataferry;
        private readonly TtlMemCache<string, Payload> _fastcache;

        public MemCacheBenchmark()
        {
            _users = GenerateUsers(CacheSize);
            _memoryCache = new MemoryCache(new MemoryCacheOptions() { SizeLimit = CacheSize });
            _bitfaster = new(CacheSize);
            _dataferry = new(CacheSize);
            _bfdataferry = new(CacheSize);
            _fastcache = new(CacheSize);
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
            _ = BenchmarkRunner.Run<MemCacheBenchmark>();
        }

        private static List<Payload> GenerateUsers(int count)
        {
            var users = new List<Payload>(count);
            for (int i = 1; i <= count; i++)
            {
                users.Add(new Payload { Identifier = $"user{i}", Data = TestUtils.GenerateRandomString(100) });
            }
            return users;
        }

        /*
        [Benchmark(OperationsPerInvoke = 1000)]
        public void DataFerryThroughput()
        {
            var random = new Random();
            for (int i = 0; i < 1000; i++)
            {
                var keyIndex = random.Next(CacheSize * 2);
                var key = $"user{keyIndex}";
                _dataferry.TryGet(key, out var _);
            }
        }
        */

        /*
        [Benchmark]
        public void MemoryCache()
        {
            foreach (var user in _users)
            {
                _memoryCache.Set(user.Identifier, user, new MemoryCacheEntryOptions { Size = 1 });
            }

            int hits = 0;
            var random = new Random();
            for (int i = 0; i < 10000; i++)
            {
                var keyIndex = random.Next(CacheSize * 2);
                var key = $"user{keyIndex}";
                if (_memoryCache.TryGetValue(key, out var _))
                {
                    hits++;
                }
            }

            double hitRate = (double)hits / 10000 * 100;
            Console.WriteLine($"MemoryCache hit rate: {hitRate:F2}%");
        }
        */

        [Benchmark]
        public void DataFerry()
        {
            foreach (var user in _users)
            {
                _dataferry.AddOrUpdate(user.Identifier, user, TimeSpan.FromMinutes(60));
            }

            var random = new Random();
            for (int i = 0; i < 10000; i++)
            {
                var keyIndex = random.Next(CacheSize * 2);
                var key = $"user{keyIndex}";
                _dataferry.TryGet(key, out var _);
            }
        }

        /*
        [Benchmark]
        public void BfDataFerry()
        {
            foreach (var user in _users)
            {
                _bfdataferry.AddOrUpdate(user.Identifier, user, TimeSpan.FromMinutes(60));
            }

            var random = new Random();
            for (int i = 0; i < 10000; i++)
            {
                var keyIndex = random.Next(CacheSize * 2);
                var key = $"user{keyIndex}";
                _bfdataferry.TryGet(key, out var _);
            }
        }
        */

        /*
        [Benchmark]
        public void BitFaster()
        {
            foreach (var user in _users)
            {
                _bitfaster.AddOrUpdate(user.Identifier, user);
            }

            var random = new Random();
            for (int i = 0; i < 10000; i++)
            {
                var keyIndex = random.Next(CacheSize * 2);
                var key = $"user{keyIndex}";
                _bitfaster.TryGet(key, out var _);
            }
        }
        */
    }
}