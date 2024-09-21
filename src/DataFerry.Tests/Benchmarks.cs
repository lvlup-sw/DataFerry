using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using DataFerry.Caches.Interfaces;
using DataFerry.Caches;
using DataFerry.Collections;
using DataFerry.Json;
using DataFerry.Properties;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace DataFerry.Tests
{
    [MemoryDiagnoser]
    [ShortRunJob(RuntimeMoniker.Net80)]
    public class Benchmarks
    {
        private IDistributedCache<MyObject> _cacheWithArrayPoolingSerializer;
        private IDistributedCache<MyObject> _cacheWithJsonSerializerWrapper;
        private string _key = "testKey";
        private MyObject _testData = new MyObject { /* ... initialize with some sample data */ };

        // Assuming you have these dependencies available or can mock them
        private IConnectionMultiplexer _redisConnection;
        private IFastMemCache<string, string> _memCache;
        private IOptions<CacheSettings> _cacheSettings;
        private ILogger _logger;

        [GlobalSetup]
        public void Setup()
        {
            // Initialize the serializers
            var arrayPoolingSerializer = new ArrayPoolingSerializer<MyObject>(new StackArrayPool<byte>());
            var jsonSerializerWrapper = new JsonSerializerWrapper<MyObject>());

            // Initialize your DistributedCache instances with the respective serializers and other dependencies
            _cacheWithArrayPoolingSerializer = new DistributedCache(
                _redisConnection, _memCache, arrayPoolingSerializer, _cacheSettings, _logger
            );
            _cacheWithJsonSerializerWrapper = new DistributedCache(
                _redisConnection, _memCache, jsonSerializerWrapper, _cacheSettings, _logger
            );

            // Pre-populate the cache with some test data
            _cacheWithArrayPoolingSerializer.SetInCacheAsync(_key, _testData, TimeSpan.FromMinutes(5)).Wait();
            _cacheWithJsonSerializerWrapper.SetInCacheAsync(_key, _testData, TimeSpan.FromMinutes(5)).Wait();
        }

        [Benchmark]
        public async Task GetFromCacheAsync_ArrayPoolingSerializer()
        {
            await _cacheWithArrayPoolingSerializer.GetFromCacheAsync(_key);
        }

        [Benchmark]
        public async Task GetFromCacheAsync_JsonSerializerWrapper()
        {
            await _cacheWithJsonSerializerWrapper.GetFromCacheAsync(_key);
        }
    }
}
