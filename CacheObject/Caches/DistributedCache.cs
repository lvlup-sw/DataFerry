using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Wrap;
using StackExchange.Redis;
using System.Text.Json;

namespace CacheObject.Caches
{
    /// <summary>
    /// An implementation of <see cref="IDistributedCache"/> which uses the <see cref="ICache{T}"/> interface as a base.
    /// </summary>
    /// <remarks>
    /// This can be used with numerous distributed cache providers such as Redis, AWS ElastiCache, or Azure Blob Storage.
    /// </remarks>
    /// <typeparam name="T"></typeparam>
    public class DistributedCache<T> : ICache<T>
    {
        private readonly IDistributedCache _cache;
        private readonly ILogger _logger;
        private readonly CacheSettings _settings;
        private readonly AsyncPolicyWrap<object> _policy;
        private readonly T _default;

        public DistributedCache(IDistributedCache cache, ILogger logger, IOptions<CacheSettings> settings, T @default)
        {
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _settings = settings.Value ?? throw new ArgumentNullException(nameof(settings));
            _policy = CreatePolicy(settings) ?? throw new ArgumentNullException(nameof(AsyncPolicyWrap));
            _default = @default;
        }

        /// <summary>
        /// Retrieves an item from the cache using a key.
        /// </summary>
        /// <param name="key">The key of the item to retrieve.</param>
        public T? GetItem(string key)
        {
            var itemBytes = _cache.Get(key);
            if (itemBytes is null)
            {
                _logger.LogWarning($"Record with key {key} not found in cache");
                return default;
            }

            return JsonSerializer.Deserialize<T>(itemBytes);
        }

        /// <summary>
        /// Asynchronously retrieves an item from the cache using a key.
        /// </summary>
        /// <param name="key">The key of the item to retrieve.</param>
        public async Task<T?> GetItemAsync(string key)
        {
            try
            {
                var itemBytes = await _cache.GetAsync(key, CancellationToken.None);

                if (itemBytes is null)
                {
                    _logger.LogWarning($"Record with key {key} not found in cache");
                    return default;
                }

                return JsonSerializer.Deserialize<T>(itemBytes);
            }
            catch (Exception ex)
            {
                _logger.LogError($"An error occurred while getting the record with key {key}: {ex.Message}");
                return default;
            }
        }

        /// <summary>
        /// Asynchronously adds an item to the cache with a specified key.
        /// </summary>
        /// <param name="key">The key to use for the item.</param>
        /// <param name="item">The item to add to the cache.</param>
        public async Task SetItemAsync(string key, T item) => await _cache.SetAsync(key, JsonSerializer.SerializeToUtf8Bytes(item));

        /// <summary>
        /// Asynchronously removes an item from the cache using a key.
        /// </summary>
        /// <param name="key">The key of the item to remove.</param>
        public async Task RemoveItemAsync(string key) => await _cache.RemoveAsync(key);

        /// <summary>
        /// Retrieves an object representation of the cache.
        /// </summary>
        /// <remarks>
        /// In this case, the cache object returned is a Redis <see cref="IDatabase"/>.
        /// </remarks>
        /// <exception cref="ArgumentNullException"></exception>"
        public object GetCache() => ConnectToCache() ?? throw new NullReferenceException(nameof(IDatabase));

        private IDatabase? ConnectToCache()
        {
            ConnectionMultiplexer _connectionMultiplexer;
            IDatabase _database;
            try
            {
                _connectionMultiplexer = ConnectionMultiplexer.Connect(_settings.ConnectionString!) ?? throw new ArgumentNullException("Invalid ConnectionString", nameof(ConnectionMultiplexer));
                _database = _connectionMultiplexer.GetDatabase() ?? throw new ArgumentNullException(nameof(IDatabase));
                return _database;
            }
            catch (Exception ex)
            {
                _logger.LogError($"An error occurred while connecting to the cache: {ex.Message}");
            }

            return null;
        }

        /// <summary>
        /// Creates a policy for handling exceptions when accessing the cache.
        /// </summary>
        /// <param name="settings">The settings for the cache.</param>
        private AsyncPolicyWrap<object> CreatePolicy(IOptions<CacheSettings> settings)
        {
            // Retry policy: RetryCount times with RetryInterval seconds delay
            var retryPolicy = Policy<object>
                .Handle<Exception>()
                .WaitAndRetryAsync(
                    retryCount: settings.Value.RetryCount,
                    sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(settings.Value.RetryInterval),
                    onRetry: (exception, timeSpan, retryCount, context) =>
                    {
                        if (retryCount == settings.Value.RetryCount)
                        {
                            _logger.LogError($"Retry limit of {settings.Value.RetryCount} reached. Exception: {exception}");
                        }
                        else
                        {
                            _logger.LogWarning($"Retry {retryCount} of {settings.Value.RetryCount} after {timeSpan.TotalSeconds} seconds delay due to: {exception}");
                        }
                    });

            // Fallback policy: Execute fallback action if an exception is thrown
            var fallbackPolicy = Policy<object>
                .Handle<Exception>()
                .FallbackAsync(
                    fallbackValue: _default!,
                    onFallbackAsync: (exception, context) =>
                    {
                        _logger.LogError($"Fallback executed due to: {exception}");
                        return Task.CompletedTask;
                    });

            return Policy.WrapAsync(retryPolicy, fallbackPolicy);
        }
    }
}