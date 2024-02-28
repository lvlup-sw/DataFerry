using Microsoft.Extensions.Caching.Distributed;
//using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Wrap;
using StackExchange.Redis;
using System.Text.Json;

namespace CacheProvider.Caches
{
    /// <summary>
    /// An implementation of <see cref="IDistributedCache"/> which uses the <see cref="ICache"/> interface as a base. Polly is overtop for handling exceptions and retries.
    /// </summary>
    /// <remarks>
    /// This can be used with numerous distributed cache providers such as Redis, AWS ElastiCache, or Azure Blob Storage.
    /// </remarks>
    public class DistributedCache : ICache
    {
        private readonly CacheSettings _settings;
        private readonly AsyncPolicyWrap<object> _policy;
        private readonly IDatabase _cache;

        /// <summary>
        /// The primary constructor for the <see cref="DistributedCache"/> class.
        /// </summary>
        /// <param name="settings">The settings for the cache.</param>
        /// <exception cref="ArgumentNullException"></exception>""
        public DistributedCache(IOptions<CacheSettings> settings)
        {
            ArgumentNullException.ThrowIfNull(settings.Value.ConnectionString);

            _settings = settings.Value;
            _policy = CreatePolicy(settings);
            _cache = ConnectionMultiplexer.Connect(_settings.ConnectionString).GetDatabase();
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
            object result = await _policy.ExecuteAsync(async () =>
            {
                var value = await _cache.StringGetAsync(key);
                if (value.IsNullOrEmpty)
                    return default!;

                return JsonSerializer.Deserialize<T>(value.ToString())!;
            });

            return default;
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
            object result = await _policy.ExecuteAsync(async () =>
            {
                return await _cache.StringSetAsync(key, JsonSerializer.SerializeToUtf8Bytes(item));
            });

            return default;
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
            object result = await _policy.ExecuteAsync(async () =>
            {
                return await _cache.KeyDeleteAsync(key);
            });

            return default;
        }

        /// <summary>
        /// Retrieves an object representation of the cache.
        /// </summary>
        /// <remarks>
        /// In this case, the cache object returned is a Redis <see cref="IDatabase"/>.
        /// </remarks>
        /// <exception cref="ArgumentNullException"></exception>"
        public object GetCache() => _cache ?? throw new NullReferenceException(nameof(_cache));

        /// <summary>
        /// Creates a policy for handling exceptions when accessing the cache.
        /// </summary>
        /// <param name="settings">The settings for the cache.</param>
        private static AsyncPolicyWrap<object> CreatePolicy(IOptions<CacheSettings> settings)
        {
            // ToDo: Add logging using ILogger

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
                            Console.WriteLine($"Retry limit of {settings.Value.RetryCount} reached. Exception: {exception}");
                        }
                        else
                        {
                            Console.WriteLine($"Retry {retryCount} of {settings.Value.RetryCount} after {timeSpan.TotalSeconds} seconds delay due to: {exception}");
                        }
                    });

            // Fallback policy: Execute fallback action if an exception is thrown
            var fallbackPolicy = Policy<object>
                .Handle<Exception>()
                .FallbackAsync(
                    fallbackValue: new RedisValue(),
                    onFallbackAsync: (exception, context) =>
                    {
                        Console.WriteLine($"Fallback executed due to: {exception}");
                        return Task.CompletedTask;
                    });

            return Policy.WrapAsync(retryPolicy, fallbackPolicy);
        }
    }
}