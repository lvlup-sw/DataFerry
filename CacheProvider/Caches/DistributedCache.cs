//using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Wrap;
using StackExchange.Redis;
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
        private readonly CacheSettings _settings;
        private readonly AsyncPolicyWrap<object> _policy;
        private readonly IConnectionMultiplexer _cache;

        /// <summary>
        /// The primary constructor for the <see cref="DistributedCache"/> class.
        /// </summary>
        /// <param name="settings">The settings for the cache.</param>
        /// <exception cref="ArgumentNullException"></exception>""
        public DistributedCache(IConnectionMultiplexer cache, CacheSettings settings)
        {
            ArgumentNullException.ThrowIfNull(cache);
            ArgumentNullException.ThrowIfNull(settings.ConnectionString);

            _cache = cache;
            _settings = settings;
            _policy = CreatePolicy(_settings);
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
                RedisValue data = await database.StringGetAsync(key);
                return data.HasValue ? data : default;
            });

            return result is RedisValue typeResult 
                ? JsonSerializer.Deserialize<T>(typeResult.ToString()) 
                : default;
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
                return await database.StringSetAsync(key, JsonSerializer.SerializeToUtf8Bytes(item));
            });

            return result is bool success
                ? success
                : default;
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
                return await database.KeyDeleteAsync(key);
            });

            return result is bool success
                ? success
                : default;
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
        /// <param name="settings">The settings for the cache.</param>
        private static AsyncPolicyWrap<object> CreatePolicy(CacheSettings settings)
        {
            // TODO: Add logging using ILogger

            // Retry policy: RetryCount times with RetryInterval seconds delay
            var retryPolicy = Policy<object>
                .Handle<Exception>()
                .WaitAndRetryAsync(
                    retryCount: settings.RetryCount,
                    sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(settings.RetryInterval),
                    onRetry: (exception, timeSpan, retryCount, context) =>
                    {
                        if (retryCount == settings.RetryCount)
                        {
                            Console.WriteLine($"Retry limit of {settings.RetryCount} reached. Exception: {exception}");
                        }
                        else
                        {
                            Console.WriteLine($"Retry {retryCount} of {settings.RetryCount} after {timeSpan.TotalSeconds} seconds delay due to: {exception}");
                        }
                    });

            // Fallback policy: Execute fallback action if an exception is thrown
            var fallbackPolicy = Policy<object>
                .Handle<Exception>()
                .FallbackAsync(
                    fallbackValue: RedisValue.Null,
                    onFallbackAsync: (exception, context) =>
                    {
                        Console.WriteLine($"Fallback executed due to: {exception}");
                        return Task.CompletedTask;
                    });

            return Policy.WrapAsync(retryPolicy, fallbackPolicy);
        }
    }
}