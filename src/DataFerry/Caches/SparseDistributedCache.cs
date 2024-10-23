using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Wrap;
using StackExchange.Redis;
using System.Buffers;
using System.Runtime.CompilerServices;

namespace lvlup.DataFerry.Caches
{
    public class SparseDistributedCache : ISparseDistributedCache
    {
        private readonly IConnectionMultiplexer _cache;
        private readonly ILfuMemCache<string, byte[]> _memCache;
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
            ILfuMemCache<string, byte[]> memCache, 
            IDataFerrySerializer serializer, 
            IOptions<CacheSettings> settings, 
            ILogger<SparseDistributedCache> logger)
        {
            ArgumentNullException.ThrowIfNull(cache);
            ArgumentNullException.ThrowIfNull(memCache);
            ArgumentNullException.ThrowIfNull(serializer);
            ArgumentNullException.ThrowIfNull(settings);
            ArgumentNullException.ThrowIfNull(logger);

            _cache = cache;
            _memCache = memCache;
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
        /// Set the fallback value for the Polly policy.
        /// </summary>
        /// <remarks>Policy will return <see cref="RedisValue.Null"/> if not set.</remarks>
        /// <param name="value"></param>
        public void SetFallbackValue(object value)
        {
            _syncPolicy = PollyPolicyGenerator.GenerateSyncPolicy(_logger, _settings, value);
            _asyncPolicy = PollyPolicyGenerator.GenerateAsyncPolicy(_logger, _settings, value);
        }

        #region SYNCHRONOUS OPERATIONS

        public void GetFromCache(string key, IBufferWriter<byte> destination)
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
            }
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

        public async ValueTask GetFromCacheAsync(string key, IBufferWriter<byte> destination, CancellationToken token = default)
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
            }

            // If the key does not exist in the _memCache, get a database connection
            IDatabase database = _cache.GetDatabase();

            // Execute against the distributed cache
            object result = await _asyncPolicy.ExecuteAsync(async (ctx, ct) =>
            {
                _logger.LogDebug("Attempting to retrieve entry with key {key} from cache.", key);
                RedisValue data = await database.StringGetAsync(key, CommandFlags.PreferReplica)
                    .ConfigureAwait(false);

                return data.HasValue ? data : default;
            }, new Context($"SparseDistributedCache.GetFromCache for {key}"), token);

            if (result is RedisValue value && value.HasValue)
            {
                // We have a value, and RedisValue contains
                // an implicit conversion operator to byte[]
                var sequence = new ReadOnlySequence<byte>((byte[])value!);

                // Deserialize and return
                destination.Write(
                    await _serializer.DeserializeAsync<byte[]>(sequence, token: token)
                );
            }
        }

        public async Task<bool> SetInCacheAsync(string key, ReadOnlySequence<byte> value, DistributedCacheEntryOptions? options, CancellationToken token = default)
        {
            if (_settings.UseMemoryCache)
            {
                _memCache.CheckBackplane();
                _memCache.AddOrUpdate(key, _serializer.SerializeToUtf8Bytes(value), TimeSpan.FromMinutes(_settings.InMemoryAbsoluteExpiration));
            }

            IDatabase database = _cache.GetDatabase();

            // Execute against the distributed cache
            object result = await _asyncPolicy.ExecuteAsync(async (ctx, ct) =>
            {
                var ttl = options?.SlidingExpiration ?? options?.AbsoluteExpirationRelativeToNow ?? TimeSpan.FromHours(_settings.AbsoluteExpiration);
                _logger.LogDebug("Attempting to add entry with key {key} and ttl {ttl} to cache.", key, ttl);
                return await database.StringSetAsync(key, _serializer.SerializeToUtf8Bytes(value), ttl)
                    .ConfigureAwait(false);
            }, new Context($"SparseDistributedCache.SetInCache for {key}"), token);

            return result as bool?
                ?? default;
        }

        public async Task<bool> RefreshInCacheAsync(string key, DistributedCacheEntryOptions options, CancellationToken token = default)
        {
            if (_settings.UseMemoryCache)
            {
                _memCache.CheckBackplane();
                _memCache.Refresh(key, TimeSpan.FromMinutes(_settings.InMemoryAbsoluteExpiration));
            }

            IDatabase database = _cache.GetDatabase();

            // Execute against the distributed cache
            object result = await _asyncPolicy.ExecuteAsync(async (ctx, ct) =>
            {
                var ttl = options.SlidingExpiration ?? options.AbsoluteExpirationRelativeToNow ?? TimeSpan.FromMinutes(60);
                _logger.LogDebug("Attempting to refresh entry with key {key} and ttl {ttl} from cache.", key, ttl);
                return await database.KeyExpireAsync(key, ttl)
                    .ConfigureAwait(false);
            }, new Context($"SparseDistributedCache.RefreshInCache for {key}"), token);

            return result as bool?
                ?? default;
        }

        public async Task<bool> RemoveFromCacheAsync(string key, CancellationToken token = default)
        {
            if (_settings.UseMemoryCache)
            {
                _memCache.CheckBackplane();
                _memCache.Remove(key);
            }

            IDatabase database = _cache.GetDatabase();

            // Execute against the distributed cache
            object result = await _asyncPolicy.ExecuteAsync(async (ctx, ct) =>
            {
                _logger.LogDebug("Attempting to remove entry with key {key} from cache.", key);
                return await database.KeyDeleteAsync(key)
                    .ConfigureAwait(false);
            }, new Context($"SparseDistributedCache.RemoveFromCache for {key}"), token);

            return result as bool?
                ?? default;
        }

        #endregion
        #region ASYNCHRONOUS BATCH OPERATIONS

        public async IAsyncEnumerable<(string Key, int Index, int Length)> GetBatchFromCacheAsync(IEnumerable<string> keys, RentedBufferWriter<byte> destination, [EnumeratorCancellation] CancellationToken token = default)
        {
            // Get as many entries from the memory cache as possible
            HashSet<string> remainingKeys = new(keys);
            if (_settings.UseMemoryCache)
            {
                foreach (var key in keys)
                {
                    GetFromMemoryCache(key, out var kvp);
                    if (kvp.Value is null) continue;

                    // Deserialize and write to buffer
                    var sequence = new ReadOnlySequence<byte>(kvp.Value);
                    var (index, length) = destination.WriteAndGetPosition(
                        _serializer.Deserialize<byte[]>(sequence)
                    );
                    remainingKeys.Remove(kvp.Key);

                    // Return properties of write operation
                    yield return (key, index, length);
                }
            }

            // Special case where we retrieved all the keys
            if (remainingKeys.Count == 0) yield break;

            // Setup our connections
            IDatabase database = _cache.GetDatabase();
            IBatch batch = database.CreateBatch();

            // Prepare batch requests
            var redisTasks = remainingKeys.ToDictionary(
                key => key,
                key => GetFromRedisAsync(key, batch.StringGetAsync(key, CommandFlags.PreferReplica), destination, token)
            );
            batch.Execute();

            // Process each task and return individual results as we complete them
            await foreach (var task in Task.WhenEach(redisTasks.Values).WithCancellation(token))
            {
                yield return task.Result;
            }
        }

        public async IAsyncEnumerable<KeyValuePair<string, bool>> SetBatchInCacheAsync(IDictionary<string, ReadOnlySequence<byte>> data, DistributedCacheEntryOptions? options, [EnumeratorCancellation] CancellationToken token = default)
        {
            yield break;
            throw new NotImplementedException();
        }

        public async IAsyncEnumerable<KeyValuePair<string, bool>> RefreshBatchFromCacheAsync(IEnumerable<string> keys, DistributedCacheEntryOptions options, [EnumeratorCancellation] CancellationToken token = default)
        {
            yield break;
            throw new NotImplementedException();
        }

        public async IAsyncEnumerable<KeyValuePair<string, bool>> RemoveBatchFromCacheAsync(IEnumerable<string> keys, [EnumeratorCancellation] CancellationToken token = default)
        {
            yield break;
            throw new NotImplementedException();
        }

        #endregion
        #region HELPER METHODS

        internal bool TryGetFromMemoryCache(string key, out byte[]? data)
        {
            if (!_settings.UseMemoryCache)
            {
                data = null;
                return false;
            }

            _memCache.CheckBackplane();
            return _memCache.TryGet(key, out data);
        }

        internal void GetFromMemoryCache(string key, out KeyValuePair<string, byte[]?> kvp)
        {
            _memCache.CheckBackplane();
            kvp = _memCache.TryGet(key, out byte[] data) 
                ? new(key, data) 
                : new(key, default);
        }

        internal async Task<(string Key, int Index, int Length)> GetFromRedisAsync(string key, Task<RedisValue> operation, RentedBufferWriter<byte> destination, CancellationToken token)
        {
            object result = await _asyncPolicy.ExecuteAsync(async (ctx, ct) =>
            {
                _logger.LogDebug("Attempting to retrieve entry with key {key} from cache.", key);
                return await operation
                    .ConfigureAwait(false);
            },
            new Context($"SparseDistributedCache.GetBatchAsync for {key}"), token);

            if (result is RedisValue value && value.HasValue)
            {
                // We have a value, and RedisValue contains
                // an implicit conversion operator to byte[]
                var sequence = new ReadOnlySequence<byte>((byte[])value!);

                // Deserialize and write to buffer
                var (index, length) = destination.WriteAndGetPosition(
                    await _serializer.DeserializeAsync<byte[]>(sequence, token: token)
                );

                // Return properties of write operation
                return (key, index, length);
            }

            // Failure case
            return (key, -1, -1);
        }

        #endregion
    }
}
