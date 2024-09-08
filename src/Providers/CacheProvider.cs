using StackExchange.Redis;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DataFerry.Providers
{
    /// <summary>
    /// CacheProvider is a generic class that implements the <see cref="ICacheProvider{T}"/> interface.
    /// </summary>
    /// <remarks>
    /// This class makes use of two types of caches: <see cref="FastMemCache"/> and <see cref="DistributedCache"/>.
    /// It uses the <see cref="IRealProvider{T}"/> interface to retrieve records from the data source.
    /// </remarks>
    /// <typeparam name="T">The type of object to cache.</typeparam>
    public class CacheProvider<T> : ICacheProvider<T> where T : class
    {
        private readonly IRealProvider<T> _realProvider;
        private readonly CacheSettings _settings;
        private readonly ILogger _logger;
        private readonly DistributedCache<T> _cache;

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
            _cache = new DistributedCache<T>(connection, new FastMemCache<string, T>(), settings, logger);
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
                ArgumentNullException.ThrowIfNullOrWhiteSpace(key);

                // Try to get entry from the cache
                var cached = await _cache.GetFromCacheAsync(key);
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
                cached = await _realProvider.GetFromSourceAsync(key);

                // Set the entry in the cache (with refinements)
                if (cached is not null && flag != GetFlags.DoNotSetRecordInCache)
                {
                    if (await _cache.SetInCacheAsync(key, cached))
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
                ArgumentNullException.ThrowIfNullOrWhiteSpace(key);

                bool cacheResult = await _cache.SetInCacheAsync(key, data, expiration);
                if (cacheResult)
                {
                    _logger.LogDebug("Entry with key {key} set in cache.", key);
                }
                else
                {
                    _logger.LogError("Failed to set entry with key {key} in cache.", key);
                }

                bool providerResult = await _realProvider.SetInSourceAsync(data);
                if (providerResult)
                {
                    _logger.LogDebug("Entry with key {key} added to data source.", key);
                }
                else
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
                ArgumentNullException.ThrowIfNullOrWhiteSpace(key);

                bool cacheResult = await _cache.RemoveFromCacheAsync(key);
                if (cacheResult)
                {
                    _logger.LogDebug("Entry with key {key} removed from cache.", key);
                }
                else
                {
                    _logger.LogError("Failed to remove entry with key {key} from cache.", key);
                }

                bool providerResult = await _realProvider.DeleteFromSourceAsync(key);
                if (providerResult)
                {
                    _logger.LogDebug("Entry with key {key} removed from data source.", key);
                }
                else
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
        /// <returns>A <typeparamref name="Dictionary"/> of <typeparamref name="string"/> keys and <typeparamref name="T"/> data</returns>
        /// <exception cref="ArgumentNullException">Thrown when the <typeparamref name="keys"/> is null, an empty string, or contains only white-space characters.</exception>
        public async Task<IDictionary<string, T>> GetDataBatchAsync(IEnumerable<string> keys, GetFlags? flags = null, CancellationToken? cancellationToken = null)
        {
            try
            {
                // Null Checks
                foreach (var key in keys)
                {
                    ArgumentNullException.ThrowIfNullOrWhiteSpace(key);
                }

                Dictionary<string, T> cached = [];
                // Try to get entries from the cache
                cached = await _cache.GetBatchFromCacheAsync(keys, cancellationToken);

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

                        var missingData = await _realProvider.GetBatchFromSourceAsync(missingKeys, cancellationToken);

                        // Update cache selectively
                        if (missingData is not null && missingData.Any() && GetFlags.DoNotSetRecordInCache != flags)
                        {
                            await _cache.SetBatchInCacheAsync(missingData, TimeSpan.FromHours(_settings.AbsoluteExpiration), cancellationToken);
                            _logger.LogDebug("Entries with keys {keys} received from real provider and set in cache.", string.Join(", ", missingData.Keys));
                        }
                        else if (missingData is null || missingData.Count == 0)
                        {
                            _logger.LogWarning("Entries with keys {keys} not received from real provider.", string.Join(", ", missingKeys));
                        }

                        // Conditionally add missing data to return object
                        cached = (missingData is not null) switch
                        {
                            true => cached.Concat(missingData).ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
                            false => cached
                        };
                    }

                    _logger.LogDebug("Cached entries with keys {keys} found in cache.", string.Join(", ", cachedKeys));
                    return cached;
                }

                // Cache miss scenario
                // Get the entries from the real provider
                _logger.LogDebug("Cached entries with keys {keys} not found in cache. Getting entries from real provider.", string.Join(", ", keys));
                cached = await _realProvider.GetBatchFromSourceAsync(keys, cancellationToken);

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
                TimeSpan absoluteExpiration = TimeSpan.FromHours(_settings.AbsoluteExpiration);
                if (GetFlags.DoNotSetRecordInCache != flags && await _cache.SetBatchInCacheAsync(cached, absoluteExpiration, cancellationToken))
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
                _logger.LogError(ex, "An error occurred while getting the records.");
                throw ex.GetBaseException();
            }
        }

        /// <summary>
        /// Sets multiple records in the cache and data source with the specified keys.
        /// </summary>
        /// <param name="Dictionary{string, T}">A dictionary containing the keys and data to store in the cache and data source.</param>
        /// <param name="cancellationToken">Cancellation token to stop the operation.</param>
        /// <returns>True if all records were set successfully; otherwise, false.</returns>
        /// <exception cref="ArgumentNullException">Thrown when the <typeparamref name="string"/> is null, an empty string, or contains only white-space characters.</exception>
        /// <exception cref="ArgumentNullException">Thrown when the <typeparamref name="T"/> is null.</exception>
        public async Task<bool> SetDataBatchAsync(Dictionary<string, T> data, CancellationToken? cancellationToken = null)
        {
            try
            {
                ArgumentNullException.ThrowIfNull(data);
                foreach (var key in data.Keys)
                {
                    ArgumentNullException.ThrowIfNullOrEmpty(key);
                }

                TimeSpan absoluteExpiration = TimeSpan.FromHours(_settings.AbsoluteExpiration);

                // Set data in cache
                var cacheResult = await _cache.SetBatchInCacheAsync(data, absoluteExpiration, cancellationToken);

                if (cacheResult)
                {
                    _logger.LogDebug("Entries with keys {keys} set in cache.", string.Join(", ", data.Keys));
                }
                else
                {
                    _logger.LogError("Failed to set entries with keys {keys} in cache.", string.Join(", ", data.Keys));
                    return false;
                }

                // Set data in the data source
                var providerResult = await _realProvider.SetBatchInSourceAsync(data);

                if (providerResult)
                {
                    _logger.LogDebug("Entries with keys {keys} added to data source.", string.Join(", ", data.Keys));
                }
                else
                {
                    _logger.LogError("Failed to add entries with keys {keys} to data source.", string.Join(", ", data.Keys));
                }

                return cacheResult && providerResult;
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

                // Remove data from the cache
                var cacheResult = await _cache.RemoveBatchFromCacheAsync(keys, cancellationToken);
                if (cacheResult)
                {
                    _logger.LogDebug("Entries with keys {keys} removed from cache.", string.Join(", ", keys));
                }
                else
                {
                    _logger.LogError("Failed to remove entries with keys {keys} from cache.", string.Join(", ", keys));
                    return false;
                }

                // Remove data from the real provider
                var providerResult = await _realProvider.RemoveBatchFromSourceAsync(keys);

                if (providerResult)
                {
                    _logger.LogDebug("Entries with keys {keys} added to data source.", string.Join(", ", keys));
                }
                else
                {
                    _logger.LogError("Failed to add entries with keys {keys} to data source.", string.Join(", ", keys));
                }

                return cacheResult && providerResult;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while removing from the cache.");
                throw ex.GetBaseException();
            }
        }
    }
}
