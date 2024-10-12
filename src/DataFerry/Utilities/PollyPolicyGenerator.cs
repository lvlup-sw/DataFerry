using Polly.Wrap;
using Polly;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace lvlup.DataFerry.Utilities
{
    public static class PollyPolicyGenerator
    {
        internal static ILogger? _logger { get; private set; }
        internal static CacheSettings? _settings { get; private set; }

        /// <summary>
        /// Creates a policy for handling exceptions when accessing the cache.
        /// </summary>
        /// <param name="logger">Logger instance to use for policy.</param>
        /// <param name="settings">The configured cache settings.</param>
        /// <param name="configuredValue">The configured fall back value for the cache.</param>
        public static AsyncPolicyWrap<object> GeneratePolicy(ILogger logger, CacheSettings settings, object? configuredValue = null)
        {
            _logger = logger;
            _settings = settings;

            ArgumentNullException.ThrowIfNull(logger, nameof(logger));
            ArgumentNullException.ThrowIfNull(settings, nameof(settings));

            return (settings.DesiredPolicy) switch
            {
                ResiliencyPatterns.Advanced => GetAdvancedPattern(configuredValue),
                ResiliencyPatterns.Basic => GetBasicPattern(configuredValue),
                _ => GetDefaultPattern()
            };
        }

        private static AsyncPolicyWrap<object> GetAdvancedPattern(object? configuredValue = null)
        {
            if (_settings is null || _logger is null)
            {
                return GetDefaultPattern();
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

        private static AsyncPolicyWrap<object> GetBasicPattern(object? configuredValue = null)
        {
            if (_settings is null || _logger is null)
            {
                return GetDefaultPattern();
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

        private static AsyncPolicyWrap<object> GetDefaultPattern()
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
