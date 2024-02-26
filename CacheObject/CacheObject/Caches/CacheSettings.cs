namespace CacheObject.Caches
{
    /// <summary>
    /// Cache settings for the <see cref="DistributedCache{T}"/> class.
    /// </summary>
    public class CacheSettings
    {
        /// <summary>
        /// Retrieves or sets the number of times to retry a cache operation.
        /// </summary>
        public int RetryCount { get; set; }

        /// <summary>
        /// Retrieves or sets the interval between cache operation retries.
        /// </summary>
        public int RetryInterval { get; set; }

        /// <summary>
        /// Retrieves or sets the expiration of the cache in minutes.
        /// </summary>
        public int AbsoluteExpiration { get; set; }
    }
}
