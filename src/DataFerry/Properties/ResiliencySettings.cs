// ===========================================================================
// <copyright file="ResiliencySettings.cs" company="Level Up Software">
// Copyright (c) Level Up Software. All rights reserved.
// </copyright>
// ===========================================================================

namespace lvlup.DataFerry.Properties;

/// <summary>
/// Resiliency settings for <see cref="DataFerry"/>.
/// </summary>
public class ResiliencySettings
{
    /// <summary>
    /// The resiliency pattern to follow. Determines the Polly policy generated.
    /// </summary>
        public ResiliencyPatterns DesiredPolicy { get; init; } = ResiliencyPatterns.Basic;

    /// <summary>
    /// Gets or sets the number of retries to attempt.
    /// </summary>
    public int RetryCount { get; init; } = 3;

    /// <summary>
    /// Gets or sets the interval between retries in seconds.
    /// </summary>
    public int RetryIntervalSeconds { get; init; } = 2;

    /// <summary>
    /// Gets or sets a value indicating whether to use exponential backoff for retries.
    /// </summary>
    public bool UseExponentialBackoff { get; init; } = true;

    /// <summary>
    /// Gets or sets the timeout interval in seconds.
    /// </summary>
    public int TimeoutIntervalSeconds { get; init; } = 60;

    /// <summary>
    /// Gets or sets the duration of the break in the circuit breaker pattern in seconds.
    /// </summary>
    public int CircuitBreakerDurationSeconds { get; init; } = 30;

    /// <summary>
    /// Gets or sets the number of consecutive exceptions before the circuit breaks.
    /// </summary>
    public int CircuitBreakerExceptionCount { get; init; } = 5;

    /// <summary>
    /// Gets or sets the maximum number of parallel executions in the bulkhead pattern.
    /// </summary>
    public int BulkheadMaxParallelization { get; init; } = 10;

    /// <summary>
    /// Gets or sets the maximum number of items that can be queued in the bulkhead pattern.
    /// </summary>
    public int BulkheadMaxQueuingActions { get; init; } = 5;
}