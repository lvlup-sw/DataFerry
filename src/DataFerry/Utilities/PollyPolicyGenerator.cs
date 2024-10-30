using Polly.Wrap;
using Polly;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace lvlup.DataFerry.Utilities
{
    public static class PollyPolicyGenerator
    {
        private static ILogger? _logger;
        private static CacheSettings? _settings;

        /// <summary>
        /// Creates a synchronous policy for handling exceptions when accessing the cache.
        /// </summary>
        /// <param name="logger">Logger instance to use for policy.</param>
        /// <param name="settings">The configured cache settings.</param>
        /// <param name="configuredValue">The configured fall back value for the cache.</param>
        public static PolicyWrap<object> GenerateSyncPolicy(ILogger logger, CacheSettings settings, object? configuredValue = null)
        {
            _logger = logger;
            _settings = settings;

            ArgumentNullException.ThrowIfNull(logger, nameof(logger));
            ArgumentNullException.ThrowIfNull(settings, nameof(settings));

            return (settings.DesiredPolicy) switch
            {
                ResiliencyPatterns.Advanced => GetAdvancedSyncPattern(configuredValue),
                ResiliencyPatterns.Basic => GetBasicSyncPattern(configuredValue),
                _ => GetDefaultSyncPattern()
            };
        }

        /// <summary>
        /// Creates an asynchronous policy for handling exceptions when accessing the cache.
        /// </summary>
        /// <param name="logger">Logger instance to use for policy.</param>
        /// <param name="settings">The configured cache settings.</param>
        /// <param name="configuredValue">The configured fall back value for the cache.</param>
        public static AsyncPolicyWrap<object> GenerateAsyncPolicy(ILogger logger, CacheSettings settings, object? configuredValue = null)
        {
            _logger = logger;
            _settings = settings;

            ArgumentNullException.ThrowIfNull(logger, nameof(logger));
            ArgumentNullException.ThrowIfNull(settings, nameof(settings));

            return (settings.DesiredPolicy) switch
            {
                ResiliencyPatterns.Advanced => GetAdvancedAsyncPattern(configuredValue),
                ResiliencyPatterns.Basic => GetBasicAsyncPattern(configuredValue),
                _ => GetDefaultAsyncPattern()
            };
        }

        private static PolicyWrap<object> GetAdvancedSyncPattern(object? configuredValue = null)
        {
            if (_settings is null || _logger is null)
            {
                return GetDefaultSyncPattern();
            }

            // First layer: timeouts
            var timeoutPolicy = Policy.Timeout(
                TimeSpan.FromSeconds(_settings.TimeoutInterval));

            // Second layer: bulkhead isolation
            var bulkheadPolicy = Policy.Bulkhead(
                maxParallelization: _settings.BulkheadMaxParallelization,
                maxQueuingActions: _settings.BulkheadMaxQueuingActions
            );

            // Third layer: circuit breaker
            var circuitBreakerPolicy = Policy
                .Handle<TimeoutException>()
                .Or<DbUpdateException>()
                .Or<DbUpdateConcurrencyException>()
            .CircuitBreaker(
                exceptionsAllowedBeforeBreaking: _settings.CircuitBreakerCount,
                durationOfBreak: TimeSpan.FromMinutes(_settings.CircuitBreakerInterval),
                onBreak: (ex, breakDelay) =>
                    _logger.LogError("Circuit breaker opened. Exception: {Exception}, Delay: {BreakDelay}", ex, breakDelay),
                onReset: () => _logger.LogDebug("Circuit breaker reset."),
                onHalfOpen: () => _logger.LogDebug("Circuit breaker half-open.")
            );

            // Fourth layer: automatic retries
            var retryPolicy = Policy
                .Handle<TimeoutException>()
                .Or<DbUpdateConcurrencyException>()
                .Or<DbUpdateException>()
                .WaitAndRetry(
                    _settings.RetryCount,
                    CalculateRetryDelay,
                    LogRetryAttempt
                );

            // Last resort: return default value
            var fallbackPolicy = Policy<object>
                .Handle<Exception>()
                .Fallback(
                    fallbackValue: configuredValue ?? RedisValue.Null,
                    onFallback: (exception, context) =>
                    {
                        _logger.LogError("Fallback executed due to: {exception}", exception);
                        return;
                    });

            // Wrap policies in order
            var combinedPolicy = Policy.Wrap(
                timeoutPolicy,
                bulkheadPolicy,
                circuitBreakerPolicy,
                retryPolicy
            );

            return fallbackPolicy.Wrap(combinedPolicy);
        }

        private static AsyncPolicyWrap<object> GetAdvancedAsyncPattern(object? configuredValue = null)
        {
            if (_settings is null || _logger is null)
            {
                return GetDefaultAsyncPattern();
            }

            // First layer: timeouts
            var timeoutPolicy = Policy.TimeoutAsync(
                TimeSpan.FromSeconds(_settings.TimeoutInterval));

            // Second layer: bulkhead isolation
            var bulkheadPolicy = Policy.BulkheadAsync(
                maxParallelization: _settings.BulkheadMaxParallelization,
                maxQueuingActions: _settings.BulkheadMaxQueuingActions
            );

            // Third layer: circuit breaker
            var circuitBreakerPolicy = Policy
                .Handle<TimeoutException>()
                .Or<DbUpdateException>()
                .Or<DbUpdateConcurrencyException>()
            .CircuitBreakerAsync(
                    exceptionsAllowedBeforeBreaking: _settings.CircuitBreakerCount,
                    durationOfBreak: TimeSpan.FromMinutes(_settings.CircuitBreakerInterval),
                    onBreak: (ex, breakDelay) =>
                        _logger.LogError("Circuit breaker opened. Exception: {Exception}, Delay: {BreakDelay}", ex, breakDelay),
                    onReset: () => _logger.LogDebug("Circuit breaker reset."),
                    onHalfOpen: () => _logger.LogDebug("Circuit breaker half-open.")
                );

            // Fourth layer: automatic retries
            var retryPolicy = Policy
                .Handle<TimeoutException>()
                .Or<DbUpdateConcurrencyException>()
                .Or<DbUpdateException>()
                .WaitAndRetryAsync(
                    _settings.RetryCount,
                    CalculateRetryDelay,
                    LogRetryAttempt
                );

            // Last resort: return default value
            var fallbackPolicy = Policy<object>
                .Handle<Exception>()
                .FallbackAsync(
                    fallbackValue: configuredValue ?? RedisValue.Null,
                    onFallbackAsync: (exception, context) =>
                    {
                        _logger.LogError("Fallback executed due to: {exception}", exception);
                        return Task.CompletedTask;
                    });

            // Wrap policies in order
            var combinedPolicy = Policy.WrapAsync(
                timeoutPolicy,
                bulkheadPolicy,
                circuitBreakerPolicy,
                retryPolicy
            );

            return fallbackPolicy.WrapAsync(combinedPolicy);
        }

        private static PolicyWrap<object> GetBasicSyncPattern(object? configuredValue = null)
        {
            if (_settings is null || _logger is null)
            {
                return GetDefaultSyncPattern();
            }

            // First layer: timeouts
            var timeoutPolicy = Policy.Timeout(
                TimeSpan.FromSeconds(_settings.TimeoutInterval));

            // Second layer: automatic retries
            var retryPolicy = Policy
                .Handle<TimeoutException>()
                .Or<DbUpdateConcurrencyException>()
                .Or<DbUpdateException>()
                .WaitAndRetry(
                    _settings.RetryCount,
                    CalculateRetryDelay,
                    LogRetryAttempt
                );

            // Last resort: return default value
            var fallbackPolicy = Policy<object>
                .Handle<Exception>()
                .Fallback(
                    fallbackValue: configuredValue ?? RedisValue.Null,
                    onFallback: (exception, context) =>
                    {
                        _logger.LogError("Fallback executed due to: {exception}", exception);
                    });

            // Wrap policies in order
            var combinedPolicy = Policy.Wrap(
                timeoutPolicy,
                retryPolicy
            );

            return fallbackPolicy.Wrap(combinedPolicy);
        }

        private static AsyncPolicyWrap<object> GetBasicAsyncPattern(object? configuredValue = null)
        {
            if (_settings is null || _logger is null)
            {
                return GetDefaultAsyncPattern();
            }

            // First layer: timeouts
            var timeoutPolicy = Policy.TimeoutAsync(
                TimeSpan.FromSeconds(_settings.TimeoutInterval));

            // Second layer: automatic retries
            var retryPolicy = Policy
                .Handle<TimeoutException>()
                .Or<DbUpdateConcurrencyException>()
                .Or<DbUpdateException>()
                .WaitAndRetryAsync(
                    _settings.RetryCount,
                    CalculateRetryDelay,
                    LogRetryAttempt
                );

            // Last resort: return default value
            var fallbackPolicy = Policy<object>
                .Handle<Exception>()
                .FallbackAsync(
                    fallbackValue: configuredValue ?? RedisValue.Null,
                    onFallbackAsync: (exception, context) =>
                    {
                        _logger.LogError("Fallback executed due to: {exception}", exception);
                        return Task.CompletedTask;
                    });

            // Wrap policies in order
            var combinedPolicy = Policy.WrapAsync(
                timeoutPolicy,
                retryPolicy
            );

            return fallbackPolicy.WrapAsync(retryPolicy);
        }

        private static PolicyWrap<object> GetDefaultSyncPattern()
        {
            // First layer: timeouts
            var timeoutPolicy = Policy.Timeout(
                TimeSpan.FromSeconds(30));

            // Last resort: return default value
            var fallbackPolicy = Policy<object>
                .Handle<Exception>()
                .Fallback(
                    fallbackValue: string.Empty,
                    onFallback: (exception, context) => { }); 

            return fallbackPolicy.Wrap(timeoutPolicy);
        }

        private static AsyncPolicyWrap<object> GetDefaultAsyncPattern()
        {
            // First layer: timeouts
            var timeoutPolicy = Policy.TimeoutAsync(
                TimeSpan.FromSeconds(30));

            // Last resort: return default value
            var fallbackPolicy = Policy<object>
                .Handle<Exception>()
                .FallbackAsync(
                    fallbackValue: string.Empty,
                    onFallbackAsync: (exception, context) =>
                    {
                        return Task.CompletedTask;
                    });

            return fallbackPolicy.WrapAsync(timeoutPolicy);
        }

        // Exponential backoff or fixed value
        private static TimeSpan CalculateRetryDelay(int retryAttempt)
        {
            if (_settings is null || _logger is null)
            {
                return TimeSpan.FromSeconds(10);
            }

            var baseDelay = _settings.UseExponentialBackoff
            ? TimeSpan.FromSeconds(Math.Pow(2, retryAttempt))
                : TimeSpan.FromSeconds(_settings.RetryInterval);

            // Add jitter
            return baseDelay + TimeSpan.FromMilliseconds(Random.Shared.Next(0, 100));
        }

        private static void LogRetryAttempt(Exception exception, TimeSpan timeSpan, int retryCount, Context context)
        {
            if (_settings is null || _logger is null)
            {
                return;
            }

            bool retriesReached = retryCount == _settings.RetryCount;

            var logLevel = retriesReached
                ? LogLevel.Error
                : LogLevel.Debug;
            var message = retriesReached
                ? $"Retry limit of {_settings.RetryCount} reached. {exception}"
                : $"Retry {retryCount} of {_settings.RetryCount} after {timeSpan.TotalSeconds} seconds delay due to: {exception}";

            _logger.Log(logLevel, message);
        }
    }
}
