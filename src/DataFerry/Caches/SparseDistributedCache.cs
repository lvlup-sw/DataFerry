using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Wrap;
using StackExchange.Redis;
using System.Buffers;

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
        private PolicyWrap<object> _syncPolicy;
        private AsyncPolicyWrap<object> _asyncPolicy;

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
            _syncPolicy = PollyPolicyGenerator.GenerateSyncPolicy(_logger, _settings);
            _asyncPolicy = PollyPolicyGenerator.GenerateAsyncPolicy(_logger, _settings);
        }

        /// <summary>
        /// Get the configured synchronous Polly policy.
        /// </summary>
        public PolicyWrap<object> GetSyncPollyPolicy() => _syncPolicy;

        /// <summary>
        /// Get the configured asynchronous Polly policy.
        /// </summary>
        public AsyncPolicyWrap<object> GetAsyncPollyPolicy() => _asyncPolicy;

        /// <summary>
        /// Set the fallback value for the polly retry policy.
        /// </summary>
        /// <remarks>Policy will return <see cref="RedisValue.Null"/> if not set.</remarks>
        /// <param name="value"></param>
        public void SetFallbackValue(object value)
        {
            _syncPolicy = PollyPolicyGenerator.GenerateSyncPolicy(_logger, _settings, value);
            _asyncPolicy = PollyPolicyGenerator.GenerateAsyncPolicy(_logger, _settings, value);
        }

        #region SYNCHRONOUS OPERATIONS

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
                _logger.LogDebug("Retrieved entry with key {key} from memory cache.", key);
                var sequence = new ReadOnlySequence<byte>(data);

                destination.Write(
                    _serializer.Deserialize<byte[]>(sequence)
                );

                return true;
            }

            // If the key does not exist in the _memCache, get a database connection
            IDatabase database = _cache.GetDatabase();

            // Execute against the distributed cache
            object result = _syncPolicy.Execute((context) =>
            {
                _logger.LogDebug("Attempting to retrieve entry with key {key} from cache.", key);
                RedisValue data = database.StringGet(key, CommandFlags.PreferReplica);

                return data.HasValue ? data : default;
            }, new Context($"SparseDistributedCache.GetFromCache for {key}"));

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
        
        public bool SetInCache(string key, ReadOnlySequence<byte> value, DistributedCacheEntryOptions? options)
        {
            if (_settings.UseMemoryCache)
            {
                _memCache.CheckBackplane();
                _memCache.AddOrUpdate(key, _serializer.SerializeToUtf8Bytes(value), TimeSpan.FromMinutes(_settings.InMemoryAbsoluteExpiration));
            }

            IDatabase database = _cache.GetDatabase();

            // Execute against the distributed cache
            object result = _syncPolicy.Execute((context) =>
            {
                var ttl = options?.SlidingExpiration ?? options?.AbsoluteExpirationRelativeToNow ?? TimeSpan.FromHours(_settings.AbsoluteExpiration);
                _logger.LogDebug("Attempting to add entry with key {key} and ttl {ttl} to cache.", key, ttl);
                return database.StringSet(key, _serializer.SerializeToUtf8Bytes(value), ttl);
            }, new Context($"SparseDistributedCache.SetInCache for {key}"));

            return result as bool?
                ?? default;
        }

        public bool RefreshInCache(string key, DistributedCacheEntryOptions options)
        {
            if (_settings.UseMemoryCache)
            {
                _memCache.CheckBackplane();
                _memCache.Refresh(key, TimeSpan.FromMinutes(_settings.InMemoryAbsoluteExpiration));
            }

            IDatabase database = _cache.GetDatabase();

            // Execute against the distributed cache
            object result = _syncPolicy.Execute((context) =>
            {
                var ttl = options.SlidingExpiration ?? options.AbsoluteExpirationRelativeToNow ?? TimeSpan.FromMinutes(60);
                _logger.LogDebug("Attempting to refresh entry with key {key} and ttl {ttl} from cache.", key, ttl);
                return database.KeyExpire(key, ttl);
            }, new Context($"SparseDistributedCache.RefreshInCache for {key}"));

            return result as bool?
                ?? default;
        }

        public bool RemoveFromCache(string key)
        {
            if (_settings.UseMemoryCache)
            {
                _memCache.CheckBackplane();
                _memCache.Remove(key);
            }

            IDatabase database = _cache.GetDatabase();

            // Execute against the distributed cache
            object result = _syncPolicy.Execute((context) =>
            {
                _logger.LogDebug("Attempting to remove entry with key {key} from cache.", key);
                return database.KeyDelete(key);
            }, new Context($"SparseDistributedCache.RemoveFromCache for {key}"));

            return result as bool?
                ?? default;
        }

        #endregion
        #region ASYNCHRONOUS OPERATIONS

        // void Serialize<T>(T value, IBufferWriter<byte> target, JsonSerializerOptions? options = default);
        public async ValueTask<bool> GetFromCacheAsync(string key, IBufferWriter<byte> destination, CancellationToken token = default)
        {
            // Check the _memCache first
            if (TryGetFromMemoryCache(key, out byte[]? data)
                && data is not null)
            {
                // Success path; deserialize and return
                _logger.LogDebug("Retrieved entry with key {key} from memory cache.", key);
                var sequence = new ReadOnlySequence<byte>(data);

                destination.Write(
                    await _serializer.DeserializeAsync<byte[]>(sequence, token: token)
                );

                return true;
            }

            // If the key does not exist in the _memCache, get a database connection
            IDatabase database = _cache.GetDatabase();

            // Execute against the distributed cache
            object result = await _asyncPolicy.ExecuteAsync(async (context) =>
            {
                _logger.LogDebug("Attempting to retrieve entry with key {key} from cache.", key);
                RedisValue data = await database.StringGetAsync(key, CommandFlags.PreferReplica)
                    .ConfigureAwait(false);

                return data.HasValue ? data : default;
            }, new Context($"SparseDistributedCache.GetFromCache for {key}"));

            if (result is RedisValue value && value.HasValue)
            {
                // We have a value, and RedisValue contains
                // an implicit conversion operator to byte[]
                var sequence = new ReadOnlySequence<byte>((byte[])value!);

                // Deserialize and return
                destination.Write(
                    await _serializer.DeserializeAsync<byte[]>(sequence, token: token)
                );

                return true;
            }

            // Otherwise, we couldn't deserialize
            _logger.LogDebug("Failed to retrieve value with key: {key}", key);
            return false;
        }

        public async ValueTask<bool> SetInCacheAsync(string key, ReadOnlySequence<byte> value, DistributedCacheEntryOptions? options, CancellationToken token = default)
        {
            if (_settings.UseMemoryCache)
            {
                _memCache.CheckBackplane();
                _memCache.AddOrUpdate(key, _serializer.SerializeToUtf8Bytes(value), TimeSpan.FromMinutes(_settings.InMemoryAbsoluteExpiration));
            }

            IDatabase database = _cache.GetDatabase();

            // Execute against the distributed cache
            object result = await _asyncPolicy.ExecuteAsync(async (context) =>
            {
                var ttl = options?.SlidingExpiration ?? options?.AbsoluteExpirationRelativeToNow ?? TimeSpan.FromHours(_settings.AbsoluteExpiration);
                _logger.LogDebug("Attempting to add entry with key {key} and ttl {ttl} to cache.", key, ttl);
                return await database.StringSetAsync(key, _serializer.SerializeToUtf8Bytes(value), ttl)
                    .ConfigureAwait(false);
            }, new Context($"SparseDistributedCache.SetInCache for {key}"));

            return result as bool?
                ?? default;
        }

        public async ValueTask<bool> RefreshInCacheAsync(string key, DistributedCacheEntryOptions options, CancellationToken token = default)
        {
            if (_settings.UseMemoryCache)
            {
                _memCache.CheckBackplane();
                _memCache.Refresh(key, TimeSpan.FromMinutes(_settings.InMemoryAbsoluteExpiration));
            }

            IDatabase database = _cache.GetDatabase();

            // Execute against the distributed cache
            object result = await _asyncPolicy.ExecuteAsync(async (context) =>
            {
                var ttl = options.SlidingExpiration ?? options.AbsoluteExpirationRelativeToNow ?? TimeSpan.FromMinutes(60);
                _logger.LogDebug("Attempting to refresh entry with key {key} and ttl {ttl} from cache.", key, ttl);
                return await database.KeyExpireAsync(key, ttl)
                    .ConfigureAwait(false);
            }, new Context($"SparseDistributedCache.RefreshInCache for {key}"));

            return result as bool?
                ?? default;
        }

        public async ValueTask<bool> RemoveFromCacheAsync(string key, CancellationToken token = default)
        {
            if (_settings.UseMemoryCache)
            {
                _memCache.CheckBackplane();
                _memCache.Remove(key);
            }

            IDatabase database = _cache.GetDatabase();

            // Execute against the distributed cache
            object result = await _asyncPolicy.ExecuteAsync(async (context) =>
            {
                _logger.LogDebug("Attempting to remove entry with key {key} from cache.", key);
                return await database.KeyDeleteAsync(key)
                    .ConfigureAwait(false);
            }, new Context($"SparseDistributedCache.RemoveFromCache for {key}"));

            return result as bool?
                ?? default;
        }

        #endregion
        #region ASYNCHRONOUS BATCH OPERATIONS

        public async ValueTask<IAsyncEnumerable<KeyValuePair<string, T?>>> GetBatchFromCacheAsync<T>(IEnumerable<string> keys, [EnumeratorCancellation] CancellationToken token = default)
        {
            // Get as many entries from the memory cache as possible
            HashSet<string> remainingKeys = new(keys);
            if (_settings.UseMemoryCache)
            {
                remainingKeys = await GetItemsFromMemoryCache(keys, token);
            }

            // Special case where we retrieved all the keys
            if (remainingKeys.Count == 0) 
            {   // Return an empty stream
                yield break;
            }

            // Setup our connections
            IDatabase database = _cache.GetDatabase();
            IBatch batch = database.CreateBatch();

            // Prepare batch requests
            var redisTasks = remainingKeys.ToDictionary(key => key, key => batch.StringGetAsync(key, CommandFlags.PreferReplica));
            batch.Execute();

            foreach (var task in redisTasks)
            {
                object policyExecutionResult = await _policy.ExecuteAsync(
                    async (c, ct) =>
                    {
                        _logger.LogDebug("Attempting to retrieve entry with key {key} from cache.", task.Key);
                        return await task.Value
                            .ConfigureAwait(false);
                    },
                    new Context($"SparseDistributedCache.GetBatchAsync for {remainingKeys.Value}"),
                    cancellationToken ?? default
                );

                T? value = default;
                if (!redisValue.IsNull)
                {
                    value = await _serializer.DeserializeAsync<T>(redisValue); // Assuming you have a deserializer
                }

                yield return new KeyValuePair<string, T?>(task.Key, value);
            }
        }

        public ValueTask SetBatchInCacheAsync(IDictionary<string, ReadOnlySequence<byte>> data, DistributedCacheEntryOptions? options, Action<string, bool> callback, CancellationToken token = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask RefreshBatchFromCacheAsync(IEnumerable<string> keys, DistributedCacheEntryOptions options, Action<string, bool> callback, CancellationToken token = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask RemoveBatchFromCacheAsync(IEnumerable<string> keys, Action<string, bool> callback, CancellationToken token = default)
        {
            throw new NotImplementedException();
        }

        #endregion
        #region HELPER METHODS

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

        internal async Task<HashSet<string>> GetItemsFromMemoryCache<T>(IEnumerable<string> keys, CancellationToken token)
        {
            // WIP
            return new();
        }

        #endregion
    }
}
