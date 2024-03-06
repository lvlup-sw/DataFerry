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
        private readonly AsyncPolicyWrap<object> _policy;

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
        public async Task<T?> GetItemAsync<T>(string key)
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
        public async Task<bool> SetItemAsync<T>(string key, T item)
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
        public async Task<bool> RemoveItemAsync(string key)
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
        /// Retrieves an object representation of the cache.
        /// </summary>
        /// <remarks>
        /// In this case, the cache object returned is a Redis <see cref="IDatabase"/>.
        /// </remarks>
        /// <exception cref="ArgumentNullException"></exception>"
        public object GetCache() => _cache.GetDatabase() ?? throw new NullReferenceException(nameof(_cache));

        /// <summary>
        /// Creates a policy for handling exceptions when accessing the cache.
        /// </summary>
        /// <param name="_settings">The settings for the cache.</param>
        private AsyncPolicyWrap<object> CreatePolicy()
        {
            // Retry policy: RetryCount times with RetryInterval seconds delay
            var retryPolicy = Policy<object>
                .Handle<Exception>()
                .WaitAndRetryAsync(
                    retryCount: _settings.RetryCount,
                    sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(_settings.RetryInterval),
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

            // Fallback policy: Execute fallback action if an exception is thrown
            var fallbackPolicy = Policy<object>
                .Handle<Exception>()
                .FallbackAsync(
                    fallbackValue: RedisValue.Null,
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
                ? $"GetItemAsync operation completed for key: {key}"
                : $"GetItemAsync operation failed for key: {key}";

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
                ? $"SetItemAsync operation completed for key: {key}"
                : $"SetItemAsync operation failed for key: {key}";

            if (success) 
                _logger.LogInformation(message);
            else 
                _logger.LogError(message);

            return success;
        }

        private bool LogAndReturnForRemove(string key, bool success)
        {
            string message = success
                ? $"RemoveItemAsync operation completed for key: {key}"
                : $"RemoveItemAsync operation failed for key: {key}";

            if (success)
                _logger.LogInformation(message);
            else
                _logger.LogError(message);

            return success;
        }
    }
}