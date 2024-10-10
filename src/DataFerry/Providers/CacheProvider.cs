using StackExchange.Redis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Buffers;

namespace DataFerry.Providers
{
    /// <summary>
    /// CacheProvider is a generic class that implements the <see cref="ICacheProvider{T}"/> interface.
    /// </summary>
    /// <remarks>
    /// This class makes use of two types of caches: <see cref="FastMemCache{TKey, TValue}"/> and <see cref="DistributedCache{T}"/>.
    /// It uses the <see cref="IRealProvider{T}"/> interface to retrieve records from the data source.
    /// </remarks>
    /// <typeparam name="T">The type of object to cache.</typeparam>
    public class CacheProvider<T> : ICacheProvider<T> where T : class
    {
        private readonly IRealProvider<T> _realProvider;
        private readonly CacheSettings _settings;
        private readonly ILogger _logger;
        private readonly DistributedCache<T> _cache;
        private readonly ArrayPool<byte>? _arrayPool;

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
            _cache = new DistributedCache<T>(
                connection, 
                new FastMemCache<string, string>(),
                settings, 
                logger
            );
        }

        /// <summary>
        /// Alternative constructor for the CacheProvider class which
        /// includes an <see cref="ArrayPool{T}"/> instance for deserialization operations.
        /// </summary>
        /// <remarks>
        /// Takes a real provider, array pool, cache type, and cache settings as parameters.
        /// </remarks>
        /// <param name="connection">The connection to the Redis server.</param>
        /// <param name="provider">The real provider to use as a data source in the case of cache misses.</param>
        /// <param name="arrayPool">The array pool singleton.</param>
        /// <param name="settings">The settings for the cache.</param>
        /// <param name="logger">The logger to use for logging.</param>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="InvalidOperationException"></exception>
        public CacheProvider(IConnectionMultiplexer connection, IRealProvider<T> provider, ArrayPool<byte> arrayPool, IOptions<CacheSettings> settings, ILogger logger)
        {
            // Null checks
            ArgumentNullException.ThrowIfNull(connection);
            ArgumentNullException.ThrowIfNull(provider);
            ArgumentNullException.ThrowIfNull(arrayPool);
            ArgumentNullException.ThrowIfNull(settings);
            ArgumentNullException.ThrowIfNull(logger);

            // Initializations
            _realProvider = provider;
            _arrayPool = arrayPool;
            _settings = settings.Value;
            _logger = logger;
            _cache = new DistributedCache<T>(
                connection,
                new FastMemCache<string, string>(),
                arrayPool,
                settings,
                logger
            );
        }

        /// <summary>
        /// Gets the cache instance.
        /// </summary>
        public DistributedCache<T> Cache => _cache;

        /// <summary>
        /// Gets the data source instance.
        /// </summary>
        public IRealProvider<T> RealProvider => _realProvider;

        /// <summary>
        /// Gets the record from the cache and data source with a specified key.
        /// </summary>
        /// <param name="key">The key of the record to retrieve.</param>
        /// <param name="flags">Flags to configure the behavior of the operation.</param>
        /// <returns>The data <typeparamref name="T"/></returns>
        /// <exception cref="ArgumentNullException">Thrown when the <typeparamref name="key"/> is null, an empty string, or contains only white-space characters.</exception>
        public async Task<T?> GetDataAsync(string key, GetFlags? flag = null)
        {
            try
            {
                // Null Checks
                ArgumentException.ThrowIfNullOrWhiteSpace(key);

                // Try to get entry from the cache
                var cached = await _cache.GetFromCacheAsync(key).ConfigureAwait(false);
                if (cached is not null)
                {
                    return cached;
                }
                else if (GetFlags.ReturnNullIfNotFoundInCache == flag)
                {
                    _logger.LogWarning("Cached entry with key {key} not found in cache.", key);
                    return null;
                }

                // If not found, get the entry from the real provider
                _logger.LogDebug("Cached entry with key {key} not found in cache. Getting entry from real provider.", key);
                cached = await _realProvider.GetFromSourceAsync(key).ConfigureAwait(false);

                // Set the entry in the cache
                if (cached is not null && flag != GetFlags.DoNotSetRecordInCache)
                {
                    bool success = await _cache.SetInCacheAsync(key, cached, TimeSpan.FromHours(_settings.AbsoluteExpiration)).ConfigureAwait(false);

                    if (!success)
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
                _logger.LogError(ex, "An error occurred while getting the record.");
                throw ex.GetBaseException();
            }
        }

        /// <summary>
        /// Adds a record to the cache and data source with a specified key.
        /// </summary>
        /// <param name="key">The key of the record to add.</param>
        /// <param name="data">The data to add to the cache.</param>
        /// <param name="expiration">The expiration time for the record.</param>
        /// <returns>True if the operation is successful; otherwise, false.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the <typeparamref name="key"/> is null, an empty string, or contains only white-space characters.</exception>
        /// <exception cref="ArgumentNullException">Thrown when the <typeparamref name="T"/> is null.</exception>
        public async Task<bool> SetDataAsync(string key, T data, TimeSpan? expiration = default)
        {
            try
            {
                ArgumentNullException.ThrowIfNull(data);
                ArgumentException.ThrowIfNullOrWhiteSpace(key);

                bool cacheResult = await _cache.SetInCacheAsync(key, data, expiration ?? TimeSpan.FromHours(_settings.AbsoluteExpiration)).ConfigureAwait(false);
                if (!cacheResult)
                {
                    _logger.LogError("Failed to set entry with key {key} in cache.", key);
                }

                bool providerResult = await _realProvider.SetInSourceAsync(data).ConfigureAwait(false);
                if (!providerResult)
                {
                    _logger.LogError("Failed to add entry with key {key} from data source.", key);
                }

                return cacheResult && providerResult;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while setting the record.");
                throw ex.GetBaseException();
            }
        }

        /// <summary>
        /// Removes a record from the cache and data source with a specified key.
        /// </summary>
        /// <returns>Returns true if the entry was removed from the cache and data source, false otherwise.</returns>
        /// <param name="key">The key of the record to remove.</param>
        /// <exception cref="ArgumentNullException">Thrown when the <typeparamref name="key"/> is null, an empty string, or contains only white-space characters.</exception>
        public async Task<bool> RemoveDataAsync(string key)
        {
            try
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(key);

                bool cacheResult = await _cache.RemoveFromCacheAsync(key).ConfigureAwait(false);
                if (!cacheResult)
                {
                    _logger.LogError("Failed to remove entry with key {key} from cache.", key);
                }

                bool providerResult = await _realProvider.DeleteFromSourceAsync(key).ConfigureAwait(false);
                if (!providerResult)
                {
                    _logger.LogError("Failed to remove entry with key {key} from data source.", key);
                }

                return cacheResult && providerResult;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while removing from the record.");
                throw ex.GetBaseException();
            }
        }

        /// <summary>
        /// Gets multiple records from the cache or data source with the specified keys.
        /// </summary>
        /// <param name="keys">The keys of the records to retrieve.</param>
        /// <param name="flags">Flags to configure the behavior of the operation.</param>
        /// <param name="cancellationToken">Cancellation token to stop the operation.</param>
        /// <returns>A <typeparamref name="IDictionary"/> of <typeparamref name="string"/> keys and <typeparamref name="T"/> data</returns>
        /// <exception cref="ArgumentNullException">Thrown when the <typeparamref name="keys"/> is null, an empty string, or contains only white-space characters.</exception>
        public async Task<IDictionary<string, T>> GetDataBatchAsync(IEnumerable<string> keys, GetFlags? flags = null, CancellationToken? cancellationToken = null)
        {
            try
            {
                // Null Checks
                foreach (var key in keys)
                {
                    ArgumentException.ThrowIfNullOrWhiteSpace(key);
                }

                // Try to get entries from the cache
                IDictionary<string, T> cached = await _cache.GetBatchFromCacheAsync(keys, cancellationToken).ConfigureAwait(false);

                // Cache hit scenario
                if (cached.Any())
                {
                    // Extract missing keys
                    HashSet<string> missingKeys = new(keys.Except(cached.Keys));

                    // Return if there are no missing keys
                    if (!missingKeys.Any()) return cached;

                    // Fetch missing keys from real provider
                    var missingData = await _realProvider.GetBatchFromSourceAsync(missingKeys, cancellationToken)
                        .ConfigureAwait(false);

                    // Update cache and merge results efficiently
                    if (missingData?.Any() == true && GetFlags.DoNotSetRecordInCache != flags)
                    {
                        await _cache.SetBatchInCacheAsync(missingData, TimeSpan.FromHours(_settings.AbsoluteExpiration), cancellationToken).ConfigureAwait(false);

                        // Add results to Dict
                        foreach (var kvp in missingData)
                        {
                            cached[kvp.Key] = kvp.Value;
                        }
                    }
                    else if (missingData == null || !missingData.Any())
                    {
                        _logger.LogWarning("Entries with keys {keys} not received from real provider.", string.Join(", ", missingKeys));
                    }

                    return cached;
                }

                // Cache miss scenario - Get the entries from the real provider
                _logger.LogDebug("Cached entries with keys {keys} not found in cache. Getting entries from real provider.", string.Join(", ", keys));
                cached = await _realProvider.GetBatchFromSourceAsync(keys, cancellationToken).ConfigureAwait(false);

                // Check if any data was received and log warnings if necessary
                if (!cached.Any())
                {
                    _logger.LogWarning("No entries received from the real provider for any of the keys: {keys}.", string.Join(", ", keys));
                    // Return since there's nothing else we can do
                    return cached;
                }
                else if (cached.Count < keys.Count())
                {
                    _logger.LogWarning("Entries with keys {keys} partially received from real provider.", string.Join(", ", keys));
                }

                TimeSpan absoluteExpiration = TimeSpan.FromHours(_settings.AbsoluteExpiration);
                // Early return for flag
                if (flags == GetFlags.DoNotSetRecordInCache) return cached;

                // Set what we have into the cache
                var cacheSetResults = await _cache.SetBatchInCacheAsync(cached, absoluteExpiration, cancellationToken).ConfigureAwait(false);

                // Return if no failed results
                if (cacheSetResults.Count == cached.Count) return cached;

                // Log failed sets
                foreach (var kvp in cacheSetResults)
                {
                    if (!kvp.Value)
                    {
                        _logger.LogWarning("Failed to set entry with key {key} in cache.", kvp.Key);
                    }
                }

                return cached;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while getting the records.");
                throw ex.GetBaseException();
            }
        }

        /// <summary>
        /// Sets multiple records in the cache and data source with the specified keys.
        /// </summary>
        /// <param name="IDictionary{string, T}">A dictionary containing the keys and data to store in the cache and data source.</param>
        /// <param name="cancellationToken">Cancellation token to stop the operation.</param>
        /// <returns>True if all records were set successfully; otherwise, false.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the <typeparamref name="string"/> is null, an empty string, or contains only white-space characters.</exception>
        /// <exception cref="ArgumentNullException">Thrown when the <typeparamref name="T"/> is null.</exception>
        public async Task<bool> SetDataBatchAsync(IDictionary<string, T> data, TimeSpan? expiration = default, CancellationToken? cancellationToken = null)
        {
            try
            {
                ArgumentNullException.ThrowIfNull(data);
                foreach (var key in data.Keys)
                {
                    ArgumentException.ThrowIfNullOrEmpty(key);
                }

                TimeSpan absoluteExpiration = expiration ?? TimeSpan.FromHours(_settings.AbsoluteExpiration);

                // Set data in cache
                var cacheSetResults = await _cache.SetBatchInCacheAsync(data, absoluteExpiration, cancellationToken).ConfigureAwait(false);

                // Log failed sets
                if (cacheSetResults.Count != data.Count)
                {
                    foreach (var kvp in cacheSetResults)
                    {
                        if (!kvp.Value)
                        {
                            _logger.LogWarning("Failed to set entry with key {key} in cache.", kvp.Key);
                        }
                    }
                }

                // Set data in cache
                var providerSetResults = await _realProvider.SetBatchInSourceAsync(data, cancellationToken).ConfigureAwait(false);

                // Log failed sets
                if (providerSetResults.Count != data.Count)
                {
                    foreach (var kvp in providerSetResults)
                    {
                        if (!kvp.Value)
                        {
                            _logger.LogWarning("Failed to set entry with key {key} in real provider.", kvp.Key);
                        }
                    }
                }

                // Return single result
                return cacheSetResults.All(kvp => kvp.Value) 
                    && providerSetResults.All(kvp => kvp.Value);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while setting the cache.");
                throw ex.GetBaseException();
            }
        }

        /// <summary>
        /// Removes multiple records from the cache and data source with the specified keys.
        /// </summary>
        /// <param name="keys">The keys of the records to remove.</param>
        /// <param name="cancellationToken">Cancellation token to stop the operation.</param>
        /// <returns>True if all records were removed successfully; otherwise, false.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the <typeparamref name="keys"/> is null, an empty string, or contains only white-space characters.</exception> 
        public async Task<bool> RemoveDataBatchAsync(IEnumerable<string> keys, CancellationToken? cancellationToken = null)
        {
            try
            {
                foreach (var key in keys)
                {
                    ArgumentException.ThrowIfNullOrEmpty(key);
                }

                // Set data in cache
                var cacheSetResults = await _cache.RemoveBatchFromCacheAsync(keys, cancellationToken).ConfigureAwait(false);

                // Log failed sets
                if (cacheSetResults.Count != keys.Count())
                {
                    foreach (var kvp in cacheSetResults)
                    {
                        if (!kvp.Value)
                        {
                            _logger.LogWarning("Failed to set entry with key {key} in cache.", kvp.Key);
                        }
                    }
                }

                // Set data in cache
                var providerSetResults = await _cache.RemoveBatchFromCacheAsync(keys, cancellationToken).ConfigureAwait(false);

                // Log failed sets
                if (providerSetResults.Count != keys.Count())
                {
                    foreach (var kvp in providerSetResults)
                    {
                        if (!kvp.Value)
                        {
                            _logger.LogWarning("Failed to set entry with key {key} in real provider.", kvp.Key);
                        }
                    }
                }

                // Return single result
                return cacheSetResults.All(kvp => kvp.Value)
                    && providerSetResults.All(kvp => kvp.Value);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while removing from the cache.");
                throw ex.GetBaseException();
            }
        }
    }
}
