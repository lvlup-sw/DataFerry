namespace DataFerry.Properties
{
    /// <summary>
    /// Cache settings for <see cref="DistributedCache{T}"/>.
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
        public int RetryCount { get; set; }

        /// <summary>
        /// Retrieves or sets the interval between cache operation retries.
        /// </summary>
        public int RetryInterval { get; set; }

        /// <summary>
        /// Set to true to use exponential backoff for cache operation retries.
        /// </summary>
        public bool UseExponentialBackoff { get; set; }

        /// <summary>
        /// Retrieves or sets the expiration of the cache in hours.
        /// </summary>
        public int AbsoluteExpiration { get; set; }

        /// <summary>
        /// Retrieves or sets the expiration of the memory cache in minutes.
        /// </summary>
        public int InMemoryAbsoluteExpiration { get; set; }

        /// <summary>
        /// Set to true to use In-Memory Caching.
        /// </summary>
        public bool UseMemoryCache { get; set; }

        /// <summary>
        /// Value used for timeout policy in seconds.
        /// </summary>
        public int TimeoutInterval { get; set; }

        /// <summary>
        /// Maximum number of concurrent transactions.
        /// </summary>
        public int BulkheadMaxParallelization { get; set; }

        /// <summary>
        /// Maximum number of enqueued transactions allowed.
        /// </summary>
        public int BulkheadMaxQueuingActions { get; set; }

        /// <summary>
        /// How many exceptions are tolerated before restricting executions.
        /// </summary>
        public int CircuitBreakerCount { get; set; }

        /// <summary>
        /// Amount of time in minutes before retrying the execution after being restricted.
        /// </summary>
        public int CircuitBreakerInterval { get; set; }
    }
}
