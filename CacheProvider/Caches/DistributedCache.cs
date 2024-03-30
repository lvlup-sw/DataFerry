using Polly;
using Polly.Wrap;
using StackExchange.Redis;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using CacheProvider.Providers;
using CacheProvider.Caches.Interfaces;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace CacheProvider.Caches
{
    /// <summary>
    /// An implementation of <see cref="ConnectionMultiplexer"/> which uses the <see cref="IDistributedCache"/> interface as a base. Polly is integrated overtop for handling exceptions and retries.
    /// </summary>
    /// <remarks>
    /// This can be used with numerous Redis cache providers such as AWS ElastiCache or Azure Blob Storage.
    /// </remarks>
    public class DistributedCache : IDistributedCache
    {
        private readonly IConnectionMultiplexer _cache;
        private IMemoryCache _memCache;
        private readonly CacheSettings _settings;
        private readonly ILogger _logger;
        private AsyncPolicyWrap<object> _policy;

        /// <summary>
        /// The primary constructor for the <see cref="DistributedCache"/> class.
        /// </summary>
        /// <param name="settings">The settings for the cache.</param>
        /// <exception cref="ArgumentNullException"></exception>""
        public DistributedCache(IConnectionMultiplexer cache, IMemoryCache memCache, IOptions<CacheSettings> settings, ILogger logger)
        {
            ArgumentNullException.ThrowIfNull(cache);
            ArgumentNullException.ThrowIfNull(memCache);
            ArgumentNullException.ThrowIfNull(settings);
            ArgumentNullException.ThrowIfNull(logger);

            _cache = cache;
            _memCache = memCache;
            _settings = settings.Value;
            _logger = logger;
            _policy = CreatePolicy();
        }

        /// <summary>
        /// Asynchronously retrieves an entry from the cache using a key.
        /// </summary>
        /// <remarks>
        /// Returns the entry if it exists in the cache, null otherwise.
        /// </remarks>
        /// <param name="key">The key of the  to retrieve.</param>
        public async Task<T?> GetAsync<T>(string key)
        {
            // Check the _memCache first
            if (_settings.UseMemoryCache && _memCache.TryGetValue(key, out T? memData))
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
                ? LogAndReturnForGet<T>(typeResult, key)
                : LogAndReturnForGet<T>(RedisValue.Null, key);
        }

        /// <summary>
        /// Asynchronously adds an entry to the cache with a specified key.
        /// </summary>
        /// <remarks>
        /// Returns true if the entry was added to the cache, false otherwise.
        /// </remarks>
        /// <param name="key">The key to use for the entry.</param>
        /// <param name="data">The data to add to the cache.</param>
        public async Task<bool> SetAsync<T>(string key, T data, TimeSpan? timeSpan = null)
        {
            IDatabase database = _cache.GetDatabase();
            if (_settings.UseMemoryCache)
            {
                _memCache.Set(key, data);
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
        public async Task<bool> RemoveAsync(string key)
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
        public async Task<Dictionary<string, T?>> GetBatchAsync<T>(IEnumerable<string> keys, CancellationToken? cancellationToken = null)
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
                        .Where(key => _settings.UseMemoryCache && !_memCache.TryGetValue(key, out _))
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
                            Value = await LogAndReturnForGetBatchAsync<T>(task)
                        })
                    );
                    return results.ToDictionary(result => result.Key, result => result.Value);
                },
                new Context($"DistributedCache.GetBatchAsync for {keys}"),
                cancellationToken ?? default
            );

            return policyExecutionResult as Dictionary<string, T?> ?? [];
        }

        /// <summary>
        /// Batch operation for setting multiple entries in cache
        /// </summary>
        /// <typeparam name="T">The type of the data to store in the cache.</typeparam>
        /// <param name="data">A dictionary containing the keys and data to store in the cache.</param>
        /// <param name="absoluteExpireTime">The absolute expiration time for the data. If this is null, the default expiration time is used.</param>
        /// <returns>True if all entries were set successfully; otherwise, false.</returns>
        public async Task<bool> SetBatchAsync<T>(Dictionary<string, T> data, TimeSpan? absoluteExpireTime = null, CancellationToken? cancellationToken = null)
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
                        data.Where(kv => kv.Value is not null).ToList().ForEach(kv => _memCache.Set(kv.Key, kv.Value));
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
        public async Task<bool> RemoveBatchAsync(IEnumerable<string> keys, CancellationToken? cancellationToken = null)
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
        /// Set the fallback value for the polly retry policy.
        /// </summary>
        /// <remarks>Policy will return <see cref="RedisValue.Null"/> if not set.</remarks>
        /// <param name="value"></param>
        public void SetFallbackValue(object value) => _policy = CreatePolicy(value);

        /// <summary>
        /// Creates a policy for handling exceptions when accessing the cache.
        /// </summary>
        /// <param name="_settings">The settings for the cache.</param>
        private AsyncPolicyWrap<object> CreatePolicy(object? configuredValue = null)
        {
            // Retry Policy Settings:
            // + RetryCount: The number of times to retry a cache operation.
            // + RetryInterval: The interval between cache operation retries.
            // + UseExponentialBackoff: Set to true to use exponential backoff for cache operation retries.
            var retryPolicy = Policy<object>
                .Handle<Exception>()
                .WaitAndRetryAsync(
                    retryCount: _settings.RetryCount,
                    // Exponential backoff or fixed interval with jitter
                    sleepDurationProvider: retryAttempt => _settings.UseExponentialBackoff
                        ? TimeSpan.FromSeconds(Math.Pow(2, retryAttempt))
                        : TimeSpan.FromSeconds(_settings.RetryInterval)
                            + TimeSpan.FromMilliseconds(new Random().Next(0, 100)),
                    onRetry: (exception, timeSpan, retryCount, context) =>
                    {
                        if (retryCount == _settings.RetryCount)
                        {
                            _logger.LogError($"Retry limit of {_settings.RetryCount} reached. Exception: {exception}");
                        }
                        else
                        {
                            _logger.LogInformation($"Retry {retryCount} of {_settings.RetryCount} after {timeSpan.TotalSeconds} seconds delay due to: {exception}");
                        }
                    });

            // Fallback Policy Settings:
            // + FallbackValue: The value to return if the fallback action is executed.
            var fallbackPolicy = Policy<object>
                .Handle<Exception>()
                .FallbackAsync(
                    fallbackValue: configuredValue ?? RedisValue.Null,
                    onFallbackAsync: (exception, context) =>
                    {
                        _logger.LogError("Fallback executed due to: {exception}", exception);
                        return Task.CompletedTask;
                    });

            return Policy.WrapAsync(retryPolicy, fallbackPolicy);
        }

        // Logging Methods to simplify return statements
        private T? LogAndReturnForGet<T>(RedisValue value, string key)
        {
            bool success = !value.IsNullOrEmpty;

            string message = success
                ? $"GetAsync operation completed for key: {key}"
                : $"GetAsync operation failed for key: {key}";

            if (success)
                _logger.LogDebug(message);
            else
                _logger.LogInformation(message);

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
            catch (JsonException ex)
            {
                _logger.LogError("Failed to deserialize the object. Exception: {ex}", ex);
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
                _logger.LogInformation(message);

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
                _logger.LogInformation(message);

            return success;
        }

        private async Task<T?> LogAndReturnForGetBatchAsync<T>(KeyValuePair<string, Task<RedisValue>> task)
        {
            RedisValue value = await task.Value.ConfigureAwait(false);
            bool success = !value.IsNullOrEmpty;
            // Check cache hits in enumerator?

            string message = success
                ? $"GetBatchAsync operation completed from cache with key {task.Key}."
                : $"Nothing found in cache with key {task.Key}.";

            if (success)
                _logger.LogDebug(message);
            else
                _logger.LogInformation(message);

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
            catch (JsonException e)
            {
                _logger.LogError(e, "Failed to deserialize the value retrieved from cache with key {key}.", task.Key);
                return default;
            }
        }

        private void UseMemoryCacheIfEnabled<T>(string key, T result)
        {
            if (_settings.UseMemoryCache)
            {
                _memCache.Set(key, result, new MemoryCacheEntryOptions().SetAbsoluteExpiration(TimeSpan.FromSeconds(_settings.AbsoluteExpiration)));
            }
        }
    }
}