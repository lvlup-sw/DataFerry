using StackExchange.Redis;
using Polly.Wrap;
using Polly;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;
using System.Buffers;
using System.Text;

namespace lvlup.DataFerry.Caches
{
    /// <summary>
    /// An implementation of <see cref="IConnectionMultiplexer"/> which uses the <see cref="IDistributedCache{T}"/> interface as a base. Polly is integrated overtop for handling exceptions and retries.
    /// </summary>
    /// <remarks>
    /// This can be used with numerous Redis cache providers such as AWS ElastiCache or Azure Cache for Redis.
    /// </remarks>
    public class DistributedCache<T> where T : class
    {
        private readonly IConnectionMultiplexer _cache;
        private readonly IFastMemCache<string, string> _memCache;
        private readonly ArrayPool<byte>? _arrayPool;
        private readonly CacheSettings _settings;
        private readonly ILogger _logger;
        private AsyncPolicyWrap<object> _policy;

        /// <summary>
        /// The primary constructor for the <see cref="DistributedCache{T}"/> class.
        /// </summary>
        /// <param name="settings">The settings for the cache.</param>
        /// <exception cref="ArgumentNullException"></exception>""
        public DistributedCache(IConnectionMultiplexer cache, IFastMemCache<string, string> memCache, IOptions<CacheSettings> settings, ILogger logger)
        {
            ArgumentNullException.ThrowIfNull(cache);
            ArgumentNullException.ThrowIfNull(memCache);
            ArgumentNullException.ThrowIfNull(settings);
            ArgumentNullException.ThrowIfNull(logger);

            _cache = cache;
            _memCache = memCache;
            _settings = settings.Value;
            _logger = logger;
            _policy = PollyPolicyGenerator.GeneratePolicy(_logger, _settings);
        }

        /// <summary>
        /// Alternative constructor for the <see cref="DistributedCache{T}"/> class
        /// which includes an <see cref="ArrayPool{T}"/> instance for deserialization operations.
        /// </summary>
        /// <param name="settings">The settings for the cache.</param>
        /// <exception cref="ArgumentNullException"></exception>""
        public DistributedCache(IConnectionMultiplexer cache, IFastMemCache<string, string> memCache, ArrayPool<byte> arrayPool, IOptions<CacheSettings> settings, ILogger logger)
        {
            ArgumentNullException.ThrowIfNull(cache);
            ArgumentNullException.ThrowIfNull(memCache);
            ArgumentNullException.ThrowIfNull(arrayPool);
            ArgumentNullException.ThrowIfNull(settings);
            ArgumentNullException.ThrowIfNull(logger);

            _cache = cache;
            _memCache = memCache;
            _arrayPool = arrayPool;
            _settings = settings.Value;
            _logger = logger;
            _policy = PollyPolicyGenerator.GeneratePolicy(_logger, _settings);
        }

        /// <summary>
        /// Retrieves an <see cref="IDatabase"/> representation of the cache.
        /// </summary>
        public IDatabase GetCacheConnection() => _cache.GetDatabase();

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
        /// Asynchronously retrieves an entry from the cache using a key.
        /// </summary>
        /// <remarks>
        /// Returns the entry if it exists in the cache, null otherwise.
        /// </remarks>
        /// <param name="key">The key of the record to retrieve.</param>
        public async Task<T?> GetFromCacheAsync(string key)
        {
            // Check the _memCache first
            if (_settings.UseMemoryCache && _memCache.TryGet(key, out string memData))
            {
                _logger.LogDebug("Retrieved data with key {key} from memory cache.", key);
                return JsonSerializer.Deserialize<T>(memData);
            }

            // If the key does not exist in the _memCache, proceed with the Polly policy execution
            IDatabase database = _cache.GetDatabase();

            object result = await _policy.ExecuteAsync(async (context) =>
            {
                _logger.LogDebug("Attempting to retrieve entry with key {key} from cache.", key);
                RedisValue data = await database.StringGetAsync(key, CommandFlags.PreferReplica)
                    .ConfigureAwait(false);
                
                return data.HasValue ? data : default;
            }, new Context($"DistributedCache.GetAsync for {key}"));

            return await GetDeserializedValueAsync(key, result) ?? default;
        }

        /// <summary>
        /// Asynchronously adds an entry to the cache with a specified key.
        /// </summary>
        /// <remarks>
        /// Returns true if the entry was added to the cache, false otherwise.
        /// </remarks>
        /// <param name="key">The key to use for the entry.</param>
        /// <param name="data">The data to add to the cache.</param>
        public async Task<bool> SetInCacheAsync(string key, T data, TimeSpan absoluteExpiration)
        {
            IDatabase database = _cache.GetDatabase();
            if (_settings.UseMemoryCache)
            {
                _memCache.AddOrUpdate(key, JsonSerializer.Serialize(data), TimeSpan.FromMinutes(_settings.InMemoryAbsoluteExpiration));
            }

            object result = await _policy.ExecuteAsync(async (context) =>
            {
                _logger.LogDebug("Attempting to add entry with key {key} to cache.", key);
                return await database.StringSetAsync(key, JsonSerializer.Serialize(data), absoluteExpiration)
                    .ConfigureAwait(false);
            }, new Context($"DistributedCache.SetAsync for {key}"));

            return result as bool?
                ?? default;
        }

        /// <summary>
        /// Asynchronously removes an entry from the cache using a key.
        /// </summary>
        /// <remarks>
        /// Returns true if the entry was removed from the cache, false otherwise.
        /// </remarks>
        /// <param name="key">The key of the entry to remove.</param>
        public async Task<bool> RemoveFromCacheAsync(string key)
        {
            IDatabase database = _cache.GetDatabase();
            if (_settings.UseMemoryCache)
            {
                _memCache.Remove(key);
            }

            object result = await _policy.ExecuteAsync(async (context) =>
            {
                _logger.LogDebug("Attempting to remove entry with key {key} from cache.", key);
                return await database.KeyDeleteAsync(key)
                    .ConfigureAwait(false);
            }, new Context($"DistributedCache.RemoveAsync for {key}"));

            return result as bool? 
                ?? default;
        }

        /// <summary>
        /// Batch operation for getting multiple entries from cache
        /// </summary>
        /// <typeparam name="T">The type of the data to retrieve from the cache.</typeparam>
        /// <param name="keys">The keys associated with the data in the cache.</param>
        /// <returns>A dictionary of the retrieved entries. If a key does not exist, its value in the dictionary will be default(<typeparamref name="T"/>).</returns>
        public async Task<IDictionary<string, T>> GetBatchFromCacheAsync(IEnumerable<string> keys, CancellationToken? cancellationToken = null)
        {
            // Get Redis objects
            IDatabase database = _cache.GetDatabase();
            IBatch batch = database.CreateBatch();

            // Setup polly to retry the entire operation if anything fails
            object policyExecutionResult = await _policy.ExecuteAsync(
                async (c, ct) =>
                {
                    return (_settings.UseMemoryCache) switch
                    {
                        true  => await GetBatchWithMemCacheAsync(keys, batch),
                        false => await GetBatchWithRedisOnlyAsync(keys, batch),
                    };
                },
                new Context($"DistributedCache.GetBatchAsync for {keys}"),
                cancellationToken ?? default
            );

            return policyExecutionResult as Dictionary<string, T> ?? [];
        }

        /// <summary>
        /// Batch operation for setting multiple entries in cache
        /// </summary>
        /// <typeparam name="T">The type of the data to store in the cache.</typeparam>
        /// <param name="data">A dictionary containing the keys and data to store in the cache.</param>
        /// <param name="absoluteExpireTime">The absolute expiration time for the data. If this is null, the default expiration time is used.</param>
        /// <returns>True if all entries were set successfully; otherwise, false.</returns>
        public async Task<IDictionary<string, bool>> SetBatchInCacheAsync(IDictionary<string, T> data, TimeSpan absoluteExpiration, CancellationToken? cancellationToken = null)
        {
            IDatabase database = _cache.GetDatabase();
            IBatch batch = database.CreateBatch();

            // Setup polly to retry the entire operation if anything fails
            object policyExecutionResult = await _policy.ExecuteAsync(
                async (c, ct) =>
                {
                    // Pre-serialize values
                    var serializedValues = data.ToDictionary(kv => kv.Key, kv => JsonSerializer.Serialize(kv.Value));

                    // Set items in the memCache
                    if (_settings.UseMemoryCache)
                    {
                        foreach (var kvp in serializedValues)
                        {
                            _memCache.AddOrUpdate(kvp.Key, kvp.Value, TimeSpan.FromMinutes(_settings.InMemoryAbsoluteExpiration));
                        }
                    }

                    // Create tasks for each item
                    var tasks = serializedValues
                        .Select(kvp => 
                        (
                            kvp.Key, 
                            Task: batch.StringSetAsync(
                                kvp.Key, 
                                kvp.Value, 
                                absoluteExpiration
                            )
                        ));

                    // Enqueues all the tasks into a single GET request
                    batch.Execute();
                    // Note we don't capture the sync context here to avoid deadlocks
                    await Task.WhenAll(tasks.Select(t => t.Task)).ConfigureAwait(false);

                    // Add each task result to a dict and return
                    return tasks.ToDictionary(
                        task => task.Key,
                        task => task.Task.Result
                    );
                },
                new Context($"DistributedCache.SetBatchAsync for {string.Join(", ", data.Keys)}"),
                cancellationToken ?? default
            );

            return policyExecutionResult as Dictionary<string, bool> ?? [];
        }

        /// <summary>
        /// Batch operation for removing multiple entries from cache.
        /// </summary>
        /// <param name="keys">The keys associated with the data in the cache.</param>
        /// <returns>True if all entries were removed successfully; otherwise, false.</returns>
        public async Task<IDictionary<string, bool>> RemoveBatchFromCacheAsync(IEnumerable<string> keys, CancellationToken? cancellationToken = null)
        {
            IDatabase database = _cache.GetDatabase();
            IBatch batch = database.CreateBatch();

            // Setup polly to retry the entire operation if anything fails
            object policyExecutionResult = await _policy.ExecuteAsync(
                async (c, ct) =>
                {
                    // Create tasks for each item
                    var tasks = keys
                        .Select(key => 
                        (
                            Key: key, 
                            Task: batch.KeyDeleteAsync(key)
                        ));

                    // Remove items from memCache
                    if (_settings.UseMemoryCache)
                    {
                        foreach (var key in keys)
                        {
                            _memCache.Remove(key);
                        }
                    }

                    // Enqueues all the tasks into a single GET request
                    batch.Execute();
                    // Note we don't capture the sync context here to avoid deadlocks
                    await Task.WhenAll(tasks.Select(t => t.Task)).ConfigureAwait(false);

                    // Add each task result to a dict and return
                    return tasks.ToDictionary(
                        task => task.Key,
                        task => task.Task.Result
                    );
                },
                new Context($"DistributedCache.RemoveBatchAsync for {string.Join(", ", keys)}"),
                cancellationToken ?? default
            );

            return policyExecutionResult as Dictionary<string, bool> ?? [];
        }

        // GetBatch helper methods
        private async Task<Dictionary<string, T>> GetBatchWithMemCacheAsync(IEnumerable<string> keys, IBatch batch)
        {
            // Extract matching keys from memCache in one pass
            // We use a dictionary to store the tasks and their associated keys since we need the values
            var memCacheHits = keys
                .Select(key =>
                {
                    _memCache.TryGet(key, out var value);
                    return (key, value);
                })
                .Where(tuple => tuple.value is not null)
                .ToDictionary(tuple => tuple.key, tuple => tuple.value);

            // Extract missing keys 
            HashSet<string> missingKeys = new(keys.Except(memCacheHits.Keys));

            if (missingKeys.Count is 0) return GetDeserializedDictionary(memCacheHits);

            // Fetch missing keys from Redis
            var redisTasks = missingKeys.ToDictionary(key => key, key => batch.StringGetAsync(key, CommandFlags.PreferReplica));

            // Enqueues all the tasks into a single GET request
            batch.Execute();

            // Await each task and deserialize the value into a tuple
            // Then filter out the null results before extracting to a dict
            // Note we don't capture the sync context here to avoid deadlocks
            Dictionary<string, T> redisResults = (await Task.WhenAll(
                redisTasks.Select(async task =>
                {
                    var value = await GetDeserializedValueAsync(task);
                    return (task.Key, value);
                })
            ).ConfigureAwait(false))
            .Where(kvp => kvp.value is not null)
            .ToDictionary(kvp => kvp.Key, kvp => kvp.value!);

            // Return if we don't have any mem cache results
            if (!memCacheHits.Any()) return redisResults;

            // Add deserialized mem cache values
            foreach (var kvp in memCacheHits)
            {
                var value = await GetDeserializedValueAsync(kvp);
                if (value is not null) redisResults.Add(kvp.Key, value);
            }

            return redisResults;
        }

        private async Task<Dictionary<string, T>> GetBatchWithRedisOnlyAsync(IEnumerable<string> keys, IBatch batch)
        {
            // Fetch missing keys from Redis
            var redisTasks = keys.ToDictionary(key => key, key => batch.StringGetAsync(key, CommandFlags.PreferReplica));

            // Enqueues all the tasks into a single GET request
            batch.Execute();

            // Await each task and deserialize the value into a tuple
            // Then filter out the null results before extracting to a dict
            // Note we don't capture the sync context here to avoid deadlocks
            Dictionary<string, T> redisResults = (await Task.WhenAll(
                redisTasks.Select(async task =>
                {
                    var value = await GetDeserializedValueAsync(task);
                    return (task.Key, value);
                })
            ).ConfigureAwait(false))
            .Where(kvp => kvp.value is not null)
            .ToDictionary(kvp => kvp.Key, kvp => kvp.value!);

            return redisResults;
        }

        // Deserialization helper methods
        private Dictionary<string, T> GetDeserializedDictionary(Dictionary<string, string> serializedDictionary)
        {
            return serializedDictionary
                .Select(async task =>
                {
                    var value = await GetDeserializedValueAsync(task);
                    return (task.Key, value);
                })
                .Where(kvp => kvp.Result.value is not null)
                .ToDictionary(kvp => kvp.Result.Key, kvp => kvp.Result.value!);
        }

        private async Task<T?> GetDeserializedValueAsync(KeyValuePair<string, Task<RedisValue>> kvp)
        {
            // RedisValue has an implicit string conversion
            string? value = (string?) await kvp.Value.ConfigureAwait(false);
            if (string.IsNullOrEmpty(value)) return default;

            // Estimate the size of the byte array needed
            int estimatedSize = Encoding.UTF8.GetByteCount(value);
            byte[] rentedArray = _arrayPool is not null
                ? _arrayPool.Rent(estimatedSize)
                : new byte[estimatedSize];

            try
            {
                // Setup the stream
                int bytesWritten = Encoding.UTF8.GetBytes(value, 0, value.Length, rentedArray, 0);
                using var memoryStream = new MemoryStream(rentedArray, 0, bytesWritten);

                // Deserialize
                var result = await JsonSerializer.DeserializeAsync<T>(memoryStream);
                if (result is not null)
                {
                    UseMemoryCacheIfEnabled(kvp.Key, value);
                    return result;
                }

                return default;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.GetBaseException(), "Failed to deserialize the value retrieved from cache with key {key}.", kvp.Key);
                return default;
            }
            finally
            {
                _arrayPool?.Return(rentedArray);
            }
        }

        private async Task<T?> GetDeserializedValueAsync(string key, object value)
        {
            if (value is RedisValue redisValue && redisValue.HasValue)
            {
                // Estimate the size of the byte array needed
                string json = redisValue.ToString();
                int estimatedSize = Encoding.UTF8.GetByteCount(json);
                byte[] rentedArray = _arrayPool is not null
                    ? _arrayPool.Rent(estimatedSize)
                    : new byte[estimatedSize];

                try
                {
                    // Setup the stream
                    int bytesWritten = Encoding.UTF8.GetBytes(json, 0, json.Length, rentedArray, 0);
                    using var memoryStream = new MemoryStream(rentedArray, 0, bytesWritten);

                    // Deserialize
                    var result = await JsonSerializer.DeserializeAsync<T>(memoryStream);
                    if (result is not null)
                    {
                        UseMemoryCacheIfEnabled(key, json);
                        return result;
                    }

                    return default;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex.GetBaseException(), "Failed to deserialize the value retrieved from cache with key {key}.", key);
                    return default;
                }
                finally
                {
                    _arrayPool?.Return(rentedArray);
                }
            }
            else
            {
                return default;
            }
        }

        private async Task<T?> GetDeserializedValueAsync(KeyValuePair<string, string> kvp)
        {
            // Estimate the size of the byte array needed
            int estimatedSize = Encoding.UTF8.GetByteCount(kvp.Value);
            byte[] rentedArray = _arrayPool is not null
                ? _arrayPool.Rent(estimatedSize)
                : new byte[estimatedSize];

            try
            {
                // Setup the stream
                int bytesWritten = Encoding.UTF8.GetBytes(kvp.Value, 0, kvp.Value.Length, rentedArray, 0);
                using var memoryStream = new MemoryStream(rentedArray, 0, bytesWritten);
                
                // Deserialize
                var result = await JsonSerializer.DeserializeAsync<T>(memoryStream);
                if (result is not null)
                {
                    UseMemoryCacheIfEnabled(kvp.Key, kvp.Value);
                    return result;
                }

                return default;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.GetBaseException(), "Failed to deserialize the value retrieved from cache with key {key}.", kvp.Key);
                return default;
            }
            finally
            {
                _arrayPool?.Return(rentedArray);
            }
        }

        // Memory cache helper
        private void UseMemoryCacheIfEnabled(string key, string? value)
        {
            if (!_settings.UseMemoryCache) return;

            ArgumentNullException.ThrowIfNull(key, nameof(key));
            ArgumentNullException.ThrowIfNull(value, nameof(value));
            
            _memCache.AddOrUpdate(
                key,
                value,
                TimeSpan.FromMinutes(_settings.InMemoryAbsoluteExpiration)
            );
        }
    }
}