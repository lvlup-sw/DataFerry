﻿using StackExchange.Redis;
using Polly.Wrap;
using Polly;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace DataFerry.Caches
{
    /// <summary>
    /// An implementation of <see cref="ConnectionMultiplexer"/> which uses the <see cref="IDistributedCache"/> interface as a base. Polly is integrated overtop for handling exceptions and retries.
    /// </summary>
    /// <remarks>
    /// This can be used with numerous Redis cache providers such as AWS ElastiCache or Azure Cache for Redis.
    /// </remarks>
    public class DistributedCache<T> : IDistributedCache<T> where T : class
    {
        private readonly IConnectionMultiplexer _cache;
        private readonly IFastMemCache<string, string> _memCache;
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

            return GetDeserializedValue(key, result) ?? default;
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
                    var value = await GetDeserializedValue(task);
                    return (task.Key, value);
                })
            ).ConfigureAwait(false))
            .Where(kvp => kvp.value is not null)
            .ToDictionary(kvp => kvp.Key, kvp => kvp.value!);

            // Return if we don't have any memCache results
            if (!memCacheHits.Any()) return redisResults;

            // Otherwise, we need to build the dict with the memCache results
            Dictionary<string, T> memResults = GetDeserializedDictionary(memCacheHits);

            // Concat and return
            return redisResults
                .Concat(memResults)
                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
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
                    var value = await GetDeserializedValue(task);
                    return (task.Key, value);
                })
            ).ConfigureAwait(false))
            .Where(kvp => kvp.value is not null)
            .ToDictionary(kvp => kvp.Key, kvp => kvp.value!);

            return redisResults;
        }

        private Dictionary<string, T> GetDeserializedDictionary(Dictionary<string, string> serializedDictionary)
        {
            return serializedDictionary
                .Select(task =>
                {
                    var value = GetDeserializedValue(task);
                    return (task.Key, value);
                })
                .Where(kvp => kvp.value is not null)
                .ToDictionary(kvp => kvp.Key, kvp => kvp.value!);
        }

        // Deserialization helper methods
        private T? GetDeserializedValue(string key, object value)
        {
            try
            {
                if (value is RedisValue redisValue && redisValue.HasValue)
                {
                    T? result = JsonSerializer.Deserialize<T>(redisValue.ToString());

                    if (result is not null)
                    {
                        UseMemoryCacheIfEnabled(key, result);
                        return result;
                    }
                }

                return default;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.GetBaseException(), "Failed to deserialize the value retrieved from cache with key {key}.", key);
                return default;
            }
        }

        private T? GetDeserializedValue(KeyValuePair<string, string> kvp)
        {
            try
            {
                T? result = JsonSerializer.Deserialize<T>(kvp.Value);

                if (result is not null)
                {
                    UseMemoryCacheIfEnabled(kvp.Key, result);
                    return result;
                }

                return default;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.GetBaseException(), "Failed to deserialize the value retrieved from cache with key {key}.", kvp.Key);
                return default;
            }
        }

        private async Task<T?> GetDeserializedValue(KeyValuePair<string, Task<RedisValue>> kvp)
        {
            try
            {
                RedisValue value = await kvp.Value.ConfigureAwait(false);
                T? result = JsonSerializer.Deserialize<T>(value.ToString());

                if (result is not null)
                {
                    UseMemoryCacheIfEnabled(kvp.Key, result);
                    return result;
                }

                return default;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.GetBaseException(), "Failed to deserialize the value retrieved from cache with key {key}.", kvp.Key);
                return default;
            }
        }

        // Memory cache helper
        private void UseMemoryCacheIfEnabled(string key, T result)
        {
            if (!_settings.UseMemoryCache) return;

            ArgumentNullException.ThrowIfNull(key, nameof(key));
            ArgumentNullException.ThrowIfNull(result, nameof(result));
            
            _memCache.AddOrUpdate(
                key, 
                JsonSerializer.Serialize(result), 
                TimeSpan.FromMinutes(_settings.InMemoryAbsoluteExpiration)
            );
        }
    }
}