// ========================================================================
// <copyright file="PollyPolicyGenerator.cs" company="Level Up Software">
// Copyright (c) Level Up Software. All rights reserved.
// </copyright>
// ========================================================================

using lvlup.DataFerry.Concurrency;
using lvlup.DataFerry.Properties;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Timeout;
using Polly.Wrap;

namespace lvlup.DataFerry.Utilities;

/// <summary>
/// Provides static methods for generating pre-configured Polly <see cref="PolicyWrap"/>
/// and <see cref="AsyncPolicyWrap"/> instances based on specified resiliency settings.
/// This utility class helps centralize the creation of common resilience patterns
/// like Timeout, Retry, Circuit Breaker, Bulkhead, and Fallback for both synchronous
/// and asynchronous operations.
/// </summary>
public static class PollyPolicyGenerator
{
    #region Generator Methods

    /// <summary>
    /// Creates a synchronous policy wrap based on the provided settings.
    /// </summary>
    /// <typeparam name="T">The return type of the operation being protected.</typeparam>
    /// <param name="logger">Logger instance to use for policy logging.</param>
    /// <param name="settings">The configured resiliency settings.</param>
    /// <param name="fallbackValue">The fallback value to return if all policies fail.</param>
    /// <param name="handledExceptionTypes">Specific exception types to handle by Retry and Circuit Breaker. If null or empty, handles System.Exception.</param>
    /// <returns>A configured <see cref="PolicyWrap{TResult}"/>.</returns>
    public static PolicyWrap<T> GenerateSyncPolicy<T>(
        ILogger logger,
        ResiliencySettings settings,
        T fallbackValue,
        params Type[]? handledExceptionTypes)
    {
        ArgumentNullException.ThrowIfNull(logger, nameof(logger));
        ArgumentNullException.ThrowIfNull(settings, nameof(settings));

        var exceptionPredicate = CreateExceptionPredicate(handledExceptionTypes);

        return settings.DesiredPolicy switch
        {
            ResiliencyPatterns.Advanced => GetAdvancedSyncPattern(logger, settings, fallbackValue, exceptionPredicate),
            ResiliencyPatterns.Basic => GetBasicSyncPattern(logger, settings, fallbackValue, exceptionPredicate),
            _ => GetDefaultSyncPattern(logger, settings, fallbackValue)
        };
    }

    /// <summary>
    /// Creates an asynchronous policy wrap based on the provided settings.
    /// </summary>
    /// <typeparam name="T">The return type of the operation being protected.</typeparam>
    /// <param name="logger">Logger instance to use for policy logging.</param>
    /// <param name="settings">The configured resiliency settings.</param>
    /// <param name="fallbackValue">The fallback value to return if all policies fail.</param>
    /// <param name="handledExceptionTypes">Specific exception types to handle by Retry and Circuit Breaker. If null or empty, handles System.Exception.</param>
    /// <returns>A configured <see cref="AsyncPolicyWrap{TResult}"/>.</returns>
    public static AsyncPolicyWrap<T> GenerateAsyncPolicy<T>(
        ILogger logger,
        ResiliencySettings settings,
        T fallbackValue,
        params Type[]? handledExceptionTypes)
    {
        ArgumentNullException.ThrowIfNull(logger, nameof(logger));
        ArgumentNullException.ThrowIfNull(settings, nameof(settings));

        var exceptionPredicate = CreateExceptionPredicate(handledExceptionTypes);

        return settings.DesiredPolicy switch
        {
            ResiliencyPatterns.Advanced => GetAdvancedAsyncPattern(logger, settings, fallbackValue, exceptionPredicate),
            ResiliencyPatterns.Basic => GetBasicAsyncPattern(logger, settings, fallbackValue, exceptionPredicate),
            _ => GetDefaultAsyncPattern(logger, settings, fallbackValue)
        };
    }

    /// <summary>
    /// Creates a predicate function to check if an exception matches the specified types.
    /// </summary>
    /// <param name="handledExceptionTypes">The specific exception types to handle. Handles System.Exception if null or empty.</param>
    /// <returns>A function that returns true if the exception should be handled.</returns>
    internal static Func<Exception, bool> CreateExceptionPredicate(Type[]? handledExceptionTypes)
    {
        if (handledExceptionTypes is null || handledExceptionTypes.Length == 0)
        {
            return _ => true;
        }

        return ex => handledExceptionTypes.Any(type => type.IsAssignableFrom(ex.GetType()));
    }

    #endregion
    #region Async Patterns

    /// <summary>
    /// Creates and returns a pre-configured Polly resilience strategy combining multiple policies
    /// (Fallback, Circuit Breaker, Retry, Bulkhead, Timeout) for asynchronous operations
    /// returning type <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The return type of the operation being protected by the policy and the type of the fallback value.</typeparam>
    /// <param name="logger">The logger instance used for logging policy events (retries, circuit breaker state changes, fallbacks, etc.).</param>
    /// <param name="settings">Configuration object containing settings for the individual policies (e.g., timeout duration, retry count, circuit breaker thresholds).</param>
    /// <param name="fallbackValue">The default value of type <typeparamref name="T"/> to return if the operation fails through all other resilience layers.</param>
    /// <param name="exceptionPredicate">A function that determines whether a given exception should be handled by the Retry and Circuit Breaker policies.
    /// Should return <c>true</c> for exceptions that warrant a retry or contribute to breaking the circuit.</param>
    /// <returns>An <see cref="AsyncPolicyWrap{TResult}"/> instance representing the combined resilience strategy. This policy should be used with <c>ExecuteAsync</c> to protect an operation.</returns>
    /// <remarks>
    /// <para>This method provides an advanced, highly robust resilience pipeline by wrapping several Polly policies together.</para>
    /// <para>The policies are combined in the following order (from innermost to outermost):</para>
    /// <list type="number">
    ///   <item><description><b>Timeout:</b> Applies a time limit to each individual attempt. If an attempt exceeds the timeout, it's aborted (typically throwing <c>TimeoutRejectedException</c>), which may trigger outer policies like Retry or Circuit Breaker.</description></item>
    ///   <item><description><b>Bulkhead:</b> Limits the number of concurrent executions and queued actions before the core operation attempt (and its timeout) begins, preventing overloading downstream systems or exhausting local resources. Applied after Timeout in the execution flow but often considered conceptually just inside Retry.</description></item>
    ///   <item><description><b>Retry:</b> Wraps the inner Bulkhead and Timeout policies. Automatically re-executes the operation if it fails with an exception matching the <paramref name="exceptionPredicate"/>.</description></item>
    ///   <item><description><b>Circuit Breaker:</b> Wraps the Retry policy. Monitors failures based on the <paramref name="exceptionPredicate"/> across potentially retried attempts. Opens the circuit if failure thresholds are met, preventing subsequent calls (including retries) for a configured duration to allow recovery.</description></item>
    ///   <item><description><b>Fallback:</b> The final safety net wrapping all other policies. Returns the provided <paramref name="fallbackValue"/> if the inner policies fail definitively (e.g., circuit breaker open, retries exhausted) for a handled exception.</description></item>
    /// </list>
    /// </remarks>
    internal static AsyncPolicyWrap<T> GetAdvancedAsyncPattern<T>(
        ILogger logger,
        ResiliencySettings settings,
        T fallbackValue,
        Func<Exception, bool> exceptionPredicate)
    {
        // First layer: timeouts
        var timeoutPolicy = Policy.TimeoutAsync(
            TimeSpan.FromSeconds(settings.TimeoutIntervalSeconds),
            TimeoutStrategy.Optimistic);

        // Second layer: bulkhead isolation
        var bulkheadPolicy = Policy.BulkheadAsync(
             settings.BulkheadMaxParallelization,
             settings.BulkheadMaxQueuingActions,
             onBulkheadRejectedAsync: context =>
             {
                 logger.LogWarning("Bulkhead rejected execution. OperationKey: {OperationKey}", context.OperationKey);
                 return Task.CompletedTask;
             });

        // Third layer: automatic retries
        var retryPolicy = Policy
            .Handle(exceptionPredicate)
            .WaitAndRetryAsync(
                retryCount: settings.RetryCount,
                sleepDurationProvider: retryAttempt => CalculateRetryDelay(settings, retryAttempt),
                onRetryAsync: (exception, timeSpan, retryCount, context) =>
                {
                    LogRetryAttempt(logger, settings, exception, timeSpan, retryCount, context);
                    return Task.CompletedTask;
                });

        // Fourth layer: circuit breaker
        var circuitBreakerPolicy = Policy
            .Handle(exceptionPredicate)
            .CircuitBreakerAsync(
                exceptionsAllowedBeforeBreaking: settings.CircuitBreakerCount,
                durationOfBreak: TimeSpan.FromMinutes(settings.CircuitBreakerIntervalMinutes),
                onBreak: (ex, breakDelay, context) =>
                    logger.LogError(ex, "Circuit breaker opened for {BreakDelay}. OperationKey: {OperationKey}", breakDelay, context.OperationKey),
                onReset: context => logger.LogDebug("Circuit breaker reset. OperationKey: {OperationKey}", context.OperationKey),
                onHalfOpen: () => logger.LogDebug("Circuit breaker half-open."));

        // Final layer: return default value
        var fallbackPolicy = Policy<T>
            .Handle<Exception>()
            .FallbackAsync(
                fallbackValue: fallbackValue,
                onFallbackAsync: (exception, context) =>
                {
                    logger.LogError(exception.Exception, "Fallback policy executed for OperationKey: {OperationKey}. Returning fallback value.", context.OperationKey);
                    return Task.CompletedTask;
                });

        // Wrap policies in order (outermost first)
        var combinedPolicy = Policy.WrapAsync(
            circuitBreakerPolicy,
            retryPolicy,
            bulkheadPolicy,
            timeoutPolicy);

        // Ensure fallback is the outermost policy (executed last)
        return fallbackPolicy.WrapAsync(combinedPolicy);
    }

    /// <summary>
    /// Creates and returns a pre-configured Polly resilience strategy combining multiple policies
    /// (Fallback, Retry, Timeout) for asynchronous operations
    /// returning type <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The return type of the operation being protected by the policy and the type of the fallback value.</typeparam>
    /// <param name="logger">The logger instance used for logging policy events (retries, fallbacks, etc.).</param>
    /// <param name="settings">Configuration object containing settings for the individual policies (e.g., timeout duration, retry count).</param>
    /// <param name="fallbackValue">The default value of type <typeparamref name="T"/> to return if the operation fails through all other resilience layers.</param>
    /// <param name="exceptionPredicate">A function that determines whether a given exception should be handled by the Retry policy.
    /// Should return <c>true</c> for exceptions that warrant a retry.</param>
    /// <returns>An <see cref="AsyncPolicyWrap{TResult}"/> instance representing the combined resilience strategy. This policy should be used with <c>ExecuteAsync</c> to protect an operation.</returns>
    /// <remarks>
    /// <para>This method provides a standard, robust resilience pipeline by wrapping several Polly policies together.</para>
    /// <para>The policies are combined in the following order (from innermost to outermost):</para>
    /// <list type="number">
    ///   <item><description><b>Timeout:</b> Applies a time limit to each individual attempt. If an attempt exceeds the timeout, it's aborted (typically throwing <c>TimeoutRejectedException</c>), which may trigger outer policies like Retry.</description></item>
    ///   <item><description><b>Retry:</b> Wraps the inner Bulkhead and Timeout policies. Automatically re-executes the operation if it fails with an exception matching the <paramref name="exceptionPredicate"/>.</description></item>
    ///   <item><description><b>Fallback:</b> The final safety net wrapping all other policies. Returns the provided <paramref name="fallbackValue"/> if the inner policies fail definitively (e.g., retries exhausted) for a handled exception.</description></item>
    /// </list>
    /// </remarks>
    internal static AsyncPolicyWrap<T> GetBasicAsyncPattern<T>(
        ILogger logger,
        ResiliencySettings settings,
        T fallbackValue,
        Func<Exception, bool> exceptionPredicate)
    {
        // First layer: timeouts
        var timeoutPolicy = Policy.TimeoutAsync(
            TimeSpan.FromSeconds(settings.TimeoutIntervalSeconds),
            TimeoutStrategy.Optimistic);

        // Second layer: automatic retries
        var retryPolicy = Policy
            .Handle(exceptionPredicate)
            .WaitAndRetryAsync(
                retryCount: settings.RetryCount,
                sleepDurationProvider: retryAttempt => CalculateRetryDelay(settings, retryAttempt),
                onRetryAsync: (exception, timeSpan, retryCount, context) =>
                {
                    LogRetryAttempt(logger, settings, exception, timeSpan, retryCount, context);
                    return Task.CompletedTask;
                });

        // Final layer: return default value
        var fallbackPolicy = Policy<T>
            .Handle<Exception>()
            .FallbackAsync(
                fallbackValue: fallbackValue,
                onFallbackAsync: (exception, context) =>
                {
                    logger.LogError(exception.Exception, "Fallback policy executed for OperationKey: {OperationKey}. Returning fallback value.", context.OperationKey);
                    return Task.CompletedTask;
                });

        // Wrap policies in order (outermost first)
        var combinedPolicy = Policy.WrapAsync(
            retryPolicy,
            timeoutPolicy);

        // Ensure fallback is the outermost policy (executed last)
        return fallbackPolicy.WrapAsync(combinedPolicy);
    }

    /// <summary>
    /// Creates and returns a pre-configured Polly resilience strategy combining multiple policies
    /// (Retry, Timeout) for asynchronous operations, specifically tailored for background tasks
    /// executed by systems like <see cref="TaskOrchestrator"/> where final exceptions should propagate rather than trigger a fallback value.
    /// </summary>
    /// <param name="logger">The logger instance used for logging policy events (retries, etc.).</param>
    /// <param name="settings">Configuration object containing settings for the individual policies (e.g., timeout duration, retry count).</param>
    /// <param name="exceptionPredicate">A function that determines whether a given exception should be handled by the Retry policy.
    /// Should return <c>true</c> for exceptions that warrant a retry.</param>
    /// <returns>An <see cref="AsyncPolicyWrap"/> instance representing the combined resilience strategy. This policy implements <see cref="IAsyncPolicy"/> and should be used with <c>ExecuteAsync</c> to protect a background operation.</returns>
    /// <remarks>
    /// <para>This method provides a standard resilience pipeline for background tasks by wrapping several Polly policies together.</para>
    /// <para>The policies are combined in the following order (from outermost to innermost - executed in reverse):</para>
    /// <list type="number">
    ///   <item><description><b>Retry:</b> Wraps the inner Timeout policy. Automatically re-executes the operation if it fails with an exception matching the <paramref name="exceptionPredicate"/>.</description></item>
    ///   <item><description><b>Timeout:</b> Applies a time limit to each individual attempt. If an attempt exceeds the timeout, it's aborted (typically throwing <c>Polly.Timeout.TimeoutRejectedException</c>), which is then handled by the outer Retry policy.</description></item>
    /// </list>
    /// <para>Unlike other policy generators that might return a fallback value, this pattern allows exceptions to propagate after all retries are exhausted, which is typically desired for background task orchestrators that handle final logging or dead-lettering externally.</para>
    /// </remarks>
    internal static AsyncPolicyWrap GetAsyncBackgroundTaskPattern(
        ILogger logger,
        ResiliencySettings settings,
        Func<Exception, bool> exceptionPredicate)
    {
        // First layer: timeouts
        var timeoutPolicy = Policy.TimeoutAsync(
            TimeSpan.FromSeconds(settings.TimeoutIntervalSeconds),
            TimeoutStrategy.Optimistic);

        // Second layer: automatic retries
        var retryPolicy = Policy
            .Handle(exceptionPredicate)
            .WaitAndRetryAsync(
                retryCount: settings.RetryCount,
                sleepDurationProvider: retryAttempt => CalculateRetryDelay(settings, retryAttempt),
                onRetryAsync: (exception, timeSpan, retryCount, context) =>
                {
                    LogRetryAttempt(logger, settings, exception, timeSpan, retryCount, context);
                    return Task.CompletedTask;
                });

        // Wrap policies in order (outermost first)
        return Policy.WrapAsync(
            retryPolicy,
            timeoutPolicy);
    }

    /// <summary>
    /// Creates and returns a pre-configured Polly resilience strategy combining multiple policies
    /// (Fallback, Timeout) for asynchronous operations
    /// returning type <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The return type of the operation being protected by the policy and the type of the fallback value.</typeparam>
    /// <param name="logger">The logger instance used for logging policy events.</param>
    /// <param name="settings">Configuration object containing settings for the individual policies (e.g., timeout duration).</param>
    /// <param name="fallbackValue">The default value of type <typeparamref name="T"/> to return if the operation fails through all other resilience layers.</param>
    /// <returns>An <see cref="AsyncPolicyWrap{TResult}"/> instance representing the combined resilience strategy. This policy should be used with <c>ExecuteAsync</c> to protect an operation.</returns>
    /// <remarks>
    /// <para>This method provides a basic resilience pipeline by wrapping Polly policies together.</para>
    /// <para>The policies are combined in the following order (from innermost to outermost):</para>
    /// <list type="number">
    ///   <item><description><b>Timeout:</b> Applies a time limit to each individual attempt. If an attempt exceeds the timeout, it's aborted (typically throwing <c>TimeoutRejectedException</c>).</description></item>
    ///   <item><description><b>Fallback:</b> The final safety net wrapping all other policies. Returns the provided <paramref name="fallbackValue"/> if the inner policies fail definitively for a handled exception.</description></item>
    /// </list>
    /// </remarks>
    internal static AsyncPolicyWrap<T> GetDefaultAsyncPattern<T>(
        ILogger logger,
        ResiliencySettings settings,
        T fallbackValue)
    {
        // First layer: timeouts
        var timeoutPolicy = Policy.TimeoutAsync(
            TimeSpan.FromSeconds(settings.TimeoutIntervalSeconds),
            TimeoutStrategy.Optimistic);

        // Final layer: return default value
        var fallbackPolicy = Policy<T>
            .Handle<Exception>()
            .FallbackAsync(
                fallbackValue: fallbackValue,
                onFallbackAsync: (exception, context) =>
                {
                    logger.LogError(exception.Exception, "Fallback policy executed for OperationKey: {OperationKey}. Returning fallback value.", context.OperationKey);
                    return Task.CompletedTask;
                });

        // Ensure fallback is the outermost policy (executed last)
        return fallbackPolicy.WrapAsync(timeoutPolicy);
    }

    #endregion
    #region Sync Patterns

    /// <summary>
    /// Creates and returns a pre-configured Polly resilience strategy combining multiple policies
    /// (Fallback, Circuit Breaker, Retry, Bulkhead, Timeout) for synchronous operations
    /// returning type <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The return type of the operation being protected by the policy and the type of the fallback value.</typeparam>
    /// <param name="logger">The logger instance used for logging policy events (retries, circuit breaker state changes, fallbacks, etc.).</param>
    /// <param name="settings">Configuration object containing settings for the individual policies (e.g., timeout duration, retry count, circuit breaker thresholds).</param>
    /// <param name="fallbackValue">The default value of type <typeparamref name="T"/> to return if the operation fails through all other resilience layers.</param>
    /// <param name="exceptionPredicate">A function that determines whether a given exception should be handled by the Retry and Circuit Breaker policies.
    /// Should return <c>true</c> for exceptions that warrant a retry or contribute to breaking the circuit.</param>
    /// <returns>An <see cref="PolicyWrap{TResult}"/> instance representing the combined resilience strategy. This policy should be used with <c>ExecuteAsync</c> to protect an operation.</returns>
    /// <remarks>
    /// <para>This method provides an advanced, highly robust resilience pipeline by wrapping several Polly policies together.</para>
    /// <para>The policies are combined in the following order (from innermost to outermost):</para>
    /// <list type="number">
    ///   <item><description><b>Timeout:</b> Applies a time limit to each individual attempt. If an attempt exceeds the timeout, it's aborted (typically throwing <c>TimeoutRejectedException</c>), which may trigger outer policies like Retry or Circuit Breaker.</description></item>
    ///   <item><description><b>Bulkhead:</b> Limits the number of concurrent executions and queued actions before the core operation attempt (and its timeout) begins, preventing overloading downstream systems or exhausting local resources. Applied after Timeout in the execution flow but often considered conceptually just inside Retry.</description></item>
    ///   <item><description><b>Retry:</b> Wraps the inner Bulkhead and Timeout policies. Automatically re-executes the operation if it fails with an exception matching the <paramref name="exceptionPredicate"/>.</description></item>
    ///   <item><description><b>Circuit Breaker:</b> Wraps the Retry policy. Monitors failures based on the <paramref name="exceptionPredicate"/> across potentially retried attempts. Opens the circuit if failure thresholds are met, preventing subsequent calls (including retries) for a configured duration to allow recovery.</description></item>
    ///   <item><description><b>Fallback:</b> The final safety net wrapping all other policies. Returns the provided <paramref name="fallbackValue"/> if the inner policies fail definitively (e.g., circuit breaker open, retries exhausted) for a handled exception.</description></item>
    /// </list>
    /// </remarks>
    internal static PolicyWrap<T> GetAdvancedSyncPattern<T>(
        ILogger logger,
        ResiliencySettings settings,
        T fallbackValue,
        Func<Exception, bool> exceptionPredicate)
    {
        // First layer: timeouts
        var timeoutPolicy = Policy.Timeout(
            TimeSpan.FromSeconds(settings.TimeoutIntervalSeconds),
            TimeoutStrategy.Optimistic);

        // Second layer: bulkhead isolation
        var bulkheadPolicy = Policy.Bulkhead(
             settings.BulkheadMaxParallelization,
             settings.BulkheadMaxQueuingActions,
             onBulkheadRejected: context =>
             {
                 logger.LogWarning("Bulkhead rejected execution. OperationKey: {OperationKey}", context.OperationKey);
             });

        // Third layer: automatic retries
        var retryPolicy = Policy
            .Handle(exceptionPredicate)
            .WaitAndRetry(
                retryCount: settings.RetryCount,
                sleepDurationProvider: retryAttempt => CalculateRetryDelay(settings, retryAttempt),
                onRetry: (exception, timeSpan, retryCount, context) =>
                {
                    LogRetryAttempt(logger, settings, exception, timeSpan, retryCount, context);
                });

        // Fourth layer: circuit breaker
        var circuitBreakerPolicy = Policy
            .Handle(exceptionPredicate)
            .CircuitBreaker(
                exceptionsAllowedBeforeBreaking: settings.CircuitBreakerCount,
                durationOfBreak: TimeSpan.FromMinutes(settings.CircuitBreakerIntervalMinutes),
                onBreak: (ex, breakDelay, context) =>
                    logger.LogError(ex, "Circuit breaker opened for {BreakDelay}. OperationKey: {OperationKey}", breakDelay, context.OperationKey),
                onReset: context => logger.LogDebug("Circuit breaker reset. OperationKey: {OperationKey}", context.OperationKey),
                onHalfOpen: () => logger.LogDebug("Circuit breaker half-open."));

        // Final layer: return default value
        var fallbackPolicy = Policy<T>
            .Handle<Exception>()
            .Fallback(
                fallbackValue: fallbackValue,
                onFallback: (exception, context) =>
                {
                    logger.LogError(exception.Exception, "Fallback policy executed for OperationKey: {OperationKey}. Returning fallback value.", context.OperationKey);
                });

        // Wrap policies in order (outermost first)
        var combinedPolicy = Policy.Wrap(
            circuitBreakerPolicy,
            retryPolicy,
            bulkheadPolicy,
            timeoutPolicy);

        // Ensure fallback is the outermost policy (executed last)
        return fallbackPolicy.Wrap(combinedPolicy);
    }

    /// <summary>
    /// Creates and returns a pre-configured Polly resilience strategy combining multiple policies
    /// (Fallback, Retry, Timeout) for synchronous operations
    /// returning type <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The return type of the operation being protected by the policy and the type of the fallback value.</typeparam>
    /// <param name="logger">The logger instance used for logging policy events (retries, fallbacks, etc.).</param>
    /// <param name="settings">Configuration object containing settings for the individual policies (e.g., timeout duration, retry count).</param>
    /// <param name="fallbackValue">The default value of type <typeparamref name="T"/> to return if the operation fails through all other resilience layers.</param>
    /// <param name="exceptionPredicate">A function that determines whether a given exception should be handled by the Retry policy.
    /// Should return <c>true</c> for exceptions that warrant a retry.</param>
    /// <returns>An <see cref="PolicyWrap{TResult}"/> instance representing the combined resilience strategy. This policy should be used with <c>ExecuteAsync</c> to protect an operation.</returns>
    /// <remarks>
    /// <para>This method provides a standard, robust resilience pipeline by wrapping several Polly policies together.</para>
    /// <para>The policies are combined in the following order (from innermost to outermost):</para>
    /// <list type="number">
    ///   <item><description><b>Timeout:</b> Applies a time limit to each individual attempt. If an attempt exceeds the timeout, it's aborted (typically throwing <c>TimeoutRejectedException</c>), which may trigger outer policies like Retry.</description></item>
    ///   <item><description><b>Retry:</b> Wraps the inner Bulkhead and Timeout policies. Automatically re-executes the operation if it fails with an exception matching the <paramref name="exceptionPredicate"/>.</description></item>
    ///   <item><description><b>Fallback:</b> The final safety net wrapping all other policies. Returns the provided <paramref name="fallbackValue"/> if the inner policies fail definitively (e.g., retries exhausted) for a handled exception.</description></item>
    /// </list>
    /// </remarks>
    internal static PolicyWrap<T> GetBasicSyncPattern<T>(
        ILogger logger,
        ResiliencySettings settings,
        T fallbackValue,
        Func<Exception, bool> exceptionPredicate)
    {
        // First layer: timeouts
        var timeoutPolicy = Policy.Timeout(
            TimeSpan.FromSeconds(settings.TimeoutIntervalSeconds),
            TimeoutStrategy.Optimistic);

        // Second layer: automatic retries
        var retryPolicy = Policy
            .Handle(exceptionPredicate)
            .WaitAndRetry(
                retryCount: settings.RetryCount,
                sleepDurationProvider: retryAttempt => CalculateRetryDelay(settings, retryAttempt),
                onRetry: (exception, timeSpan, retryCount, context) =>
                {
                    LogRetryAttempt(logger, settings, exception, timeSpan, retryCount, context);
                });

        // Final layer: return default value
        var fallbackPolicy = Policy<T>
            .Handle<Exception>()
            .Fallback(
                fallbackValue: fallbackValue,
                onFallback: (exception, context) =>
                {
                    logger.LogError(exception.Exception, "Fallback policy executed for OperationKey: {OperationKey}. Returning fallback value.", context.OperationKey);
                });

        // Wrap policies in order (outermost first)
        var combinedPolicy = Policy.Wrap(
            retryPolicy,
            timeoutPolicy);

        // Ensure fallback is the outermost policy (executed last)
        return fallbackPolicy.Wrap(combinedPolicy);
    }

    /// <summary>
    /// Creates and returns a pre-configured Polly resilience strategy combining multiple policies
    /// (Fallback, Timeout) for synchronous operations
    /// returning type <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">The return type of the operation being protected by the policy and the type of the fallback value.</typeparam>
    /// <param name="logger">The logger instance used for logging policy events.</param>
    /// <param name="settings">Configuration object containing settings for the individual policies (e.g., timeout duration).</param>
    /// <param name="fallbackValue">The default value of type <typeparamref name="T"/> to return if the operation fails through all other resilience layers.</param>
    /// <returns>An <see cref="PolicyWrap{TResult}"/> instance representing the combined resilience strategy. This policy should be used with <c>ExecuteAsync</c> to protect an operation.</returns>
    /// <remarks>
    /// <para>This method provides a basic resilience pipeline by wrapping Polly policies together.</para>
    /// <para>The policies are combined in the following order (from innermost to outermost):</para>
    /// <list type="number">
    ///   <item><description><b>Timeout:</b> Applies a time limit to each individual attempt. If an attempt exceeds the timeout, it's aborted (typically throwing <c>TimeoutRejectedException</c>).</description></item>
    ///   <item><description><b>Fallback:</b> The final safety net wrapping all other policies. Returns the provided <paramref name="fallbackValue"/> if the inner policies fail definitively for a handled exception.</description></item>
    /// </list>
    /// </remarks>
    internal static PolicyWrap<T> GetDefaultSyncPattern<T>(
        ILogger logger,
        ResiliencySettings settings,
        T fallbackValue)
    {
        // First layer: timeouts
        var timeoutPolicy = Policy.Timeout(
            TimeSpan.FromSeconds(settings.TimeoutIntervalSeconds),
            TimeoutStrategy.Optimistic);

        // Final layer: return default value
        var fallbackPolicy = Policy<T>
            .Handle<Exception>()
            .Fallback(
                fallbackValue: fallbackValue,
                onFallback: (exception, context) =>
                {
                    logger.LogError(exception.Exception, "Fallback policy executed for OperationKey: {OperationKey}. Returning fallback value.", context.OperationKey);
                });

        // Ensure fallback is the outermost policy (executed last)
        return fallbackPolicy.Wrap(timeoutPolicy);
    }

    #endregion
    #region Helper Methods

    /// <summary>
    /// Calculates the delay TimeSpan for the next retry attempt based on the configured strategy (exponential backoff or fixed) and adds jitter.
    /// </summary>
    /// <param name="settings">The resiliency settings containing retry configuration (RetryIntervalSeconds, UseExponentialBackoff).</param>
    /// <param name="retryAttempt">The current retry attempt number (1-based for the first retry).</param>
    /// <returns>A TimeSpan representing the calculated delay before the next retry, including jitter. Ensures a minimum positive delay.</returns>
    private static TimeSpan CalculateRetryDelay(
        ResiliencySettings settings,
        int retryAttempt)
    {
        // Calculate base delay
        TimeSpan baseDelay = settings.UseExponentialBackoff
            ? TimeSpan.FromSeconds(Math.Pow(2, retryAttempt))
            : TimeSpan.FromSeconds(settings.RetryIntervalSeconds);

        // Add jitter: random +/- 0-100ms
        var jitter = TimeSpan.FromMilliseconds(Random.Shared.Next(-100, 100)); // Range changed slightly
        var calculatedDelay = baseDelay + jitter;

        // Ensure delay is not negative (or excessively short)
        return calculatedDelay > TimeSpan.Zero
            ? calculatedDelay
            : TimeSpan.FromMilliseconds(100);
    }

    /// <summary>
    /// Logs information about a retry attempt, including the reason (exception), delay, attempt number, and operation key.
    /// </summary>
    /// <param name="logger">The logger instance to write log entries to.</param>
    /// <param name="settings">The resiliency settings containing the total configured RetryCount.</param>
    /// <param name="exception">The exception that triggered the retry attempt.</param>
    /// <param name="timeSpan">The calculated delay that occurred before this retry attempt.</param>
    /// <param name="retryCount">The current retry attempt number (1-based for the first retry).</param>
    /// <param name="context">The Polly execution context, used here to retrieve the OperationKey.</param>
    private static void LogRetryAttempt(
        ILogger logger,
        ResiliencySettings settings,
        Exception exception,
        TimeSpan timeSpan,
        int retryCount,
        Context context)
    {
        bool isFinalAttempt = retryCount == settings.RetryCount;

        var logLevel = isFinalAttempt
            ? LogLevel.Error
            : LogLevel.Warning;

        string message = isFinalAttempt
            ? $"Retry limit ({settings.RetryCount}) reached for OperationKey: {context.OperationKey}. Final exception before fallback/circuit breaker."
            : $"Retry {retryCount} of {settings.RetryCount} for OperationKey: {context.OperationKey}. Delay: {timeSpan.TotalSeconds:N2}s. Reason:";

        // Log the exception details along with the message using structured logging
        logger.Log(logLevel, exception, message);
    }

    #endregion
}