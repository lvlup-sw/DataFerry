using System.Buffers;
using System.Runtime.CompilerServices;
using lvlup.DataFerry.Caching.Clients.Contracts;
using lvlup.DataFerry.Memory;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Wrap;
using StackExchange.Redis;

namespace lvlup.DataFerry.Caching.Clients;

/// <summary>
/// Concrete implementation of IRedisClient using StackExchange.Redis and Hash storage strategy.
/// Includes resilience policies via Polly and low-allocation operations.
/// </summary>
public sealed class RedisClient : IRedisClient
{
    // Constants from RedisCache implementation
    private const string AbsoluteExpirationKey = "absexp";
    private const string SlidingExpirationKey = "sldexp";
    private const string DataKey = "data";
    private static readonly RedisValue[] _hashFieldsDataOnly = [DataKey];
    private static readonly RedisValue[] _hashFieldsMetadataOnly = [AbsoluteExpirationKey, SlidingExpirationKey];
    private static readonly RedisValue[] _hashFieldsAll = [AbsoluteExpirationKey, SlidingExpirationKey, DataKey];
    private const long NotPresent = -1;

    // Connection and State
    private volatile IConnectionMultiplexer? _connectionMux; // Made nullable for lazy init
    private volatile IDatabase? _database; // Made nullable for lazy init
    private readonly Func<Task<IConnectionMultiplexer>>? _connectionMultiplexerFactory; // Optional factory
    private readonly string? _redisConfiguration; // Optional configuration string
    private readonly RedisKey _instancePrefix;
    private readonly ILogger<RedisClient> _logger;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private bool _disposed;

    // Resilience Policies (assuming setup elsewhere or via options)
    private readonly ReaderWriterLockSlim _policyLock;
    private PolicyWrap _syncPolicy; // Keep sync policy if needed for non-interface methods? Or remove? Let's remove for now.
    private AsyncPolicyWrap _asyncPolicy;

    // Reconnect Logic Fields (adapted from RedisCache)
    private long _lastConnectTicks = 0;
    private long _firstErrorTimeTicks;
    private long _previousErrorTimeTicks;
    private readonly TimeSpan ReconnectMinInterval = TimeSpan.FromSeconds(60);
    private readonly TimeSpan ReconnectErrorThreshold = TimeSpan.FromSeconds(30);
    private readonly bool _useForceReconnect = false; // TODO: Make configurable via options

    // Constructor using configuration string
    public RedisClient(string redisConfiguration, string? instanceName, ILogger<RedisClient> logger /*, Polly policies/options */)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(redisConfiguration);
        Initialize(null, redisConfiguration, instanceName, logger);
    }

    // Constructor using factory
     public RedisClient(Func<Task<IConnectionMultiplexer>> connectionMultiplexerFactory, string? instanceName, ILogger<RedisClient> logger /*, Polly policies/options */)
    {
        ArgumentNullException.ThrowIfNull(connectionMultiplexerFactory);
        Initialize(connectionMultiplexerFactory, null, instanceName, logger);
    }

    [MemberNotNull(nameof(_logger), nameof(_policyLock), nameof(_asyncPolicy), nameof(_instancePrefix))]
    private void Initialize(Func<Task<IConnectionMultiplexer>>? factory, string? config, string? instanceName, ILogger<RedisClient> logger)
    {
        _connectionMultiplexerFactory = factory;
        _redisConfiguration = config;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _policyLock = new();

        // TODO: Setup Polly policies properly based on injected config/options
        _asyncPolicy = Policy.WrapAsync(Policy.NoOpAsync()); // Placeholder

        if (!string.IsNullOrEmpty(instanceName))
        {
            _instancePrefix = (RedisKey)Encoding.UTF8.GetBytes(instanceName);
        }
        else
        {
            _instancePrefix = RedisKey.Empty; // Ensure it's not null
        }
    }

    // --- IRedisClient Implementation ---

    public async ValueTask<bool> TryGetAsync(string key, IBufferWriter<byte> writer, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(writer);
        CheckDisposed();
        token.ThrowIfCancellationRequested();

        Lease<byte>? dataLease = null;
        try
        {
            var database = await ConnectAsync(token).ConfigureAwait(false);
            RedisValue[] metadata = RedisValue.EmptyArray; // Default empty
            bool refreshNeeded = false;
            TimeSpan? slidingExpr = null;
            DateTimeOffset? absExpr = null;

            // Execute getting metadata and data using Polly
            (metadata, dataLease) = await _asyncPolicy.ExecuteAsync(async (ctx, ct) =>
            {
                _logger.LogDebug("Attempting Redis HGETALL/HGET for key {key}.", key);
                var prefixedKey = _instancePrefix.Append(key);
                // Pipeline metadata and data retrieval
                var metadataTask = database.HashGetAsync(prefixedKey, _hashFieldsMetadataOnly);
                var dataTask = database.HashGetLeaseAsync(prefixedKey, DataKey); // Use Lease for low-alloc data

                await Task.WhenAll(metadataTask, dataTask).ConfigureAwait(false);

                return (metadataTask.Result, dataTask.Result);

            }, new Context($"RedisClient.TryGetAsync for {key}"), token).ConfigureAwait(false);


            if (dataLease is not null && metadata.Length >= 2) // Found data and got metadata
            {
                MapMetadata(metadata, out absExpr, out slidingExpr);
                if (slidingExpr.HasValue)
                {
                     // Schedule refresh after returning data
                     refreshNeeded = true;
                }

                // Write data to the output buffer
                writer.Write(dataLease.Span);
                _logger.LogDebug("Redis GET succeeded for key {key}, wrote {length} bytes.", key, dataLease.Length);
                return true; // Found and written
            }
            else
            {
                 _logger.LogDebug("Redis GET failed or returned null/empty for key {key}.", key);
                return false; // Not found
            }

            // Perform refresh outside the main data retrieval logic if needed
            // Note: Refresh logic from RedisCache is complex; simple KeyExpire shown here
            if (refreshNeeded && slidingExpr.HasValue)
            {
                // Fire-and-forget refresh might be acceptable
                _ = RefreshInternalAsync(database, key, absExpr, slidingExpr.Value, CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
             OnRedisError(ex, _database); // Use potentially null _database safely
             _logger.LogError(ex, "Redis error during TryGetAsync for key {key}.", key);
             // Depending on policy, Polly might handle retries. Decide whether to throw.
             // For TryGet, usually better to return false on error.
             return false;
        }
        finally
        {
            dataLease?.Dispose(); // IMPORTANT: Return leased buffer to pool
        }
        // Should not be reachable if TryGet found data or errored appropriately
        return false;
    }


    public async ValueTask<byte[]?> GetBytesAsync(string key, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        CheckDisposed();
        token.ThrowIfCancellationRequested();

        RedisValue[] results = RedisValue.EmptyArray; // hash fields: absexp, sldexp, data
        try
        {
            var database = await ConnectAsync(token).ConfigureAwait(false);
            bool refreshNeeded = false;
            TimeSpan? slidingExpr = null;
            DateTimeOffset? absExpr = null;

             results = await _asyncPolicy.ExecuteAsync(async (ctx, ct) =>
             {
                  _logger.LogDebug("Attempting Redis HGETALL for key {key}.", key);
                 return await database.HashGetAsync(_instancePrefix.Append(key), _hashFieldsAll).ConfigureAwait(false);

             }, new Context($"RedisClient.GetBytesAsync for {key}"), token).ConfigureAwait(false);


            if (results.Length >= 2) // Check if metadata was retrieved
            {
                MapMetadata(results, out absExpr, out slidingExpr);
                if (slidingExpr.HasValue)
                {
                    refreshNeeded = true; // Schedule refresh
                }
            }

             // Perform refresh outside the main data retrieval logic if needed
             if (refreshNeeded && slidingExpr.HasValue)
             {
                  _ = RefreshInternalAsync(database, key, absExpr, slidingExpr.Value, CancellationToken.None);
             }


            if (results.Length >= 3 && results[2].HasValue && !results[2].IsNullOrEmpty)
            {
                 _logger.LogDebug("Redis GET succeeded for key {key}.", key);
                return (byte[])results[2]!; // Data is the 3rd element
            }
            else
            {
                 _logger.LogDebug("Redis GET failed or returned null/empty data for key {key}.", key);
                return null;
            }
        }
        catch (Exception ex)
        {
             OnRedisError(ex, _database);
             _logger.LogError(ex, "Redis error during GetBytesAsync for key {key}.", key);
            // Return null on error for Get operations
             return null;
        }
    }


    public async Task SetAsync(string key, ReadOnlySequence<byte> data, DistributedCacheEntryOptions options, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(options);
        CheckDisposed();
        token.ThrowIfCancellationRequested();

        byte[]? leasedArray = null;
        try
        {
            var database = await ConnectAsync(token).ConfigureAwait(false);
            var creationTime = DateTimeOffset.UtcNow;
            var absoluteExpiration = GetAbsoluteExpiration(creationTime, options);

            // Linearize sequence if needed, using pooled array
            var redisDataValue = LinearizeToRedisValue(data, out leasedArray);

            var fields = GetHashFields(redisDataValue, absoluteExpiration, options.SlidingExpiration);
            var expiry = GetRedisExpiry(creationTime, absoluteExpiration, options); // Get TimeSpan for KeyExpire

            await _asyncPolicy.ExecuteAsync(async (ctx, ct) =>
            {
                 _logger.LogDebug("Attempting Redis HSET/EXPIRE for key {key} with expiry {expiry}.", key, expiry);
                var prefixedKey = _instancePrefix.Append(key);

                // Pipeline HashSet and KeyExpire
                var hashTask = database.HashSetAsync(prefixedKey, fields);
                var expireTask = expiry.HasValue
                                    ? database.KeyExpireAsync(prefixedKey, expiry.Value)
                                    : Task.FromResult(false); // No-op task if no expiry

                await Task.WhenAll(hashTask, expireTask).ConfigureAwait(false);
                // Check task results/exceptions if needed, though Polly might handle some exceptions
                return Task.CompletedTask; // Return type for Polly ExecuteAsync<TResult> if needed

            }, new Context($"RedisClient.SetAsync for {key}"), token).ConfigureAwait(false);
             _logger.LogDebug("Redis HSET/EXPIRE completed for key {key}.", key);

        }
        catch (Exception ex)
        {
             OnRedisError(ex, _database);
             _logger.LogError(ex, "Redis error during SetAsync for key {key}.", key);
             // Decide whether to throw or swallow error on Set
             throw;
        }
        finally
        {
             RecycleLeasedArray(leasedArray); // Return pooled array if rented
        }
    }


    public Task SetBytesAsync(string key, byte[] data, DistributedCacheEntryOptions options, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(data); // Check byte array null explicitly
        // Delegate to the ReadOnlySequence overload
        return SetAsync(key, new ReadOnlySequence<byte>(data), options, token);
    }

    public async Task RemoveAsync(string key, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        CheckDisposed();
        token.ThrowIfCancellationRequested();

        try
        {
            var database = await ConnectAsync(token).ConfigureAwait(false);

            await _asyncPolicy.ExecuteAsync(async (ctx, ct) =>
            {
                _logger.LogDebug("Attempting Redis DEL for key {key}.", key);
                return await database.KeyDeleteAsync(_instancePrefix.Append(key)).ConfigureAwait(false);

            }, new Context($"RedisClient.RemoveAsync for {key}"), token).ConfigureAwait(false);
             _logger.LogDebug("Redis DEL completed for key {key}.", key);
        }
        catch (Exception ex)
        {
             OnRedisError(ex, _database);
             _logger.LogError(ex, "Redis error during RemoveAsync for key {key}.", key);
             // Decide whether to throw or swallow
             throw;
        }
    }

    public async Task RefreshAsync(string key, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        CheckDisposed();
        token.ThrowIfCancellationRequested();

        RedisValue[] metadata;
        try
        {
             var database = await ConnectAsync(token).ConfigureAwait(false);

             // Get metadata first using Polly
             metadata = await _asyncPolicy.ExecuteAsync(async (ctx, ct) =>
             {
                 _logger.LogDebug("Attempting Redis HGET (metadata) for refresh on key {key}.", key);
                 return await database.HashGetAsync(_instancePrefix.Append(key), _hashFieldsMetadataOnly).ConfigureAwait(false);

             }, new Context($"RedisClient.RefreshAsync_GetMeta for {key}"), token).ConfigureAwait(false);


             if (metadata.Length >= 2)
             {
                 MapMetadata(metadata, out DateTimeOffset? absExpr, out TimeSpan? sldExpr);
                 if (sldExpr.HasValue)
                 {
                      // Perform the actual refresh using Polly
                     await RefreshInternalAsync(database, key, absExpr, sldExpr.Value, token).ConfigureAwait(false);
                      _logger.LogDebug("Redis EXPIRE refresh completed for key {key}.", key);
                 } else {
                      _logger.LogDebug("No sliding expiration found, refresh skipped for key {key}.", key);
                 }
             } else {
                 _logger.LogDebug("Key not found or no metadata, refresh skipped for key {key}.", key);
             }
        }
        catch (Exception ex)
        {
             OnRedisError(ex, _database);
             _logger.LogError(ex, "Redis error during RefreshAsync for key {key}.", key);
             // Decide whether to throw or swallow
             throw;
        }
    }

    // --- Internal Refresh Logic (Called by Get/Refresh) ---
    private Task RefreshInternalAsync(IDatabase database, string key, DateTimeOffset? absExpr, TimeSpan sldExpr, CancellationToken token)
    {
         TimeSpan? expiry = CalculateRefreshExpiry(absExpr, sldExpr);
         if (!expiry.HasValue || expiry.Value <= TimeSpan.Zero)
         {
              _logger.LogDebug("Refresh skipped for key {key} due to invalid calculated expiry.", key);
             return Task.CompletedTask; // No refresh needed or possible
         }

         // Wrap the expire call in Polly policy
         return _asyncPolicy.ExecuteAsync(async (ctx, ct) =>
         {
              _logger.LogDebug("Attempting Redis EXPIRE for key {key} with new ttl {expiry}.", key, expiry.Value);
              // Using FireAndForget might be suitable for refresh if exact success isn't critical
              return await database.KeyExpireAsync(_instancePrefix.Append(key), expiry.Value, CommandFlags.FireAndForget).ConfigureAwait(false);
         }, new Context($"RedisClient.RefreshInternalAsync for {key}"), token);
    }

    private static TimeSpan? CalculateRefreshExpiry(DateTimeOffset? absExpr, TimeSpan sldExpr)
    {
         TimeSpan? expiry;
         if (absExpr.HasValue)
         {
             var remainingAbsolute = absExpr.Value - DateTimeOffset.UtcNow;
             expiry = remainingAbsolute <= sldExpr ? remainingAbsolute : sldExpr;
         }
         else
         {
             expiry = sldExpr;
         }
         return expiry;
    }


    // --- Batch Operations (Kept from CacheOrchestrator, Internal for now) ---
    // NOTE: These need significant adaptation to use the Hash storage strategy
    // The provided stubs below are NOT fully updated for hash usage yet.
    #region Internal Batch Operations (Not part of IRedisClient currently)

    internal async IAsyncEnumerable<(string Key, int Index, int Length)> GetBatchFromCacheAsync(
        IEnumerable<string> keys, RentedBufferWriter<byte> destination, [EnumeratorCancellation] CancellationToken token = default)
    {
        // TODO: Adapt this to use HashGetAsync/HashGetLeaseAsync for each key,
        // potentially using Redis batching (IBatch) for pipelining the HashGets.
        // Also needs to handle metadata and sliding expiration refresh per key.
        ArgumentNullException.ThrowIfNull(keys); ArgumentNullException.ThrowIfNull(destination); CheckDisposed();
        _logger.LogWarning("GetBatchFromCacheAsync is not fully adapted for Hash storage strategy.");
        await Task.Yield(); // Prevent warning
        yield break; // Placeholder
        // Original logic needs complete rewrite for hash strategy
    }

     internal async IAsyncEnumerable<KeyValuePair<string, bool>> SetBatchInCacheAsync(
        IDictionary<string, byte[]> data, DistributedCacheEntryOptions options, [EnumeratorCancellation] CancellationToken token = default)
    {
        // TODO: Adapt this to use IBatch and pipeline HashSetAsync/KeyExpireAsync calls for each key/value pair,
        // constructing the HashEntry[] for each based on data and options.
        ArgumentNullException.ThrowIfNull(data); ArgumentNullException.ThrowIfNull(options); CheckDisposed();
         _logger.LogWarning("SetBatchInCacheAsync is not fully adapted for Hash storage strategy.");
        await Task.Yield();
        foreach(var k in data.Keys) yield return new KeyValuePair<string, bool>(k, false); // Placeholder
        // Original logic needs complete rewrite for hash strategy
    }

     internal async IAsyncEnumerable<KeyValuePair<string, bool>> RefreshBatchFromCacheAsync(
        IEnumerable<string> keys, TimeSpan ttl, [EnumeratorCancellation] CancellationToken token = default)
    {
        // TODO: Adapt this. Refreshing batches is complex with sliding expiration via Hashes.
        // Would likely need to MGET hash metadata, calculate new TTLs individually, then batch KeyExpire calls.
        ArgumentNullException.ThrowIfNull(keys); CheckDisposed();
         _logger.LogWarning("RefreshBatchFromCacheAsync is not fully adapted for Hash storage strategy.");
        await Task.Yield();
        foreach(var k in keys) yield return new KeyValuePair<string, bool>(k, false); // Placeholder
        // Original logic needs complete rewrite for hash strategy
    }

     internal async IAsyncEnumerable<KeyValuePair<string, bool>> RemoveBatchFromCacheAsync(
        IEnumerable<string> keys, [EnumeratorCancellation] CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(keys); CheckDisposed();
        try {
            var database = await ConnectAsync(token).ConfigureAwait(false);
            RedisKey[] redisKeys = keys.Select(k => _instancePrefix.Append(k)).ToArray();
            if (redisKeys.Length == 0) yield break;

            // Execute using Polly
            long deletedCount = await _asyncPolicy.ExecuteAsync(async (ctx, ct) =>
            {
                 _logger.LogDebug("Attempting batched Redis DEL for {count} keys.", redisKeys.Length);
                return await database.KeyDeleteAsync(redisKeys).ConfigureAwait(false); // KeyDelete supports multiple keys
            }, new Context("RedisClient.RemoveBatchAsync"), token).ConfigureAwait(false);
             _logger.LogDebug("Batched Redis DEL completed, removed {count} keys.", deletedCount);

            // KeyDeleteAsync with multiple keys returns total count deleted.
            // We can't easily know *which* succeeded/failed without checking existence before/after.
            // For simplicity, yield success for all keys if count > 0? Or assume all succeeded?
            // Yielding based on input count vs deleted count isn't reliable if some keys didn't exist.
            // Let's just yield true for all requested keys if *any* were deleted for simplicity,
            // acknowledging this isn't perfect reporting per key.
            bool anyDeleted = deletedCount > 0;
            foreach(var key in keys)
            {
                yield return new KeyValuePair<string, bool>(key, anyDeleted); // Simplified result
            }
        }
        catch(Exception ex)
        {
             OnRedisError(ex, _database);
             _logger.LogError(ex, "Redis error during RemoveBatchFromCacheAsync.");
             // Decide whether to throw or yield failures
             foreach(var key in keys) yield return new KeyValuePair<string, bool>(key, false);
        }
    }

    #endregion // Internal Batch Operations

    // --- Connection Management (Adapted from RedisCache) ---
    #region Connection Management

    [MemberNotNull(nameof(_database))]
    private async ValueTask<IDatabase> ConnectAsync(CancellationToken token = default, bool forceReconnect = false)
    {
        CheckDisposed();
        token.ThrowIfCancellationRequested();

        var database = _database; // Read volatile field
        if (database is not null && !forceReconnect)
        {
            return database;
        }
        return await ConnectSlowAsync(token).ConfigureAwait(false);
    }

    private async ValueTask<IDatabase> ConnectSlowAsync(CancellationToken token)
    {
        await _connectionLock.WaitAsync(token).ConfigureAwait(false);
        try
        {
            // Double check after acquiring lock
            if (_database is null)
            {
                _logger.LogInformation("Connecting to Redis...");
                IConnectionMultiplexer connection;
                if (_connectionMultiplexerFactory is not null)
                {
                    connection = await _connectionMultiplexerFactory().ConfigureAwait(false);
                }
                else if (!string.IsNullOrWhiteSpace(_redisConfiguration))
                {
                    connection = await ConnectionMultiplexer.ConnectAsync(_redisConfiguration).ConfigureAwait(false);
                }
                else
                {
                    throw new InvalidOperationException("Redis connection configuration or factory must be provided.");
                }

                PrepareConnection(connection); // Register hooks, etc.
                _database = connection.GetDatabase();
                _connectionMux = connection; // Store connection only after successful DB get
                 _logger.LogInformation("Successfully connected to Redis.");
            }
            // Ensure _database is not null here - throw if connection failed? PrepareConnection should handle errors?
             if (_database is null) throw new InvalidOperationException("Failed to get Redis database instance after connection.");
             return _database;
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    private void PrepareConnection(IConnectionMultiplexer connection)
    {
        WriteTimeTicks(ref _lastConnectTicks, DateTimeOffset.UtcNow);
        // Reset error tracking on successful connection
        WriteTimeTicks(ref _firstErrorTimeTicks, DateTimeOffset.MinValue);
        WriteTimeTicks(ref _previousErrorTimeTicks, DateTimeOffset.MinValue);

        // Add library suffix (optional but good practice)
        try
        {
             connection.AddLibraryNameSuffix("DataferryCache");
             // Add other relevant suffixes if needed
        }
        catch (Exception ex)
        {
             _logger.LogWarning(ex, "Unable to AddLibraryNameSuffix to Redis connection.");
        }

        // TODO: Register ProfilingSession if needed, similar to RedisCache
        // if (_options.ProfilingSession is not null) connection.RegisterProfiler(_options.ProfilingSession);
    }

    private void OnRedisError(Exception exception, IDatabase? database)
    {
         if (exception is ObjectDisposedException) return; // Ignore errors on disposed object

         _logger.LogError(exception, "Redis error occurred.");

         // Optional: Forced reconnect logic from RedisCache
         if (_useForceReconnect && (exception is RedisConnectionException or SocketException or ObjectDisposedException)) // Also check ObjectDisposedException here?
         {
             var utcNow = DateTimeOffset.UtcNow;
             var previousConnectTime = ReadTimeTicks(ref _lastConnectTicks);
             TimeSpan elapsedSinceLastReconnect = utcNow - previousConnectTime;

             if (elapsedSinceLastReconnect < ReconnectMinInterval) return; // Too soon

             var firstErrorTime = ReadTimeTicks(ref _firstErrorTimeTicks);
             if (firstErrorTime == DateTimeOffset.MinValue)
             {
                 WriteTimeTicks(ref _firstErrorTimeTicks, utcNow);
                 WriteTimeTicks(ref _previousErrorTimeTicks, utcNow);
                 return;
             }

             TimeSpan elapsedSinceFirstError = utcNow - firstErrorTime;
             TimeSpan elapsedSinceMostRecentError = utcNow - ReadTimeTicks(ref _previousErrorTimeTicks);

             bool shouldReconnect = elapsedSinceFirstError >= ReconnectErrorThreshold &&
                                    elapsedSinceMostRecentError <= ReconnectErrorThreshold;

             WriteTimeTicks(ref _previousErrorTimeTicks, utcNow);

             if (!shouldReconnect) return;

             WriteTimeTicks(ref _firstErrorTimeTicks, DateTimeOffset.MinValue);
             WriteTimeTicks(ref _previousErrorTimeTicks, DateTimeOffset.MinValue);

             _logger.LogWarning("Attempting forced Redis reconnection due to sustained errors.");

             // Only CompareExchange the database field, connection will be handled by ConnectAsync
             // If we only null _database, ConnectAsync will try to use existing _connectionMux? Needs careful thought.
             // Let's null both to force full reconnect via ConnectSlowAsync.
             var currentMux = Interlocked.Exchange(ref _connectionMux, null);
             var currentDb = Interlocked.Exchange(ref _database, null); // Null database first

             ReleaseConnection(currentMux); // Dispose old connection
         }
    }

    private static void ReleaseConnection(IConnectionMultiplexer? connection)
    {
         if (connection is not null)
         {
             try { connection.Close(allowCommandsToComplete: false); } catch { /* ignore */ } // Best effort close
             try { connection.Dispose(); } catch { /* ignore */ }
         }
    }

    private static DateTimeOffset ReadTimeTicks(ref long field) => Volatile.Read(ref field) == 0 ? DateTimeOffset.MinValue : new DateTimeOffset(Volatile.Read(ref field), TimeSpan.Zero);
    private static void WriteTimeTicks(ref long field, DateTimeOffset value) => Volatile.Write(ref field, value == DateTimeOffset.MinValue ? 0L : value.UtcTicks);

    #endregion // Connection Management

    // --- Helpers (Adapted from RedisCache) ---
    #region Helpers

    private static void MapMetadata(RedisValue[] results, out DateTimeOffset? absoluteExpiration, out TimeSpan? slidingExpiration)
    {
        absoluteExpiration = null;
        slidingExpiration = null;
        if (results.Length >= 1) // Check index bounds
        {
             var absoluteExpirationTicks = (long?)results[0];
             if (absoluteExpirationTicks.HasValue && absoluteExpirationTicks.Value != NotPresent)
             {
                 absoluteExpiration = new DateTimeOffset(absoluteExpirationTicks.Value, TimeSpan.Zero);
             }
        }
        if (results.Length >= 2) // Check index bounds
        {
            var slidingExpirationTicks = (long?)results[1];
            if (slidingExpirationTicks.HasValue && slidingExpirationTicks.Value != NotPresent)
            {
                slidingExpiration = new TimeSpan(slidingExpirationTicks.Value);
            }
        }
    }

    // Helper to create the HashEntry array for storing data + metadata
    private static HashEntry[] GetHashFields(RedisValue dataValue, DateTimeOffset? absoluteExpiration, TimeSpan? slidingExpiration) =>
        [
            new HashEntry(AbsoluteExpirationKey, absoluteExpiration?.Ticks ?? NotPresent),
            new HashEntry(SlidingExpirationKey, slidingExpiration?.Ticks ?? NotPresent),
            new HashEntry(DataKey, dataValue)
        ];

    // Helper to calculate expiry for Redis KeyExpire command (prefers TimeSpan for PEXPIRE/EXPIRE)
    private static TimeSpan? GetRedisExpiry(DateTimeOffset creationTime, DateTimeOffset? absoluteExpiration, DistributedCacheEntryOptions options)
    {
        if (absoluteExpiration.HasValue && options.SlidingExpiration.HasValue)
        {
            var remainingAbsolute = absoluteExpiration.Value - creationTime;
            return remainingAbsolute <= options.SlidingExpiration.Value ? remainingAbsolute : options.SlidingExpiration.Value;
        }
        else if (absoluteExpiration.HasValue)
        {
            return absoluteExpiration.Value - creationTime;
        }
        else if (options.SlidingExpiration.HasValue)
        {
            return options.SlidingExpiration.Value;
        }
        return null; // No expiry
    }

    // Helper to calculate absolute expiration based on options
    private static DateTimeOffset? GetAbsoluteExpiration(DateTimeOffset creationTime, DistributedCacheEntryOptions options)
    {
        if (options.AbsoluteExpiration.HasValue && options.AbsoluteExpiration <= creationTime) throw new ArgumentOutOfRangeException(nameof(DistributedCacheEntryOptions.AbsoluteExpiration), options.AbsoluteExpiration.Value, "The absolute expiration value must be in the future.");
        if (options.AbsoluteExpirationRelativeToNow.HasValue) return creationTime + options.AbsoluteExpirationRelativeToNow;
        return options.AbsoluteExpiration;
    }

    // Helper to handle ReadOnlySequence potentially needing pooling for RedisValue conversion
    private static RedisValue LinearizeToRedisValue(in ReadOnlySequence<byte> value, out byte[]? leased)
    {
        if (value.IsSingleSegment)
        {
            leased = null;
            return value.First; // Can use ReadOnlyMemory directly
        }

        // Multiple segments, need to copy to a contiguous buffer
        var length = checked((int)value.Length);
        leased = ArrayPool<byte>.Shared.Rent(length);
        value.CopyTo(leased);
        // Create ReadOnlyMemory from the leased array segment
        return new ReadOnlyMemory<byte>(leased, 0, length);
    }

    private static void RecycleLeasedArray(byte[]? leased)
    {
        if (leased is not null)
        {
            ArrayPool<byte>.Shared.Return(leased);
        }
    }

     private void CheckDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);
     // Or use Microsoft.AspNetCore.Shared.ObjectDisposedThrowHelper.ThrowIf(_disposed, this);


    #endregion

    // --- Final Dispose ---
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ReleaseConnection(Interlocked.Exchange(ref _connectionMux, null)); // Use null from _connectionMux field
        _connectionLock.Dispose();
    }
}

/*

using lvlup.DataFerry.Buffers;
using lvlup.DataFerry.Orchestrators.Contracts;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using Polly.Wrap;
using StackExchange.Redis;
using System.Buffers;
using System.Runtime.CompilerServices;

namespace lvlup.DataFerry.Orchestrators;

public class CacheOrchestrator : ICacheOrchestrator
{
    private readonly IConnectionMultiplexer _cache;
    private readonly IMemCache<string, byte[]> _memCache;
    private readonly CacheSettings _settings;
    private readonly ILogger<CacheOrchestrator> _logger;
    private readonly ReaderWriterLockSlim _policyLock;
    private PolicyWrap<object> _syncPolicy;
    private AsyncPolicyWrap<object> _asyncPolicy;

    /// <summary>
    /// Initializes a new instance of the <see cref="CacheOrchestrator"/> class.
    /// </summary>
    /// <param name="cache">The Redis connection multiplexer.</param>
    /// <param name="memCache">The in-memory cache.</param>
    /// <param name="settings">The cache settings.</param>
    /// <param name="logger">The logger.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="cache"/>, <paramref name="memCache"/>, <paramref name="settings"/>, or <paramref name="logger"/> is null.</exception>
    public CacheOrchestrator(
        IConnectionMultiplexer cache,
        IMemCache<string, byte[]> memCache,
        IOptions<CacheSettings> settings,
        ILogger<CacheOrchestrator> logger)
    {
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(memCache);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(logger);

        _cache = cache;
        _memCache = memCache;
        _settings = settings.Value;
        _logger = logger;
        _policyLock = new();
        _syncPolicy = PollyPolicyGenerator.GenerateSyncPolicy(_logger, _settings);
        _asyncPolicy = PollyPolicyGenerator.GenerateAsyncPolicy(_logger, _settings);
    }

    /// <inheritdoc/>
    public PolicyWrap<object> SyncPolicy
    {
        get
        {
            _policyLock.EnterReadLock();
            try     { return _syncPolicy; }
            finally { _policyLock.ExitReadLock(); }
        }
        set
        {
            _policyLock.EnterWriteLock();
            try 
            {
                ArgumentNullException.ThrowIfNull(value);
                _syncPolicy = value;
            }
            finally { _policyLock.ExitWriteLock(); }
        }
    }

    /// <inheritdoc/>
    public AsyncPolicyWrap<object> AsyncPolicy
    {
        get
        {
            _policyLock.EnterReadLock();
            try     { return _asyncPolicy; }
            finally { _policyLock.ExitReadLock(); }
        }
        set
        {
            _policyLock.EnterWriteLock();
            try 
            {
                ArgumentNullException.ThrowIfNull(value);
                _asyncPolicy = value;
            } 
            finally { _policyLock.ExitWriteLock(); }
        }
    }

    /// <inheritdoc/>
    public void SetFallbackValue(object value)
    {
        SyncPolicy = PollyPolicyGenerator.GenerateSyncPolicy(_logger, _settings, value);
        AsyncPolicy = PollyPolicyGenerator.GenerateAsyncPolicy(_logger, _settings, value);
    }

    #region SYNCHRONOUS OPERATIONS

    /// <inheritdoc/>
    public void GetFromCache(string key, IBufferWriter<byte> destination)
    {
        // Check the _memCache first
        if (TryGetFromMemoryCache(key, out byte[]? data)
            && data is not null)
        {
            // Success path; write to BufferWriter and return
            _logger.LogDebug("Retrieved entry with key {key} from memory cache.", key);
            destination.Write(data);
            return;
        }

        // If the key does not exist in the _memCache, get a database connection
        IDatabase database = _cache.GetDatabase();

        // Execute against the distributed cache
        object result = _syncPolicy.Execute((context) =>
        {
            _logger.LogDebug("Attempting to retrieve entry with key {key} from cache.", key);
            RedisValue redisValue = database.StringGet(key, CommandFlags.PreferReplica);

            return redisValue.HasValue ? redisValue : default;
        }, new Context($"SparseDistributedCache.GetFromCache for {key}"));

        if (result is RedisValue { HasValue: true } value)
        {
            // We have a value, and RedisValue contains
            // an implicit conversion operator to byte[]
            destination.Write((byte[])value!);
        }
    }

    /// <inheritdoc/>
    public bool SetInCache(string key, byte[] serializedValue, DistributedCacheEntryOptions? options)
    {
        if (_settings.UseMemoryCache)
        {
            _memCache.CheckBackplane();
            _memCache.AddOrUpdate(key, serializedValue, TimeSpan.FromMinutes(_settings.InMemoryAbsoluteExpiration));
        }

        IDatabase database = _cache.GetDatabase();

        // Execute against the distributed cache
        object result = _syncPolicy.Execute((context) =>
        {
            var ttl = options?.SlidingExpiration ?? options?.AbsoluteExpirationRelativeToNow ?? TimeSpan.FromHours(_settings.AbsoluteExpiration);
            _logger.LogDebug("Attempting to add entry with key {key} and ttl {ttl} to cache.", key, ttl);
            return database.StringSet(key, serializedValue, ttl);
        }, new Context($"SparseDistributedCache.SetInCache for {key}"));

        return result as bool?
            ?? default;
    }

    /// <inheritdoc/>
    public bool RefreshInCache(string key, TimeSpan ttl)
    {
        if (_settings.UseMemoryCache)
        {
            _memCache.CheckBackplane();
            _memCache.Refresh(key, TimeSpan.FromMinutes(_settings.InMemoryAbsoluteExpiration));
        }

        IDatabase database = _cache.GetDatabase();

        // Execute against the distributed cache
        object result = _syncPolicy.Execute((context) =>
        {
            _logger.LogDebug("Attempting to refresh entry with key {key} and ttl {ttl} from cache.", key, ttl);
            return database.KeyExpire(key, ttl, ExpireWhen.Always);
        }, new Context($"SparseDistributedCache.RefreshInCache for {key}"));

        return result as bool?
            ?? default;
    }

    /// <inheritdoc/>
    public bool RemoveFromCache(string key)
    {
        if (_settings.UseMemoryCache)
        {
            _memCache.CheckBackplane();
            _memCache.Remove(key);
        }

        IDatabase database = _cache.GetDatabase();

        // Execute against the distributed cache
        object result = _syncPolicy.Execute((context) =>
        {
            _logger.LogDebug("Attempting to remove entry with key {key} from cache.", key);
            return database.KeyDelete(key);
        }, new Context($"SparseDistributedCache.RemoveFromCache for {key}"));

        return result as bool?
            ?? default;
    }

    #endregion
    #region ASYNCHRONOUS OPERATIONS

    /// <inheritdoc/>
    public async ValueTask GetFromCacheAsync(string key, IBufferWriter<byte> destination, CancellationToken token = default)
    {
        // Check the _memCache first
        if (TryGetFromMemoryCache(key, out byte[]? data)
            && data is not null)
        {
            // Success path; write to BufferWriter and return
            _logger.LogDebug("Retrieved entry with key {key} from memory cache.", key);
            destination.Write(data);
            return;
        }

        // If the key does not exist in the _memCache, get a database connection
        IDatabase database = _cache.GetDatabase();

        // Execute against the distributed cache
        object result = await _asyncPolicy.ExecuteAsync(async (ctx, ct) =>
        {
            _logger.LogDebug("Attempting to retrieve entry with key {key} from cache.", key);
            RedisValue redisValue = await database.StringGetAsync(key, CommandFlags.PreferReplica)
                .ConfigureAwait(false);

            return redisValue.HasValue ? redisValue : default;
        }, new Context($"SparseDistributedCache.GetFromCache for {key}"), token).ConfigureAwait(false);

        if (result is RedisValue { HasValue: true } value)
        {
            // We have a value, and RedisValue contains
            // an implicit conversion operator to byte[]
            destination.Write((byte[])value!);
        }
    }

    /// <inheritdoc/>
    public async ValueTask<bool> SetInCacheAsync(string key, byte[] serializedValue, DistributedCacheEntryOptions? options, CancellationToken token = default)
    {
        if (_settings.UseMemoryCache)
        {
            _memCache.CheckBackplane();
            _memCache.AddOrUpdate(key, serializedValue, TimeSpan.FromMinutes(_settings.InMemoryAbsoluteExpiration));
        }

        IDatabase database = _cache.GetDatabase();

        // Execute against the distributed cache
        object result = await _asyncPolicy.ExecuteAsync(async (ctx, ct) =>
        {
            var ttl = options?.SlidingExpiration ?? options?.AbsoluteExpirationRelativeToNow ?? TimeSpan.FromHours(_settings.AbsoluteExpiration);
            _logger.LogDebug("Attempting to add entry with key {key} and ttl {ttl} to cache.", key, ttl);
            return await database.StringSetAsync(key, serializedValue, ttl)
                .ConfigureAwait(false);
        }, new Context($"SparseDistributedCache.SetInCache for {key}"), token).ConfigureAwait(false);

        return result as bool?
            ?? default;
    }

    /// <inheritdoc/>
    public async ValueTask<bool> RefreshInCacheAsync(string key, TimeSpan ttl, CancellationToken token = default)
    {
        if (_settings.UseMemoryCache)
        {
            _memCache.CheckBackplane();
            _memCache.Refresh(key, TimeSpan.FromMinutes(_settings.InMemoryAbsoluteExpiration));
        }

        IDatabase database = _cache.GetDatabase();

        // Execute against the distributed cache
        object result = await _asyncPolicy.ExecuteAsync(async (ctx, ct) =>
        {
            _logger.LogDebug("Attempting to refresh entry with key {key} and ttl {ttl} from cache.", key, ttl);
            return await database.KeyExpireAsync(key, ttl, ExpireWhen.Always)
                .ConfigureAwait(false);
        }, new Context($"SparseDistributedCache.RefreshInCache for {key}"), token).ConfigureAwait(false);

        return result as bool?
            ?? default;
    }

    /// <inheritdoc/>
    public async ValueTask<bool> RemoveFromCacheAsync(string key, CancellationToken token = default)
    {
        if (_settings.UseMemoryCache)
        {
            _memCache.CheckBackplane();
            _memCache.Remove(key);
        }

        IDatabase database = _cache.GetDatabase();

        // Execute against the distributed cache
        object result = await _asyncPolicy.ExecuteAsync(async (ctx, ct) =>
        {
            _logger.LogDebug("Attempting to remove entry with key {key} from cache.", key);
            return await database.KeyDeleteAsync(key)
                .ConfigureAwait(false);
        }, new Context($"SparseDistributedCache.RemoveFromCache for {key}"), token).ConfigureAwait(false);

        return result as bool?
            ?? default;
    }

    #endregion
    #region ASYNCHRONOUS BATCH OPERATIONS

    /// <inheritdoc/>
    public async IAsyncEnumerable<(string Key, int Index, int Length)> GetBatchFromCacheAsync(
        IEnumerable<string> keys, 
        RentedBufferWriter<byte> destination, 
        [EnumeratorCancellation] CancellationToken token = default)
    {
        // Get as many entries from the memory cache as possible
        HashSet<string> remainingKeys = [..keys];
        if (_settings.UseMemoryCache)
        {
            foreach (var key in keys)
            {
                GetFromMemoryCache(key, out var kvp);
                if (kvp.Value is null) continue;

                // Deserialize and write to buffer
                (int index, int length) = destination.WriteAndGetPosition(kvp.Value);
                remainingKeys.Remove(kvp.Key);

                // Return properties of write operation
                yield return (key, index, length);
            }
        }

        // Special case where we retrieved all the keys
        if (remainingKeys.Count == 0) yield break;

        // Setup our connections
        IDatabase database = _cache.GetDatabase();
        IBatch batch = database.CreateBatch();

        // Prepare batch requests
        var redisTasks = remainingKeys.ToDictionary(
            key => key,
            key => GetFromRedisTask(key, batch.StringGetAsync(key, CommandFlags.PreferReplica), destination, token)
        );
        batch.Execute();

        // Process each task and return individual results as we complete them
        await foreach (var task in Task.WhenEach(redisTasks.Values).WithCancellation(token))
        {
            yield return task.Result;
        }
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<KeyValuePair<string, bool>> SetBatchInCacheAsync(
        IDictionary<string, byte[]> data, 
        DistributedCacheEntryOptions? options, 
        [EnumeratorCancellation] CancellationToken token = default)
    {
        // Set our entries in the memory cache
        if (_settings.UseMemoryCache)
        {
            foreach (var kvp in data)
            {
                _memCache.CheckBackplane();
                _memCache.AddOrUpdate(kvp.Key, kvp.Value, TimeSpan.FromMinutes(_settings.InMemoryAbsoluteExpiration));
            }
        }

        // Setup our connections
        IDatabase database = _cache.GetDatabase();
        IBatch batch = database.CreateBatch();

        var ttl = options?.SlidingExpiration ?? options?.AbsoluteExpirationRelativeToNow ?? TimeSpan.FromHours(_settings.AbsoluteExpiration);

        // Prepare batch requests
        var redisTasks = data.ToDictionary(
            kvp => SetInRedisTask(kvp.Key, batch.StringSetAsync(kvp.Key, kvp.Value, ttl, When.Always), ttl, token),
            kvp => kvp.Key
        );
        batch.Execute();

        // Process each task and return individual results as we complete them
        await foreach (var task in Task.WhenEach(redisTasks.Keys).WithCancellation(token))
        {
            yield return new KeyValuePair<string, bool>(redisTasks[task], task.Result);
        }
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<KeyValuePair<string, bool>> RefreshBatchFromCacheAsync(
        IEnumerable<string> keys, 
        TimeSpan ttl, 
        [EnumeratorCancellation] CancellationToken token = default)
    {
        // Set our entries in the memory cache
        if (_settings.UseMemoryCache)
        {
            foreach (var key in keys)
            {
                _memCache.CheckBackplane();
                _memCache.Refresh(key, TimeSpan.FromMinutes(_settings.InMemoryAbsoluteExpiration));
            }
        }

        // Setup our connections
        IDatabase database = _cache.GetDatabase();
        IBatch batch = database.CreateBatch();

        // Prepare batch requests
        var redisTasks = keys.ToDictionary(
            key => RefreshInRedisTask(key, batch.KeyExpireAsync(key, ttl, ExpireWhen.Always), ttl, token),
            key => key
        );
        batch.Execute();

        // Process each task and return individual results as we complete them
        await foreach (var task in Task.WhenEach(redisTasks.Keys).WithCancellation(token))
        {
            yield return new KeyValuePair<string, bool>(redisTasks[task], task.Result);
        }
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<KeyValuePair<string, bool>> RemoveBatchFromCacheAsync(
        IEnumerable<string> keys, 
        [EnumeratorCancellation] CancellationToken token = default)
    {
        // Remove as many entries from the memory cache as possible
        if (_settings.UseMemoryCache)
        {
            foreach (var key in keys)
            {
                _memCache.CheckBackplane();
                _memCache.Remove(key);
            }
        }

        // Setup our connections
        IDatabase database = _cache.GetDatabase();
        IBatch batch = database.CreateBatch();

        // Prepare batch requests
        var redisTasks = keys.ToDictionary(
            key => RemoveFromRedisTask(key, batch.KeyDeleteAsync(key), token),
            key => key
        );
        batch.Execute();

        // Process each task and return individual results as we complete them
        await foreach (var task in Task.WhenEach(redisTasks.Keys).WithCancellation(token))
        {
            yield return new KeyValuePair<string, bool>(redisTasks[task], task.Result);
        }
    }

    #endregion
    #region HELPER METHODS

    /// <summary>
    /// Outputs a <see cref="byte"/> array associated with the key from the memory cache.
    /// </summary>
    /// <param name="key"></param>
    /// <param name="data"></param>
    /// <returns>A <c>bool</c> indicating the result of the operation.</returns>
    private bool TryGetFromMemoryCache(string key, out byte[]? data)
    {
        if (!_settings.UseMemoryCache)
        {
            data = null;
            return false;
        }

        _memCache.CheckBackplane();
        return _memCache.TryGet(key, out data);
    }

    /// <summary>
    /// Outputs a <see cref="KeyValuePair"/> associated with the key from the memory cache.
    /// </summary>
    /// <param name="key"></param>
    /// <param name="kvp"></param>
    private void GetFromMemoryCache(string key, out KeyValuePair<string, byte[]?> kvp)
    {
        _memCache.CheckBackplane();
        kvp = _memCache.TryGet(key, out byte[] data)
            ? new(key, data)
            : new(key, default);
    }

    /// <summary>
    /// Creates the individual Get task for the distributed cache.
    /// </summary>
    /// <param name="key"></param>
    /// <param name="operation"></param>
    /// <param name="destination"></param>
    /// <param name="token"></param>
    /// <returns>A <c>Task</c> containing the result of the operation.</returns>
    private async Task<(string Key, int Index, int Length)> GetFromRedisTask(
        string key, 
        Task<RedisValue> operation, 
        RentedBufferWriter<byte> destination, 
        CancellationToken token)
    {
        object result = await _asyncPolicy.ExecuteAsync(async (ctx, ct) =>
        {
            _logger.LogDebug("Attempting to retrieve entry with key {key} from cache.", key);
            return await operation
                .ConfigureAwait(false);
        },
        new Context($"SparseDistributedCache.GetBatchAsync for {key}"), token).ConfigureAwait(false);

        if (result is RedisValue { HasValue: true } value)
        {
            // We have a value, and RedisValue contains
            // an implicit conversion operator to byte[]
            var (index, length) = destination.WriteAndGetPosition((byte[])value!);

            // Return properties of write operation
            return (key, index, length);
        }

        // Failure case
        return (key, -1, -1);
    }

    /// <summary>
    /// Creates the individual Set task for the distributed cache.
    /// </summary>
    /// <param name="key"></param>
    /// <param name="operation"></param>
    /// <param name="expiration"></param>
    /// <param name="token"></param>
    /// <returns>A <c>Task</c> containing the result of the operation.</returns>
    private async Task<bool> SetInRedisTask(string key, Task<bool> operation, TimeSpan expiration, CancellationToken token)
    {
        object result = await _asyncPolicy.ExecuteAsync(async (ctx, ct) =>
        {
            _logger.LogDebug("Attempting to add entry with key {key} and ttl {ttl} to cache.", key, expiration);
            return await operation
                .ConfigureAwait(false);
        }, new Context($"SparseDistributedCache.SetInCache for {key}"), token).ConfigureAwait(false);

        return result as bool?
            ?? default;
    }

    /// <summary>
    /// Creates the individual Refresh task for the distributed cache.
    /// </summary>
    /// <param name="key"></param>
    /// <param name="operation"></param>
    /// <param name="expiration"></param>
    /// <param name="token"></param>
    /// <returns>A <c>Task</c> containing the result of the operation.</returns>
    private async Task<bool> RefreshInRedisTask(string key, Task<bool> operation, TimeSpan expiration, CancellationToken token)
    {
        object result = await _asyncPolicy.ExecuteAsync(async (ctx, ct) =>
        {
            _logger.LogDebug("Attempting to refresh entry with key {key} and ttl {ttl} from cache.", key, expiration);
            return await operation
                .ConfigureAwait(false);
        }, new Context($"SparseDistributedCache.RefreshInCache for {key}"), token).ConfigureAwait(false);

        return result as bool?
            ?? default;
    }

    /// <summary>
    /// Creates the individual Remove task for the distributed cache.
    /// </summary>
    /// <param name="key"></param>
    /// <param name="operation"></param>
    /// <param name="token"></param>
    /// <returns>A <c>Task</c> containing the result of the operation.</returns>
    private async Task<bool> RemoveFromRedisTask(string key, Task<bool> operation, CancellationToken token)
    {
        object result = await _asyncPolicy.ExecuteAsync(async (ctx, ct) =>
        {
            _logger.LogDebug("Attempting to remove entry with key {key} from cache.", key);
            return await operation
                .ConfigureAwait(false);
        }, new Context($"SparseDistributedCache.RemoveFromCache for {key}"), token).ConfigureAwait(false);

        return result as bool?
            ?? default;
    }

    #endregion
}

*/