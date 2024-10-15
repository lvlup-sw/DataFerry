namespace lvlup.DataFerry.Properties
{
    /// <summary>
    /// Cache settings for <see cref="SparseDistributedCache{T}"/>.
    /// </summary>
    /// <remarks>
    /// You need to pass an instance of this class to the <see cref="DataFerry{T}"/>.
    /// </remarks>
    public class CacheSettings
    {
        /// <summary>
        /// The resiliency pattern to follow. Determines the Polly policy generated.
        /// </summary>
        public ResiliencyPatterns DesiredPolicy { get; set; }

        /// <summary>
        /// Retrieves or sets the number of times to retry a cache operation.
        /// </summary>
        public int RetryCount { get; set; } = 1;

        /// <summary>
        /// Retrieves or sets the interval between cache operation retries.
        /// </summary>
        public int RetryInterval { get; set; } = 2;

        /// <summary>
        /// Set to true to use exponential backoff for cache operation retries.
        /// </summary>
        public bool UseExponentialBackoff { get; set; } = true;

        /// <summary>
        /// Retrieves or sets the expiration of the cache in hours.
        /// </summary>
        public int AbsoluteExpiration { get; set; } = 24;

        /// <summary>
        /// Retrieves or sets the expiration of the memory cache in minutes.
        /// </summary>
        public int InMemoryAbsoluteExpiration { get; set; } = 60;

        /// <summary>
        /// Set to true to use In-Memory Caching.
        /// </summary>
        public bool UseMemoryCache { get; set; } = true;

        /// <summary>
        /// Value used for timeout policy in seconds.
        /// </summary>
        public int TimeoutInterval { get; set; } = 30;

        /// <summary>
        /// Maximum number of concurrent transactions.
        /// </summary>
        public int BulkheadMaxParallelization { get; set; } = 10;

        /// <summary>
        /// Maximum number of enqueued transactions allowed.
        /// </summary>
        public int BulkheadMaxQueuingActions { get; set; } = 100;

        /// <summary>
        /// How many exceptions are tolerated before restricting executions.
        /// </summary>
        public int CircuitBreakerCount { get; set; } = 3;

        /// <summary>
        /// Amount of time in minutes before retrying the execution after being restricted.
        /// </summary>
        public int CircuitBreakerInterval { get; set; } = 1;
    }
}
