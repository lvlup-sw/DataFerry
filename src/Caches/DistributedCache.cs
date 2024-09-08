using StackExchange.Redis;
using Microsoft.Extensions.Caching.Memory;
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
    /// This can be used with numerous Redis cache providers such as AWS ElastiCache or Azure Blob Storage.
    /// </remarks>
    public class DistributedCache<T> : IDistributedCache<T> where T : class
    {
        private readonly IConnectionMultiplexer _cache;
        private readonly IFastMemCache<string, T> _memCache;
        private readonly CacheSettings _settings;
        private readonly ILogger _logger;
        private AsyncPolicyWrap<object> _policy;

        /// <summary>
        /// The primary constructor for the <see cref="DistributedCache{T}"/> class.
        /// </summary>
        /// <param name="settings">The settings for the cache.</param>
        /// <exception cref="ArgumentNullException"></exception>""
        public DistributedCache(IConnectionMultiplexer cache, IFastMemCache<string, T> memCache, IOptions<CacheSettings> settings, ILogger logger)
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
            if (_settings.UseMemoryCache && _memCache.TryGet(key, out T? memData))
            {
                _logger.LogDebug("Retrieved data with key {key} from memory cache.", key);
                return memData;
            }

            // If the key does not exist in the _memCache, proceed with the Polly policy execution
            IDatabase database = _cache.GetDatabase();

            object result = await _policy.ExecuteAsync(async (context) =>
            {
                _logger.LogDebug("Attempting to retrieve entry with key {key} from cache.", key);
                RedisValue data = await database.StringGetAsync(key, CommandFlags.PreferReplica);
                return data.HasValue ? data : default;
            }, new Context($"DistributedCache.GetAsync for {key}"));

            return result is RedisValue typeResult
                ? LogAndReturnForGet(typeResult, key)
                : LogAndReturnForGet(RedisValue.Null, key);
        }

        /// <summary>
        /// Asynchronously adds an entry to the cache with a specified key.
        /// </summary>
        /// <remarks>
        /// Returns true if the entry was added to the cache, false otherwise.
        /// </remarks>
        /// <param name="key">The key to use for the entry.</param>
        /// <param name="data">The data to add to the cache.</param>
        public async Task<bool> SetInCacheAsync(string key, T data, TimeSpan? timeSpan = null)
        {
            IDatabase database = _cache.GetDatabase();
            if (_settings.UseMemoryCache)
            {
                TimeSpan expiration = _settings.InMemoryAbsoluteExpiration != 0
                    ? TimeSpan.FromMinutes(_settings.InMemoryAbsoluteExpiration)
                    : TimeSpan.FromMinutes(120);

                _memCache.AddOrUpdate(key, data, expiration);
            }

            object result = await _policy.ExecuteAsync(async (context) =>
            {
                _logger.LogDebug("Attempting to add entry with key {key} to cache.", key);
                return await database.StringSetAsync(key, JsonSerializer.Serialize(data), timeSpan);
            }, new Context($"DistributedCache.SetAsync for {key}"));

            return result is bool success
                ? LogAndReturnForSet(key, success)
                : LogAndReturnForSet(key, default);
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
                return await database.KeyDeleteAsync(key);
            }, new Context($"DistributedCache.RemoveAsync for {key}"));

            return result is bool success
                ? LogAndReturnForRemove(key, success)
                : LogAndReturnForRemove(key, default);
        }

        /// <summary>
        /// Batch operation for getting multiple entries from cache
        /// </summary>
        /// <typeparam name="T">The type of the data to retrieve from the cache.</typeparam>
        /// <param name="keys">The keys associated with the data in the cache.</param>
        /// <returns>A dictionary of the retrieved entries. If a key does not exist, its value in the dictionary will be default(<typeparamref name="T"/>).</returns>
        public async Task<Dictionary<string, T>> GetBatchFromCacheAsync(IEnumerable<string> keys, CancellationToken? cancellationToken = null)
        {
            IDatabase database = _cache.GetDatabase();
            IBatch batch = database.CreateBatch();

            // Setup polly to retry the entire operation if anything fails
            object policyExecutionResult = await _policy.ExecuteAsync(
                async (c, ct) =>
                {
                    // We use a dictionary to store the tasks and their associated keys since we need the values
                    // Also, we only want to retrieve the keys that don't exist in the _memCache
                    var tasks = keys
                        .Where(key => _settings.UseMemoryCache && !_memCache.TryGet(key, out _))
                        .ToDictionary(key => key, key => batch.StringGetAsync(key, CommandFlags.PreferReplica));

                    // Execute the batch and wait for all the tasks to complete
                    batch.Execute();
                    // Note we don't capture the sync context here to avoid deadlocks
                    await Task.WhenAll(tasks.Values).ConfigureAwait(false);

                    // Check each task result and deserialize the value if it exists
                    var results = await Task.WhenAll(
                        tasks.Select(async task => new
                        {
                            task.Key,
                            Value = await LogAndReturnForGetBatchAsync(task)
                        })
                    );
                    return results.ToDictionary(result => result.Key, result => result.Value);
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
        public async Task<bool> SetBatchInCacheAsync(Dictionary<string, T> data, TimeSpan? absoluteExpireTime = null, CancellationToken? cancellationToken = null)
        {
            TimeSpan absoluteExpiration = absoluteExpireTime ?? TimeSpan.FromHours(_settings.AbsoluteExpiration);
            IDatabase database = _cache.GetDatabase();
            IBatch batch = database.CreateBatch();

            // Setup polly to retry the entire operation if anything fails
            object policyExecutionResult = await _policy.ExecuteAsync(
                async (c, ct) =>
                {
                    // We use a list to store the tasks since we're just adding bools
                    var tasks = data
                        .Where(kv => kv.Value is not null) // Exclude null values
                        .Select(kv => new
                        {
                            kv.Key,
                            Task = batch.StringSetAsync(
                                kv.Key,
                                JsonSerializer.Serialize(kv.Value),
                                absoluteExpiration
                            )
                        }).ToList();

                    // Set items in the memCache
                    if (_settings.UseMemoryCache)
                    {
                        TimeSpan expiration = _settings.InMemoryAbsoluteExpiration != 0
                            ? TimeSpan.FromMinutes(_settings.InMemoryAbsoluteExpiration)
                            : TimeSpan.FromMinutes(120);

                        data.Where(kv => kv.Value is not null).ToList().ForEach(kv => _memCache.AddOrUpdate(kv.Key, kv.Value, expiration));
                    }

                    // Execute the batch and wait for all the tasks to complete
                    batch.Execute();
                    // Note we don't capture the sync context here to avoid deadlocks
                    await Task.WhenAll(tasks.Select(t => t.Task)).ConfigureAwait(false);

                    // Check each task result and log the outcome
                    var results = tasks.ToDictionary(
                        task => task.Key,
                        task => LogAndReturnForSet(task.Key, task.Task.Result)
                    );
                    return results.Values.All(success => success);
                },
                new Context($"DistributedCache.SetBatchAsync for {string.Join(", ", data.Keys)}"),
                cancellationToken ?? default
            );

            return policyExecutionResult is bool success
                ? success
                : default;
        }

        /// <summary>
        /// Batch operation for removing multiple entries from cache.
        /// </summary>
        /// <param name="keys">The keys associated with the data in the cache.</param>
        /// <returns>True if all entries were removed successfully; otherwise, false.</returns>
        public async Task<bool> RemoveBatchFromCacheAsync(IEnumerable<string> keys, CancellationToken? cancellationToken = null)
        {
            IDatabase database = _cache.GetDatabase();
            IBatch batch = database.CreateBatch();

            // Setup polly to retry the entire operation if anything fails
            object policyExecutionResult = await _policy.ExecuteAsync(
                async (c, ct) =>
                {
                    // We use a list to store the tasks since we're just adding bools
                    var tasks = keys.Select(key => new
                    {
                        Key = key,
                        Task = batch.KeyDeleteAsync(key)
                    }).ToList();

                    // Remove items from the memCache
                    if (_settings.UseMemoryCache)
                    {
                        keys.ToList().ForEach(key => _memCache.Remove(key));
                    }

                    // Execute the batch and wait for all the tasks to complete
                    batch.Execute();
                    // Note we don't capture the sync context here to avoid deadlocks
                    await Task.WhenAll(tasks.Select(t => t.Task)).ConfigureAwait(false);

                    // Check each task result and log the outcome
                    var results = tasks.ToDictionary(
                        task => task.Key,
                        task => LogAndReturnForRemove(task.Key, task.Task.Result)
                    );
                    return results.Values.All(success => success);
                },
                new Context($"DistributedCache.RemoveBatchAsync for {string.Join(", ", keys)}"),
                cancellationToken ?? default
            );

            return policyExecutionResult is bool success
                ? success
                : default;
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

        // Logging Methods to simplify return statements
        private T? LogAndReturnForGet(RedisValue value, string key)
        {
            bool success = !value.IsNullOrEmpty;

            string message = success
                ? $"GetAsync operation completed for key: {key}"
                : $"GetAsync operation failed for key: {key}";

            _logger.LogDebug(message);

            try
            {
                T? result = default;

                if (success)
                {
                    result = JsonSerializer.Deserialize<T?>(value.ToString());
                    UseMemoryCacheIfEnabled(key, result);
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.GetBaseException(), "Failed to deserialize the object. Exception: {ex}", ex);
                return default;
            }
        }

        private bool LogAndReturnForSet(string key, bool success)
        {
            string message = success
                ? $"SetAsync operation completed for key: {key}"
                : $"SetAsync operation failed for key: {key}";

            if (success)
                _logger.LogDebug(message);
            else
                _logger.LogWarning(message);

            return success;
        }

        private bool LogAndReturnForRemove(string key, bool success)
        {
            string message = success
                ? $"RemoveAsync operation completed for key: {key}"
                : $"RemoveAsync operation failed for key: {key}";

            if (success)
                _logger.LogDebug(message);
            else
                _logger.LogWarning(message);

            return success;
        }

        private async Task<T?> LogAndReturnForGetBatchAsync(KeyValuePair<string, Task<RedisValue>> task)
        {
            RedisValue value = await task.Value.ConfigureAwait(false);
            bool success = !value.IsNullOrEmpty;
            // Check cache hits in enumerator?

            string message = success
                ? $"GetBatchAsync operation completed from cache with key {task.Key}."
                : $"Nothing found in cache with key {task.Key}.";

            _logger.LogDebug(message);

            // We handle the deserialization here, so we need a try-catch
            try
            {
                T? result = default;

                if (success)
                {
                    result = JsonSerializer.Deserialize<T?>(value.ToString());
                    UseMemoryCacheIfEnabled(task.Key, result);
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.GetBaseException(), "Failed to deserialize the value retrieved from cache with key {key}.", task.Key);
                return default;
            }
        }

        private void UseMemoryCacheIfEnabled(string key, T? result)
        {
            ArgumentNullException.ThrowIfNull(result, nameof(result));

            if (_settings.UseMemoryCache)
            {
                TimeSpan expiration = _settings.InMemoryAbsoluteExpiration != 0
                    ? TimeSpan.FromMinutes(_settings.InMemoryAbsoluteExpiration)
                    : TimeSpan.FromMinutes(120);

                _memCache.AddOrUpdate(key, result, expiration);
            }
        }
    }
}