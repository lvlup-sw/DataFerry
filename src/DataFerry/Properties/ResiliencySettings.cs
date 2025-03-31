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
    /// Retrieves or sets the number of times to retry an operation.
    /// </summary>
    public int RetryCount { get; init; } = 3;

    /// <summary>
    /// Retrieves or sets the interval in seconds between operation retries.
    /// </summary>
    public int RetryIntervalSeconds { get; init; } = 2;

    /// <summary>
    /// Set to true to use exponential backoff for operation retries.
    /// </summary>
    public bool UseExponentialBackoff { get; init; } = true;

    /// <summary>
    /// Value used for timeout policy in seconds.
    /// </summary>
    public int TimeoutIntervalSeconds { get; init; } = 5;

    /// <summary>
    /// Maximum number of concurrent transactions.
    /// </summary>
    public int BulkheadMaxParallelization { get; init; } = 10;

    /// <summary>
    /// Maximum number of enqueued transactions allowed.
    /// </summary>
    public int BulkheadMaxQueuingActions { get; init; } = 20;

    /// <summary>
    /// How many exceptions are tolerated before restricting executions.
    /// </summary>
    public int CircuitBreakerCount { get; init; } = 3;

    /// <summary>
    /// Amount of time in minutes before retrying the execution after being restricted.
    /// </summary>
    public int CircuitBreakerIntervalMinutes { get; init; } = 1;
}