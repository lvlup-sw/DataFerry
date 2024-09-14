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
            _cache = new DistributedCache<T>(connection, new FastMemCache<string, string>(), settings, logger);
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
                cached = await _realProvider.GetFromSourceAsync(key).ConfigureAwait(false);

                // Set the entry in the cache (with refinements)
                if (cached is not null && flag != GetFlags.DoNotSetRecordInCache)
                {
                    if (await _cache.SetInCacheAsync(key, cached, TimeSpan.FromHours(_settings.AbsoluteExpiration)).ConfigureAwait(false))
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
                ArgumentException.ThrowIfNullOrWhiteSpace(key);

                bool cacheResult = await _cache.SetInCacheAsync(key, data, expiration ?? TimeSpan.FromHours(_settings.AbsoluteExpiration)).ConfigureAwait(false);
                if (cacheResult)
                {
                    _logger.LogDebug("Entry with key {key} set in cache.", key);
                }
                else
                {
                    _logger.LogError("Failed to set entry with key {key} in cache.", key);
                }

                bool providerResult = await _realProvider.SetInSourceAsync(data).ConfigureAwait(false);
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
                ArgumentException.ThrowIfNullOrWhiteSpace(key);

                bool cacheResult = await _cache.RemoveFromCacheAsync(key).ConfigureAwait(false);
                if (cacheResult)
                {
                    _logger.LogDebug("Entry with key {key} removed from cache.", key);
                }
                else
                {
                    _logger.LogError("Failed to remove entry with key {key} from cache.", key);
                }

                bool providerResult = await _realProvider.DeleteFromSourceAsync(key).ConfigureAwait(false);
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
                    var cachedKeys = cached.Keys.ToList();
                    var missingKeys = keys.Except(cachedKeys);

                    // Fetch missing keys from real provider
                    if (missingKeys.Any())
                    {
                        _logger.LogDebug("Cached entries with keys {keys} not found in cache. Getting entries from real provider.", string.Join(", ", missingKeys));

                        var missingData = await _realProvider.GetBatchFromSourceAsync(missingKeys, cancellationToken)
                            .ConfigureAwait(false);

                        // Update cache selectively
                        if (missingData is not null 
                            && missingData.Any() 
                            && GetFlags.DoNotSetRecordInCache != flags)
                        {
                            await _cache.SetBatchInCacheAsync(missingData, TimeSpan.FromHours(_settings.AbsoluteExpiration), cancellationToken)
                                .ConfigureAwait(false);

                            _logger.LogDebug("Entries with keys {keys} received from real provider and set in cache.", string.Join(", ", missingData.Keys));
                        }
                        else if (missingData is null || !missingData.Any())
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
                cached = await _realProvider.GetBatchFromSourceAsync(keys, cancellationToken)
                    .ConfigureAwait(false);

                if (!cached.Any())
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

                // Early return for flag
                if (flags == GetFlags.DoNotSetRecordInCache) return cached;

                var cacheSetResults = await _cache.SetBatchInCacheAsync(cached, absoluteExpiration, cancellationToken).ConfigureAwait(false);

                // Log successful sets
                cacheSetResults
                    .Where(kvp => kvp.Value) // Filter for successful operations
                    .ToList()
                    .ForEach(kvp => _logger.LogDebug("Entry with key {key} set in cache.", kvp.Key));

                // Log failed sets
                cacheSetResults
                    .Where(kvp => !kvp.Value) // Filter for failed operations
                    .ToList()
                    .ForEach(kvp => _logger.LogWarning("Failed to set entry with key {key} in cache.", kvp.Key));

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
        public async Task<IDictionary<string, bool>> SetDataBatchAsync(IDictionary<string, T> data, TimeSpan? expiration = default, CancellationToken? cancellationToken = null)
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

                // Log successful sets
                cacheSetResults
                    .Where(kvp => kvp.Value) // Filter for successful operations
                    .ToList()
                    .ForEach(kvp => _logger.LogDebug("Entry with key {key} set in cache.", kvp.Key));

                // Log failed sets
                cacheSetResults
                    .Where(kvp => !kvp.Value) // Filter for failed operations
                    .ToList()
                    .ForEach(kvp => _logger.LogWarning("Failed to set entry with key {key} in cache.", kvp.Key));

                // Set data in cache
                var providerSetResults = await _cache.SetBatchInCacheAsync(data, absoluteExpiration, cancellationToken).ConfigureAwait(false);

                // Log successful sets
                providerSetResults
                    .Where(kvp => kvp.Value) // Filter for successful operations
                    .ToList()
                    .ForEach(kvp => _logger.LogDebug("Entry with key {key} set in real provider.", kvp.Key));

                // Log failed sets
                providerSetResults
                    .Where(kvp => !kvp.Value) // Filter for failed operations
                    .ToList()
                    .ForEach(kvp => _logger.LogWarning("Failed to set entry with key {key} in cache.", kvp.Key));

                return cacheSetResults.Concat(providerSetResults)
                    .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
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
        public async Task<IDictionary<string, bool>> RemoveDataBatchAsync(IEnumerable<string> keys, CancellationToken? cancellationToken = null)
        {
            try
            {
                foreach (var key in keys)
                {
                    ArgumentException.ThrowIfNullOrEmpty(key);
                }

                // Set data in cache
                var cacheSetResults = await _cache.RemoveBatchFromCacheAsync(keys, cancellationToken).ConfigureAwait(false);

                // Log successful sets
                cacheSetResults
                    .Where(kvp => kvp.Value) // Filter for successful operations
                    .ToList()
                    .ForEach(kvp => _logger.LogDebug("Entry with key {key} removed from cache.", kvp.Key));

                // Log failed sets
                cacheSetResults
                    .Where(kvp => !kvp.Value) // Filter for failed operations
                    .ToList()
                    .ForEach(kvp => _logger.LogWarning("Failed to set entry with key {key} in cache.", kvp.Key));

                // Set data in cache
                var providerSetResults = await _cache.RemoveBatchFromCacheAsync(keys, cancellationToken).ConfigureAwait(false);

                // Log successful sets
                providerSetResults
                    .Where(kvp => kvp.Value) // Filter for successful operations
                    .ToList()
                    .ForEach(kvp => _logger.LogDebug("Entry with key {key} removed from real provider.", kvp.Key));

                // Log failed sets
                providerSetResults
                    .Where(kvp => !kvp.Value) // Filter for failed operations
                    .ToList()
                    .ForEach(kvp => _logger.LogWarning("Failed to set entry with key {key} in real provider.", kvp.Key));

                return cacheSetResults.Concat(providerSetResults)
                    .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while removing from the cache.");
                throw ex.GetBaseException();
            }
        }
    }
}
