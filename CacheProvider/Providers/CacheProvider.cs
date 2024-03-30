using CacheProvider.Caches;
using CacheProvider.Providers.Interfaces;
using MemCache = Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace CacheProvider.Providers
{
    /// <summary>
    /// CacheProvider is a generic class that implements the <see cref="ICacheProvider{T}"/> interface.
    /// </summary>
    /// <remarks>
    /// This class makes use of two types of caches: <see cref="MemoryCache"/> and <see cref="DistributedCache"/>.
    /// It uses the <see cref="IRealProvider{T}>"/> interface to retrieve entries from the real provider.
    /// </remarks>
    /// <typeparam name="T">The type of object to cache.</typeparam>
    public class CacheProvider<T> : ICacheProvider<T> where T : class
    {
        private readonly IRealProvider<T> _realProvider;
        private readonly CacheSettings _settings;
        private readonly ILogger _logger;
        private readonly DistributedCache _cache;

        /// <summary>
        /// Primary constructor for the CacheProvider class.
        /// </summary>
        /// <remarks>
        /// Takes a real provider, cache type, and cache settings as parameters.
        /// </remarks>
        /// <param name="connection">The connection to the Redis server.</param>
        /// <param name="provider">The real provider to use as a data source in the case of cache misses.</param>
        /// <param name="settings">The settings for the cache.</param>
        /// <param name="logger">The logger to use for logging.</param>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="InvalidOperationException"></exception>
        public CacheProvider(IConnectionMultiplexer connection, IRealProvider<T> provider, IOptions<CacheSettings> settings, ILogger logger)
        {
            // Null checks
            ArgumentNullException.ThrowIfNull(connection);
            ArgumentNullException.ThrowIfNull(provider);
            ArgumentNullException.ThrowIfNull(settings);
            ArgumentNullException.ThrowIfNull(logger);

            // Initializations
            _realProvider = provider;
            _settings = settings.Value;
            _logger = logger;
            _cache = new DistributedCache(connection, new MemCache.MemoryCache(new MemoryCacheOptions()), settings, logger);
        }

        /// <summary>
        /// Gets the cache instance.
        /// </summary>
        public DistributedCache Cache => _cache;

        /// <summary>
        /// Asynchronously retrieves an entry from the cache using a specified key.
        /// If the entry is not found in the cache, it retrieves the entry from the real provider and caches it before returning.
        /// </summary>
        /// <param name="key">The key to use for caching the data.</param>
        /// <param name="flag">Optional flag to control cache behavior.</param>
        /// <returns>The cached data.</returns>
        /// <exception cref="ArgumentException">Thrown when the key is null, an empty string, or contains only white-space characters.</exception>
        public async Task<T?> GetFromCacheAsync(string key, GetFlags? flag = null)
        {
            try
            {
                // Null Checks
                ArgumentException.ThrowIfNullOrWhiteSpace(key);

                // Try to get entry from the cache
                var cached = await _cache.GetAsync<T>(key);
                if (cached is not null)
                {
                    _logger.LogDebug("Cached entry with key {key} found in cache.", key);
                    return cached;
                }
                else if (GetFlags.ReturnNullIfNotFoundInCache == flag)
                {
                    _logger.LogDebug("Cached entry with key {key} not found in cache.", key);
                    return null;
                }

                // If not found, get the entry from the real provider
                _logger.LogDebug("Cached entry with key {key} not found in cache. Getting entry from real provider.", key);
                cached = await _realProvider.GetAsync(key);

                // Set the entry in the cache (with refinements)
                if (cached is not null && flag != GetFlags.DoNotSetCacheEntry)
                {
                    if (await _cache.SetAsync(key, cached))
                    {
                        _logger.LogDebug("Entry with key {key} received from real provider and set in cache.", key);
                    }
                    else
                    {
                        _logger.LogWarning("Failed to set entry with key {key} in cache.", key);
                    }
                }
                else if (cached is null)
                {
                    _logger.LogError("Entry with key {key} not received from real provider.", key);
                }

                return cached;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while checking the cache.");
                throw ex.GetBaseException();
            }
        }

        /// <summary>
        /// Asynchronously sets an entry in the cache using a specified key.
        /// </summary>
        /// <param name="key">The key to use for caching the data.</param>
        /// <param name="data">The data to cache.</param>
        /// <returns>True if the operation is successful; otherwise, false.</returns>
        public async Task<bool> SetInCacheAsync(string key, T data, TimeSpan? expiration = null)
        {
            try
            {
                ArgumentNullException.ThrowIfNull(data);
                ArgumentException.ThrowIfNullOrWhiteSpace(key);

                bool result = await _cache.SetAsync(key, data, expiration);
                if (result)
                {
                    _logger.LogDebug("Entry with key {key} set in cache.", key);
                }
                else
                {
                    _logger.LogError("Failed to set entry with key {key} in cache.", key);
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while setting the cache.");
                throw ex.GetBaseException();
            }
        }

        /// <summary>
        /// Asynchronously removes an entry from the cache using a specified key.
        /// </summary>
        /// <param name="key">The key of the entry to remove.</param>
        /// <returns>True if the operation is successful; otherwise, false.</returns>
        public async Task<bool> RemoveFromCacheAsync(string key)
        {
            try
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(key);

                var result = await _cache.RemoveAsync(key);
                if (result)
                {
                    _logger.LogDebug("Entry with key {key} removed from cache.", key);
                }
                else
                {
                    _logger.LogError("Failed to remove entry with key {key} from cache.", key);
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while removing from the cache.");
                throw ex.GetBaseException();
            }
        }

        /// <summary>
        /// Asynchronously retrieves multiple entries from the cache using specified keys.
        /// If any entries are not found in the cache, it retrieves them from the real provider and caches them before returning.
        /// </summary>
        /// <param name="keys">The keys to use for caching the data.</param>
        /// <param name="flags">Optional flags to control cache behavior.</param>
        /// <param name="cancellationToken">Optional cancellation token.</param>
        /// <returns>The cached data.</returns>
        public async Task<IDictionary<string, T?>> GetBatchFromCacheAsync(IEnumerable<string> keys, GetFlags? flags = null, CancellationToken? cancellationToken = null)
        {
            try
            {
                // Null Checks
                foreach (var key in keys)
                {
                    ArgumentException.ThrowIfNullOrEmpty(key);
                }

                Dictionary<string, T?> cached = [];
                // Try to get entries from the cache
                cached = await _cache.GetBatchAsync<T>(keys, cancellationToken);

                // Cache hit scenario
                if (cached.Count > 0)
                {
                    // Extract missing keys
                    var cachedKeys = cached.Keys.ToList();
                    var missingKeys = keys.Except(cachedKeys);

                    // Fetch missing keys from real provider
                    if (missingKeys.Any())
                    {
                        _logger.LogDebug("Cached entries with keys {keys} not found in cache. Getting entries from real provider.", string.Join(", ", missingKeys));

                        var missingData = await _realProvider.GetBatchAsync(missingKeys, cancellationToken);

                        // Update cache selectively
                        if (missingData is not null && missingData.Count > 0 && GetFlags.DoNotSetCacheEntry != flags)
                        {
                            await _cache.SetBatchAsync(missingData, TimeSpan.FromSeconds(_settings.AbsoluteExpiration), cancellationToken);
                            _logger.LogDebug("Entries with keys {keys} received from real provider and set in cache.", string.Join(", ", missingData.Keys));
                        }
                        else if (missingData is null || missingData.Count == 0)
                        {
                            _logger.LogWarning("Entries with keys {keys} not received from real provider.", string.Join(", ", missingKeys));
                        }

                        // Conditionally add missing data to return object
                        cached = (missingData is not null) switch
                        {
                            true  => cached.Concat(missingData).ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
                            false => cached
                        };
                    }

                    _logger.LogDebug("Cached entries with keys {keys} found in cache.", string.Join(", ", cachedKeys));
                    return cached;
                }

                // Cache miss scenario
                // Get the entries from the real provider
                _logger.LogDebug("Cached entries with keys {keys} not found in cache. Getting entries from real provider.", string.Join(", ", keys));
                cached = await _realProvider.GetBatchAsync(keys, cancellationToken);

                if (cached.Count == 0)
                {
                    _logger.LogWarning("Entries with keys {keys} not received from real provider.", string.Join(", ", keys));
                    return cached;
                }
                else if (cached.Count < keys.Count())
                {
                    _logger.LogWarning("Entries with keys {keys} partially received from real provider.", string.Join(", ", keys));
                }

                // Set the entries in the cache
                TimeSpan absoluteExpiration = TimeSpan.FromSeconds(_settings.AbsoluteExpiration);
                if (GetFlags.DoNotSetCacheEntry != flags && await _cache.SetBatchAsync(cached, absoluteExpiration, cancellationToken))
                {
                    _logger.LogDebug("Entries with keys {keys} received from real provider and set in cache.", string.Join(", ", keys));
                }
                else
                {
                    _logger.LogWarning("Failed to set entries with keys {keys} in cache.", string.Join(", ", keys));
                }

                return cached;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while checking the cache.");
                throw ex.GetBaseException();
            }
        }

        /// <summary>
        /// Asynchronously sets multiple entries in the cache using specified keys.
        /// </summary>
        /// <param name="data">The data to cache.</param>
        /// <param name="cancellationToken">Optional cancellation token.</param>
        /// <returns>True if the operation is successful; otherwise, false.</returns>
        public async Task<bool> SetBatchInCacheAsync(Dictionary<string, T> data, CancellationToken? cancellationToken = null)
        {
            try
            {
                ArgumentNullException.ThrowIfNull(data);
                foreach (var key in data.Keys)
                {
                    ArgumentException.ThrowIfNullOrEmpty(key);
                }

                TimeSpan absoluteExpiration = TimeSpan.FromSeconds(_settings.AbsoluteExpiration);
                var result = await _cache.SetBatchAsync(data, absoluteExpiration, cancellationToken);
                if (result)
                {
                    _logger.LogInformation("Entries with keys {keys} set in cache.", string.Join(", ", data.Keys));
                }
                else
                {
                    _logger.LogError("Failed to set entries with keys {keys} in cache.", string.Join(", ", data.Keys));
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while setting the cache.");
                throw ex.GetBaseException();
            }
        }

        /// <summary>
        /// Asynchronously removes multiple entries from the cache using specified keys.
        /// </summary>
        /// <param name="keys">The keys of the entries to remove.</param>
        /// <param name="cancellationToken">Optional cancellation token.</param>
        /// <returns>True if the operation is successful; otherwise, false.</returns>
        public async Task<bool> RemoveBatchFromCacheAsync(IEnumerable<string> keys, CancellationToken? cancellationToken = null)
        {
            try
            {
                foreach (var key in keys)
                {
                    ArgumentException.ThrowIfNullOrEmpty(key);
                }

                var result = await _cache.RemoveBatchAsync(keys, cancellationToken);
                if (result)
                {
                    _logger.LogInformation("Entries with keys {keys} removed from cache.", string.Join(", ", keys));
                }
                else
                {
                    _logger.LogError("Failed to remove entries with keys {keys} from cache.", string.Join(", ", keys));
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while removing from the cache.");
                throw ex.GetBaseException();
            }
        }
    }
}
