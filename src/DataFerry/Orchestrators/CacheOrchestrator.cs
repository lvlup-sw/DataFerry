using lvlup.DataFerry.Buffers;
using lvlup.DataFerry.Orchestrators.Abstractions;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Wrap;
using StackExchange.Redis;
using System.Buffers;
using System.Runtime.CompilerServices;

namespace lvlup.DataFerry.Orchestrators
{
    public class CacheOrchestrator : ICacheOrchestrator
    {
        private readonly IConnectionMultiplexer _cache;
        private readonly IMemCache<string, byte[]> _memCache;
        private readonly CacheSettings _settings;
        private readonly ILogger<CacheOrchestrator> _logger;
        private readonly ReaderWriterLockSlim _policyLock;
        private PolicyWrap<object> _syncPolicy;
        private AsyncPolicyWrap<object> _asyncPolicy;

        /// <summary>
        /// The primary constructor for the <see cref="CacheOrchestrator"/> class.
        /// </summary>
        /// <param name="settings">The settings for the cache.</param>
        /// <exception cref="ArgumentNullException"></exception>""
        public CacheOrchestrator(
            IConnectionMultiplexer cache,
            IMemCache<string, byte[]> memCache,
            IOptions<CacheSettings> settings,
            ILogger<CacheOrchestrator> logger)
        {
            ArgumentNullException.ThrowIfNull(cache);
            ArgumentNullException.ThrowIfNull(memCache);
            ArgumentNullException.ThrowIfNull(settings);
            ArgumentNullException.ThrowIfNull(logger);

            _cache = cache;
            _memCache = memCache;
            _settings = settings.Value;
            _logger = logger;
            _policyLock = new();
            _syncPolicy = PollyPolicyGenerator.GenerateSyncPolicy(_logger, _settings);
            _asyncPolicy = PollyPolicyGenerator.GenerateAsyncPolicy(_logger, _settings);
        }

        /// <inheritdoc/>
        public PolicyWrap<object> SyncPolicy
        {
            get
            {
                _policyLock.EnterReadLock();
                try     { return _syncPolicy; }
                finally {  _policyLock.ExitReadLock(); }
            }
            set
            {
                _policyLock.EnterWriteLock();
                try {
                    ArgumentNullException.ThrowIfNull(value);
                    _syncPolicy = value;
                } finally {  _policyLock.ExitWriteLock(); }
            }
        }

        /// <inheritdoc/>
        public AsyncPolicyWrap<object> AsyncPolicy
        {
            get
            {
                _policyLock.EnterReadLock();
                try     { return _asyncPolicy; }
                finally { _policyLock.ExitReadLock(); }
            }
            set
            {
                _policyLock.EnterWriteLock();
                try {
                    ArgumentNullException.ThrowIfNull(value);
                    _asyncPolicy = value;
                } finally { _policyLock.ExitWriteLock(); }
            }
        }

        /// <inheritdoc/>
        public void SetFallbackValue(object value)
        {
            SyncPolicy = PollyPolicyGenerator.GenerateSyncPolicy(_logger, _settings, value);
            AsyncPolicy = PollyPolicyGenerator.GenerateAsyncPolicy(_logger, _settings, value);
        }

        #region SYNCHRONOUS OPERATIONS

        /// <inheritdoc/>
        public void GetFromCache(string key, IBufferWriter<byte> destination)
        {
            // Check the _memCache first
            if (TryGetFromMemoryCache(key, out byte[]? data)
                && data is not null)
            {
                // Success path; write to BufferWriter and return
                _logger.LogDebug("Retrieved entry with key {key} from memory cache.", key);
                destination.Write(data);
                return;
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
                destination.Write((byte[])value!);
            }
        }

        /// <inheritdoc/>
        public bool SetInCache(string key, byte[] serializedValue, DistributedCacheEntryOptions? options)
        {
            if (_settings.UseMemoryCache)
            {
                _memCache.CheckBackplane();
                _memCache.AddOrUpdate(key, serializedValue, TimeSpan.FromMinutes(_settings.InMemoryAbsoluteExpiration));
            }

            IDatabase database = _cache.GetDatabase();

            // Execute against the distributed cache
            object result = _syncPolicy.Execute((context) =>
            {
                var ttl = options?.SlidingExpiration ?? options?.AbsoluteExpirationRelativeToNow ?? TimeSpan.FromHours(_settings.AbsoluteExpiration);
                _logger.LogDebug("Attempting to add entry with key {key} and ttl {ttl} to cache.", key, ttl);
                return database.StringSet(key, serializedValue, ttl);
            }, new Context($"SparseDistributedCache.SetInCache for {key}"));

            return result as bool?
                ?? default;
        }

        /// <inheritdoc/>
        public bool RefreshInCache(string key, TimeSpan ttl)
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
                _logger.LogDebug("Attempting to refresh entry with key {key} and ttl {ttl} from cache.", key, ttl);
                return database.KeyExpire(key, ttl, ExpireWhen.Always);
            }, new Context($"SparseDistributedCache.RefreshInCache for {key}"));

            return result as bool?
                ?? default;
        }

        /// <inheritdoc/>
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

        /// <inheritdoc/>
        public async ValueTask GetFromCacheAsync(string key, IBufferWriter<byte> destination, CancellationToken token = default)
        {
            // Check the _memCache first
            if (TryGetFromMemoryCache(key, out byte[]? data)
                && data is not null)
            {
                // Success path; write to BufferWriter and return
                _logger.LogDebug("Retrieved entry with key {key} from memory cache.", key);
                destination.Write(data);
                return;
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
                destination.Write((byte[])value!);
            }
        }

        /// <inheritdoc/>
        public async ValueTask<bool> SetInCacheAsync(string key, byte[] serializedValue, DistributedCacheEntryOptions? options, CancellationToken token = default)
        {
            if (_settings.UseMemoryCache)
            {
                _memCache.CheckBackplane();
                _memCache.AddOrUpdate(key, serializedValue, TimeSpan.FromMinutes(_settings.InMemoryAbsoluteExpiration));
            }

            IDatabase database = _cache.GetDatabase();

            // Execute against the distributed cache
            object result = await _asyncPolicy.ExecuteAsync(async (ctx, ct) =>
            {
                var ttl = options?.SlidingExpiration ?? options?.AbsoluteExpirationRelativeToNow ?? TimeSpan.FromHours(_settings.AbsoluteExpiration);
                _logger.LogDebug("Attempting to add entry with key {key} and ttl {ttl} to cache.", key, ttl);
                return await database.StringSetAsync(key, serializedValue, ttl)
                    .ConfigureAwait(false);
            }, new Context($"SparseDistributedCache.SetInCache for {key}"), token);

            return result as bool?
                ?? default;
        }

        /// <inheritdoc/>
        public async ValueTask<bool> RefreshInCacheAsync(string key, TimeSpan ttl, CancellationToken token = default)
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
                _logger.LogDebug("Attempting to refresh entry with key {key} and ttl {ttl} from cache.", key, ttl);
                return await database.KeyExpireAsync(key, ttl, ExpireWhen.Always)
                    .ConfigureAwait(false);
            }, new Context($"SparseDistributedCache.RefreshInCache for {key}"), token);

            return result as bool?
                ?? default;
        }

        /// <inheritdoc/>
        public async ValueTask<bool> RemoveFromCacheAsync(string key, CancellationToken token = default)
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

        /// <inheritdoc/>
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
                    var (index, length) = destination.WriteAndGetPosition(kvp.Value);
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
                key => GetFromRedisTask(key, batch.StringGetAsync(key, CommandFlags.PreferReplica), destination, token)
            );
            batch.Execute();

            // Process each task and return individual results as we complete them
            await foreach (var task in Task.WhenEach(redisTasks.Values).WithCancellation(token))
            {
                yield return task.Result;
            }
        }

        /// <inheritdoc/>
        public async IAsyncEnumerable<KeyValuePair<string, bool>> SetBatchInCacheAsync(IDictionary<string, byte[]> data, DistributedCacheEntryOptions? options, [EnumeratorCancellation] CancellationToken token = default)
        {
            // Set our entries in the memory cache
            if (_settings.UseMemoryCache)
            {
                foreach (var kvp in data)
                {
                    _memCache.CheckBackplane();
                    _memCache.AddOrUpdate(kvp.Key, kvp.Value, TimeSpan.FromMinutes(_settings.InMemoryAbsoluteExpiration));
                }
            }

            // Setup our connections
            IDatabase database = _cache.GetDatabase();
            IBatch batch = database.CreateBatch();

            var ttl = options?.SlidingExpiration ?? options?.AbsoluteExpirationRelativeToNow ?? TimeSpan.FromHours(_settings.AbsoluteExpiration);

            // Prepare batch requests
            var redisTasks = data.ToDictionary(
                kvp => SetInRedisTask(kvp.Key, batch.StringSetAsync(kvp.Key, kvp.Value, ttl, When.Always), ttl, token),
                kvp => kvp.Key
            );
            batch.Execute();

            // Process each task and return individual results as we complete them
            await foreach (var task in Task.WhenEach(redisTasks.Keys).WithCancellation(token))
            {
                yield return new KeyValuePair<string, bool>(redisTasks[task], task.Result);
            }
        }

        /// <inheritdoc/>
        public async IAsyncEnumerable<KeyValuePair<string, bool>> RefreshBatchFromCacheAsync(IEnumerable<string> keys, TimeSpan ttl, [EnumeratorCancellation] CancellationToken token = default)
        {
            // Set our entries in the memory cache
            if (_settings.UseMemoryCache)
            {
                foreach (var key in keys)
                {
                    _memCache.CheckBackplane();
                    _memCache.Refresh(key, TimeSpan.FromMinutes(_settings.InMemoryAbsoluteExpiration));
                }
            }

            // Setup our connections
            IDatabase database = _cache.GetDatabase();
            IBatch batch = database.CreateBatch();

            // Prepare batch requests
            var redisTasks = keys.ToDictionary(
                key => RefreshInRedisTask(key, batch.KeyExpireAsync(key, ttl, ExpireWhen.Always), ttl, token),
                key => key
            );
            batch.Execute();

            // Process each task and return individual results as we complete them
            await foreach (var task in Task.WhenEach(redisTasks.Keys).WithCancellation(token))
            {
                yield return new KeyValuePair<string, bool>(redisTasks[task], task.Result);
            }
        }

        /// <inheritdoc/>
        public async IAsyncEnumerable<KeyValuePair<string, bool>> RemoveBatchFromCacheAsync(IEnumerable<string> keys, [EnumeratorCancellation] CancellationToken token = default)
        {
            // Remove as many entries from the memory cache as possible
            if (_settings.UseMemoryCache)
            {
                foreach (var key in keys)
                {
                    _memCache.CheckBackplane();
                    _memCache.Remove(key);
                }
            }

            // Setup our connections
            IDatabase database = _cache.GetDatabase();
            IBatch batch = database.CreateBatch();

            // Prepare batch requests
            var redisTasks = keys.ToDictionary(
                key => RemoveFromRedisTask(key, batch.KeyDeleteAsync(key), token),
                key => key
            );
            batch.Execute();

            // Process each task and return individual results as we complete them
            await foreach (var task in Task.WhenEach(redisTasks.Keys).WithCancellation(token))
            {
                yield return new KeyValuePair<string, bool>(redisTasks[task], task.Result);
            }
        }

        #endregion
        #region HELPER METHODS

        /// <summary>
        /// Outputs a <see cref="byte"/> array associated with the key from the memory cache.
        /// </summary>
        /// <param name="key"></param>
        /// <param name="data"></param>
        /// <returns>A <c>bool</c> indicating the result of the operation.</returns>
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

        /// <summary>
        /// Outputs a <see cref="KeyValuePair"/> associated with the key from the memory cache.
        /// </summary>
        /// <param name="key"></param>
        /// <param name="kvp"></param>
        internal void GetFromMemoryCache(string key, out KeyValuePair<string, byte[]?> kvp)
        {
            _memCache.CheckBackplane();
            kvp = _memCache.TryGet(key, out byte[] data)
                ? new(key, data)
                : new(key, default);
        }

        /// <summary>
        /// Creates the individual Get task for the distributed cache.
        /// </summary>
        /// <param name="key"></param>
        /// <param name="operation"></param>
        /// <param name="destination"></param>
        /// <param name="token"></param>
        /// <returns>A <c>Task</c> containing the result of the operation.</returns>
        internal async Task<(string Key, int Index, int Length)> GetFromRedisTask(string key, Task<RedisValue> operation, RentedBufferWriter<byte> destination, CancellationToken token)
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
                var (index, length) = destination.WriteAndGetPosition((byte[])value!);

                // Return properties of write operation
                return (key, index, length);
            }

            // Failure case
            return (key, -1, -1);
        }

        /// <summary>
        /// Creates the individual Set task for the distributed cache.
        /// </summary>
        /// <param name="key"></param>
        /// <param name="operation"></param>
        /// <param name="expiration"></param>
        /// <param name="token"></param>
        /// <returns>A <c>Task</c> containing the result of the operation.</returns>
        internal async Task<bool> SetInRedisTask(string key, Task<bool> operation, TimeSpan expiration, CancellationToken token)
        {
            object result = await _asyncPolicy.ExecuteAsync(async (ctx, ct) =>
            {
                _logger.LogDebug("Attempting to add entry with key {key} and ttl {ttl} to cache.", key, expiration);
                return await operation
                    .ConfigureAwait(false);
            }, new Context($"SparseDistributedCache.SetInCache for {key}"), token);

            return result as bool?
                ?? default;
        }

        /// <summary>
        /// Creates the individual Refresh task for the distributed cache.
        /// </summary>
        /// <param name="key"></param>
        /// <param name="operation"></param>
        /// <param name="expiration"></param>
        /// <param name="token"></param>
        /// <returns>A <c>Task</c> containing the result of the operation.</returns>
        internal async Task<bool> RefreshInRedisTask(string key, Task<bool> operation, TimeSpan expiration, CancellationToken token)
        {
            object result = await _asyncPolicy.ExecuteAsync(async (ctx, ct) =>
            {
                _logger.LogDebug("Attempting to refresh entry with key {key} and ttl {ttl} from cache.", key, expiration);
                return await operation
                    .ConfigureAwait(false);
            }, new Context($"SparseDistributedCache.RefreshInCache for {key}"), token);

            return result as bool?
                ?? default;
        }

        /// <summary>
        /// Creates the individual Remove task for the distributed cache.
        /// </summary>
        /// <param name="key"></param>
        /// <param name="operation"></param>
        /// <param name="token"></param>
        /// <returns>A <c>Task</c> containing the result of the operation.</returns>
        internal async Task<bool> RemoveFromRedisTask(string key, Task<bool> operation, CancellationToken token)
        {
            object result = await _asyncPolicy.ExecuteAsync(async (ctx, ct) =>
            {
                _logger.LogDebug("Attempting to remove entry with key {key} from cache.", key);
                return await operation
                    .ConfigureAwait(false);
            }, new Context($"SparseDistributedCache.RemoveFromCache for {key}"), token);

            return result as bool?
                ?? default;
        }

        #endregion
    }
}
