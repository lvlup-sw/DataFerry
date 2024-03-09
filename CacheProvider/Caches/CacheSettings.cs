﻿using CacheProvider.Providers;

namespace CacheProvider.Caches
{
    /// <summary>
    /// Cache settings for <see cref="LocalCache"/> and <see cref="DistributedCache"/>.
    /// </summary>
    /// <remarks>
    /// You need to pass an instance of this class to the <see cref="CacheProvider{T}"/>.
    /// </remarks>
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
        /// Set to true to use exponential backoff for cache operation retries.
        /// </summary>
        public bool UseExponentialBackoff { get; set; }

        /// <summary>
        /// Retrieves or sets the expiration of the cache in minutes.
        /// </summary>
        public int AbsoluteExpiration { get; set; }

        /// <summary>
        /// Connection String used to connect to the Distributed Cache (ex Elasticache, Redis, etc).
        /// </summary>
        public string? ConnectionString { get; set; }
    }
}
