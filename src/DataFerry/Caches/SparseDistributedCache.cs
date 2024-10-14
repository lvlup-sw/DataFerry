using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Wrap;
using StackExchange.Redis;
using System.Buffers;
using static System.Runtime.InteropServices.JavaScript.JSType;
using System.Text.Json;

namespace lvlup.DataFerry.Caches
{
    public class SparseDistributedCache : ISparseDistributedCache
    {
        private readonly IConnectionMultiplexer _cache;
        private readonly IFastMemCache<string, byte[]> _memCache;
        private readonly StackArrayPool<byte> _arrayPool;
        private readonly IDataFerrySerializer _serializer;
        private readonly CacheSettings _settings;
        private readonly ILogger<SparseDistributedCache> _logger;
        private AsyncPolicyWrap<object> _policy;

        /// <summary>
        /// The primary constructor for the <see cref="SparseDistributedCache"/> class.
        /// </summary>
        /// <param name="settings">The settings for the cache.</param>
        /// <exception cref="ArgumentNullException"></exception>""
        public SparseDistributedCache(
            IConnectionMultiplexer cache, 
            IFastMemCache<string, byte[]> memCache, 
            StackArrayPool<byte> arrayPool, 
            IDataFerrySerializer serializer, 
            IOptions<CacheSettings> settings, 
            ILogger<SparseDistributedCache> logger)
        {
            ArgumentNullException.ThrowIfNull(cache);
            ArgumentNullException.ThrowIfNull(memCache);
            ArgumentNullException.ThrowIfNull(arrayPool);
            ArgumentNullException.ThrowIfNull(serializer);
            ArgumentNullException.ThrowIfNull(settings);
            ArgumentNullException.ThrowIfNull(logger);

            _cache = cache;
            _memCache = memCache;
            _arrayPool = arrayPool;
            _serializer = serializer;
            _settings = settings.Value;
            _logger = logger;
            _policy = PollyPolicyGenerator.GeneratePolicy(_logger, _settings);
        }

        /// <summary>
        /// Get the configured Polly policy.
        /// </summary>
        public AsyncPolicyWrap<object> GetPollyPolicy() => _policy;

        /// <summary>
        /// Set the fallback value for the polly retry policy.
        /// </summary>
        /// <remarks>Policy will return <see cref="RedisValue.Null"/> if not set.</remarks>
        /// <param name="value"></param>
        public void SetFallbackValue(object value) => _policy = PollyPolicyGenerator.GeneratePolicy(_logger, _settings, value);

        /// <summary>
        /// 
        /// </summary>
        /// <param name="key"></param>
        /// <param name="destination"></param>
        /// <returns></returns>
        public bool GetFromCache(string key, IBufferWriter<byte> destination)
        {
            // Check the _memCache first
            if (TryGetFromMemoryCache(key, out byte[]? data)
                && data is not null)
            {
                // Success path; deserialize and return
                _logger.LogDebug("Retrieved data with key {key} from memory cache.", key);
                var sequence = new ReadOnlySequence<byte>(data);

                destination.Write(
                    _serializer.Deserialize<byte[]>(sequence)
                );

                return true;
            }

            // If the key does not exist in the _memCache, get a database connection
            IDatabase database = _cache.GetDatabase();

            // We have an async policy but make sure it works 
            // synchronously by adding GetAwaiter and GetResult.
            object result = _policy.ExecuteAsync(async (context) =>
            {
                _logger.LogDebug("Attempting to retrieve entry with key {key} from cache.", key);
                RedisValue data = await database.StringGetAsync(key, CommandFlags.PreferReplica)
                    .ConfigureAwait(false);

                return data.HasValue ? data : default;
            }, new Context($"DistributedCache.GetAsync for {key}"))
            .GetAwaiter()
            .GetResult();

            if (result is RedisValue value && value.HasValue)
            {
                // We have a value, and RedisValue contains
                // an implicit conversion operator to byte[]
                var sequence = new ReadOnlySequence<byte>((byte[])value!);

                // Deserialize and return
                destination.Write(
                    _serializer.Deserialize<byte[]>(sequence)
                );

                return true;
            }

            // Otherwise, we couldn't deserialize
            _logger.LogDebug("Failed to retrieve value with key: {key}", key);
            return false;
        }

        public bool SetInCache(string key, ReadOnlySequence<byte> value, DistributedCacheEntryOptions options)
        {
            throw new NotImplementedException();
        }

        private bool TryGetFromMemoryCache(string key, out byte[]? data)
        {
            if (!_settings.UseMemoryCache)
            {
                data = null;
                return false;
            }

            _memCache.CheckBackplane();
            return _memCache.TryGet(key, out data);
        }

        // void Serialize<T>(T value, IBufferWriter<byte> target, JsonSerializerOptions? options = default);



        public ValueTask GetBatchFromCacheAsync<T>(IEnumerable<string> keys, Action<string, T?> callback, CancellationToken token = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask<bool> GetFromCacheAsync(string key, IBufferWriter<byte> destination, CancellationToken token = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask RefreshBatchFromCacheAsync(IEnumerable<string> keys, Action<string, bool> callback, CancellationToken token = default)
        {
            throw new NotImplementedException();
        }

        public bool RefreshInCache(string key)
        {
            throw new NotImplementedException();
        }

        public ValueTask<bool> RefreshInCacheAsync(string key, CancellationToken token = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask RemoveBatchFromCacheAsync(IEnumerable<string> keys, Action<string, bool> callback, CancellationToken token = default)
        {
            throw new NotImplementedException();
        }

        public bool RemoveFromCache(string key)
        {
            throw new NotImplementedException();
        }

        public ValueTask<bool> RemoveFromCacheAsync(string key, CancellationToken token = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask SetBatchInCacheAsync(IDictionary<string, ReadOnlySequence<byte>> data, DistributedCacheEntryOptions options, TimeSpan? absoluteExpiration, Action<string, bool> callback, CancellationToken token = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask<bool> SetInCacheAsync(string key, ReadOnlySequence<byte> value, DistributedCacheEntryOptions options, CancellationToken token = default)
        {
            throw new NotImplementedException();
        }
    }
}
