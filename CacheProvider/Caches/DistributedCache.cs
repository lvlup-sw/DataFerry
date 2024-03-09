using Polly;
using Polly.Wrap;
using StackExchange.Redis;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace CacheProvider.Caches
{
    /// <summary>
    /// An implementation of <see cref="ConnectionMultiplexer"/> which uses the <see cref="ICache"/> interface as a base. Polly is integrated overtop for handling exceptions and retries.
    /// </summary>
    /// <remarks>
    /// This can be used with numerous Redis cache providers such as AWS ElastiCache or Azure Blob Storage.
    /// </remarks>
    public class DistributedCache : IDistributedCache
    {
        private readonly IConnectionMultiplexer _cache;
        private readonly CacheSettings _settings;
        private readonly ILogger _logger;
        private AsyncPolicyWrap<object> _policy;

        /// <summary>
        /// The primary constructor for the <see cref="DistributedCache"/> class.
        /// </summary>
        /// <param name="settings">The settings for the cache.</param>
        /// <exception cref="ArgumentNullException"></exception>""
        public DistributedCache(IConnectionMultiplexer cache, CacheSettings settings, ILogger logger)
        {
            ArgumentNullException.ThrowIfNull(cache);
            ArgumentNullException.ThrowIfNull(settings.ConnectionString);
            ArgumentNullException.ThrowIfNull(logger);

            _cache = cache;
            _settings = settings;
            _logger = logger;
            _policy = CreatePolicy();
        }

        /// <summary>
        /// Asynchronously retrieves an item from the cache using a key.
        /// </summary>
        /// <remarks>
        /// Returns the item if it exists in the cache, null otherwise.
        /// </remarks>
        /// <param name="key">The key of the item to retrieve.</param>
        public async Task<T?> GetAsync<T>(string key)
        {
            IDatabase database = _cache.GetDatabase();

            object result = await _policy.ExecuteAsync(async () =>
            {
                _logger.LogInformation("Attempting to retrieve item with key {key} from cache.", key);
                RedisValue data = await database.StringGetAsync(key);
                return data.HasValue ? data : default;
            });

            return result is RedisValue typeResult
                ? LogAndReturnForGet<T>(typeResult, key)
                : LogAndReturnForGet<T>(RedisValue.Null, key);
        }

        /// <summary>
        /// Asynchronously adds an item to the cache with a specified key.
        /// </summary>
        /// <remarks>
        /// Returns true if the item was added to the cache, false otherwise.
        /// </remarks>
        /// <param name="key">The key to use for the item.</param>
        /// <param name="item">The item to add to the cache.</param>
        public async Task<bool> SetAsync<T>(string key, T item)
        {
            IDatabase database = _cache.GetDatabase();

            object result = await _policy.ExecuteAsync(async () =>
            {
                _logger.LogInformation("Attempting to add item with key {key} to cache.", key);
                return await database.StringSetAsync(key, JsonSerializer.Serialize(item));
            });

            return result is bool success
                ? LogAndReturnForSet(key, success)
                : LogAndReturnForSet(key, default);
        }

        /// <summary>
        /// Asynchronously removes an item from the cache using a key.
        /// </summary>
        /// <remarks>
        /// Returns true if the item was removed from the cache, false otherwise.
        /// </remarks>
        /// <param name="key">The key of the item to remove.</param>
        public async Task<bool> RemoveAsync(string key)
        {
            IDatabase database = _cache.GetDatabase();

            object result = await _policy.ExecuteAsync(async () =>
            {
                _logger.LogInformation("Attempting to remove item with key {key} from cache.", key);
                return await database.KeyDeleteAsync(key);
            });

            return result is bool success
                ? LogAndReturnForRemove(key, success)
                : LogAndReturnForRemove(key, default);
        }

        /// <summary>
        /// Batch operation for getting multiple Items from cache
        /// </summary>
        /// <typeparam name="T">The type of the data to retrieve from the cache.</typeparam>
        /// <param name="keys">The keys associated with the data in the cache.</param>
        /// <returns>A dictionary of the retrieved Items. If a key does not exist, its value in the dictionary will be default(<typeparamref name="T"/>).</returns>
        public async Task<Dictionary<string, T>> GetBatchAsync<T>(IEnumerable<string> keys)
        {
            IDatabase database = _cache.GetDatabase();
            IBatch batch = database.CreateBatch();

            // Setup polly to retry the entire operation if anything fails
            object policyExecutionResult = await _policy.ExecuteAsync(
                async (c) =>
                {
                    // We use a dictionary to store the tasks and their associated keys since we need the values
                    var tasks = keys.ToDictionary(key => key, key => batch.StringGetAsync(key, CommandFlags.PreferReplica));

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
                new Context($"DistributedCache.GetBatchAsync for {keys}")
            );

            return policyExecutionResult as Dictionary<string, T>;
        }

        /// <summary>
        /// Batch operation for setting multiple Items in cache
        /// </summary>
        /// <typeparam name="T">The type of the data to store in the cache.</typeparam>
        /// <param name="data">A dictionary containing the keys and data to store in the cache.</param>
        /// <param name="absoluteExpireTime">The absolute expiration time for the data. If this is null, the default expiration time is used.</param>
        /// <returns>True if all Items were set successfully; otherwise, false.</returns>
        public async Task<bool> SetBatchAsync<T>(Dictionary<string, T> data, TimeSpan? absoluteExpireTime = null)
        {
            TimeSpan absoluteExpiration = absoluteExpireTime ?? TimeSpan.FromHours(_settings.AbsoluteExpiration);
            IDatabase database = _cache.GetDatabase();
            IBatch batch = database.CreateBatch();

            // Setup polly to retry the entire operation if anything fails
            object policyExecutionResult = await _policy.ExecuteAsync(
                async (c) =>
                {
                    // We use a list to store the tasks since we're just adding bools
                    var tasks = data.Select(kv => new
                    {
                        kv.Key,
                        Task = batch.StringSetAsync(
                            kv.Key,
                            JsonSerializer.Serialize(kv.Value),
                            absoluteExpiration
                        )
                    }).ToList();

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
                new Context($"DistributedCache.SetBatchAsync for {string.Join(", ", data.Keys)}")
            );

            return policyExecutionResult is bool success
                ? success
                : default;
        }

        /// <summary>
        /// Batch operation for removing multiple Items from cache
        /// </summary>
        /// <param name="keys">The keys associated with the data in the cache.</param>
        /// <returns>True if all Items were removed successfully; otherwise, false.</returns>
        public async Task<bool> RemoveBatchAsync(IEnumerable<string> keys)
        {
            IDatabase database = _cache.GetDatabase();
            IBatch batch = database.CreateBatch();

            // Setup polly to retry the entire operation if anything fails
            object policyExecutionResult = await _policy.ExecuteAsync(
                async (c) =>
                {
                    // We use a list to store the tasks since we're just adding bools
                    var tasks = keys.Select(key => new
                    {
                        Key = key,
                        Task = batch.KeyDeleteAsync(key)
                    }).ToList();

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
                new Context($"DistributedCache.RemoveBatchAsync for {string.Join(", ", keys)}")
            );

            return policyExecutionResult is bool success
                ? success
                : default;
        }

        /// <summary>
        /// Retrieves an <see cref="IDatabase"/> representation of the cache.
        /// </summary>
        public IDatabase GetCache() => _cache.GetDatabase();

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
                _logger.LogInformation(message);
            else
                _logger.LogError(message);

            try
            {
                return success ? JsonSerializer.Deserialize<T>(value.ToString()) : default;
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
                _logger.LogInformation(message);
            else 
                _logger.LogError(message);

            return success;
        }

        private bool LogAndReturnForRemove(string key, bool success)
        {
            string message = success
                ? $"RemoveAsync operation completed for key: {key}"
                : $"RemoveAsync operation failed for key: {key}";

            if (success)
                _logger.LogInformation(message);
            else
                _logger.LogError(message);

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
                _logger.LogInformation(message);
            else
                _logger.LogWarning(message);

            // We handle the deserialization here, so we need a try-catch
            try
            {
                return success ? JsonSerializer.Deserialize<T>(value.ToString()) : default;
            }
            catch (JsonException e)
            {
                _logger.LogError(e, "Failed to deserialize the value retrieved from cache with key {key}.", task.Key);
                return default;
            }
        }
    }
}