using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Wrap;
using System.Text.Json;

namespace CacheObject.Caches
{
    /// <summary>
    /// An implementation of <see cref="IDistributedCache"/> which uses the <see cref="ICache"/> interface as a base.
    /// </summary>
    /// <remarks>
    /// This can be used with numerous distributed cache providers such as Redis, AWS ElastiCache, or Azure Blob Storage.
    /// <typeparam name="T"></typeparam>
    public class DistributedCache<T> : ICache<T>
    {
        private readonly IDistributedCache _cache;
        private readonly ILogger _logger;
        private readonly CacheSettings _settings;
        private readonly IAsyncPolicy<object> _policy;

        public DistributedCache(IDistributedCache cache, ILogger logger, IOptions<CacheSettings> settings)
        {
            _cache = cache ?? throw new ArgumentNullException(nameof(cache));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _settings = settings.Value ?? throw new ArgumentNullException(nameof(settings));
            _policy = CreatePolicy(settings) ?? throw new ArgumentNullException(nameof(AsyncPolicyWrap));
        }

        /// <summary>
        /// Retrieves an item from the cache using a key.
        /// </summary>
        /// <param name="key">The key of the item to retrieve.</param>
        public T GetItem(string key)
        {
            var itemBytes = _cache.Get(key);
            if (itemBytes is null)
            {
                _logger.LogWarning($"Record with key {key} not found in cache");
                return default!;
            }

            var item = JsonSerializer.Deserialize<T>(itemBytes);
            return item!;
        }

        /// <summary>
        /// Asynchronously retrieves an item from the cache using a key.
        /// </summary>
        /// <param name="key">The key of the item to retrieve.</param>
        public async Task<T> GetItemAsync(string key)
        {
            Context context = new($"DistributedCache.GetRecordAsync for {key}");
            var itemBytes = await _policy.ExecuteAsync(ctx 
                => _cache.GetAsync(key, CancellationToken.None).ContinueWith(t 
                => (object)t.Result! 
                ?? throw new InvalidOperationException("Cache returned null")), context);

            if (itemBytes is null)
            {
                _logger.LogWarning($"Record with key {key} not found in cache");
                return default!;
            }

            var item = JsonSerializer.Deserialize<T>(itemBytes as byte[]);
            return item!;
        }

        /// <summary>
        /// Asynchronously adds an item to the cache with a specified key.
        /// </summary>
        /// <param name="key">The key to use for the item.</param>
        /// <param name="item">The item to add to the cache.</param>
        public async Task SetItemAsync(string key, T item)
        {
            var itemBytes = JsonSerializer.SerializeToUtf8Bytes(item);
            await _cache.SetAsync(key, itemBytes);
        }

        /// <summary>
        /// Asynchronously removes an item from the cache using a key.
        /// </summary>
        /// <param name="key">The key of the item to remove.</param>
        public async Task RemoveItemAsync(string key) => await _cache.RemoveAsync(key);

        /// <summary>
        /// Retrieves the count of items in the cache.
        /// </summary>
        [Obsolete("This method is not supported by IDistributedCache.")]
        public int GetItemCount()
        {
            // Note: IDistributedCache doesn't support getting the count of items in the cache.
            // This is a placeholder implementation and may not work as expected.
            _logger.LogWarning("GetItemCount is not supported by IDistributedCache.");
            return 0;
        }

        /// <summary>
        /// Retrieves all items from the cache.
        /// </summary>
        [Obsolete("This method is not supported by IDistributedCache.")]
        public object GetItems()
        {
            // Note: IDistributedCache doesn't support getting all items in the cache.
            // This is a placeholder implementation and may not work as expected.
            _logger.LogWarning("GetItems is not supported by IDistributedCache.");
            return new object();
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
                    fallbackValue: new object(), // Replace this with your fallback value
                    onFallbackAsync: async (exception, context) =>
                    {
                        _logger.LogError($"Fallback executed due to: {exception}");
                    });

            return Policy.WrapAsync(retryPolicy, fallbackPolicy);
        }
    }
}
