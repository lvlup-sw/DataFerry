using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using lvlup.DataFerry.Tests.TestModels;
using Microsoft.Extensions.Caching.Memory;
using BitFaster.Caching.Lfu;
using lvlup.DataFerry.Caches;
using BenchmarkDotNet.Configs;

namespace lvlup.DataFerry.Tests
{
    public class MemCacheBenchmark
    {
        private const int CacheSize = 1000;
        private readonly List<Payload> _users;
        private readonly IMemoryCache _memoryCache;
        private readonly ConcurrentLfu<string, Payload> _bflfu;
        private readonly LfuMemCache<string, Payload> _lfu;

        public MemCacheBenchmark()
        {
            _users = GenerateUsers(CacheSize);
            _memoryCache = new MemoryCache(new MemoryCacheOptions() { SizeLimit = CacheSize });
            _bflfu = new ConcurrentLfu<string, Payload>(CacheSize);
            _lfu = new LfuMemCache<string, Payload>(CacheSize);
        }

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

        [Benchmark]
        public void DataFerry()
        {
            foreach (var user in _users)
            {
                _lfu.AddOrUpdate(user.Identifier, user, TimeSpan.FromMinutes(60));
            }

            int hits = 0;
            var random = new Random();
            for (int i = 0; i < 10000; i++)
            {
                var keyIndex = random.Next(CacheSize * 2);
                var key = $"user{keyIndex}";
                if (_lfu.TryGet(key, out var _))
                {
                    hits++;
                }
            }

            double hitRate = (double)hits / 10000 * 100;
            Console.WriteLine($"DataFerry hit rate: {hitRate:F2}%");
        }

        [Benchmark]
        public void BitFaster()
        {
            foreach (var user in _users)
            {
                _bflfu.AddOrUpdate(user.Identifier, user);
            }

            int hits = 0;
            var random = new Random();
            for (int i = 0; i < 10000; i++)
            {
                var keyIndex = random.Next(CacheSize * 2);
                var key = $"user{keyIndex}";
                if (_bflfu.TryGet(key, out var _))
                {
                    hits++;
                }
            }

            double hitRate = (double)hits / 10000 * 100;
            Console.WriteLine($"BitFaster hit rate: {hitRate:F2}%");
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

        public static void Main(string[] args)
        {
            var summary = BenchmarkRunner.Run<MemCacheBenchmark>(
                    ManualConfig.Create(DefaultConfig.Instance)
                        .WithOptions(ConfigOptions.DisableOptimizationsValidator)
                );
        }
    }
}