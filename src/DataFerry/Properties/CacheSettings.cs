namespace lvlup.DataFerry.Properties;

/// <summary>
/// Cache settings for <see cref="DataFerry"/>.
/// </summary>
public class CacheSettings
{
    /// <summary>
    /// The resiliency pattern to follow. Determines the Polly policy generated.
    /// </summary>
    public ResiliencyPatterns DesiredPolicy { get; init; } = ResiliencyPatterns.Advanced;

    /// <summary>
    /// Retrieves or sets the number of times to retry a cache operation.
    /// </summary>
    public int RetryCount { get; init; } = 1;

    /// <summary>
    /// Retrieves or sets the interval between cache operation retries.
    /// </summary>
    public int RetryInterval { get; init; } = 2;

    /// <summary>
    /// Set to true to use exponential backoff for cache operation retries.
    /// </summary>
    public bool UseExponentialBackoff { get; init; } = true;

    /// <summary>
    /// Retrieves or sets the expiration of the cache in hours.
    /// </summary>
    public int AbsoluteExpiration { get; init; } = 24;

    /// <summary>
    /// Retrieves or sets the expiration of the memory cache in minutes.
    /// </summary>
    public int InMemoryAbsoluteExpiration { get; init; } = 60;

    /// <summary>
    /// Set to true to use In-Memory Caching.
    /// </summary>
    public bool UseMemoryCache { get; init; } = true;

    /// <summary>
    /// Value used for timeout policy in seconds.
    /// </summary>
    public int TimeoutInterval { get; init; } = 30;

    /// <summary>
    /// Maximum number of concurrent transactions.
    /// </summary>
    public int BulkheadMaxParallelization { get; init; } = 10;

    /// <summary>
    /// Maximum number of enqueued transactions allowed.
    /// </summary>
    public int BulkheadMaxQueuingActions { get; init; } = 100;

    /// <summary>
    /// How many exceptions are tolerated before restricting executions.
    /// </summary>
    public int CircuitBreakerCount { get; init; } = 3;

    /// <summary>
    /// Amount of time in minutes before retrying the execution after being restricted.
    /// </summary>
    public int CircuitBreakerInterval { get; init; } = 1;
}