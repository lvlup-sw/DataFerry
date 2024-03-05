using Microsoft.Extensions.Caching.Distributed;
using System.Collections.Concurrent;

namespace CacheProvider.Caches
{
    /// <summary>
    /// Local or Distributed cache enumerator. Used to determine which cache implementation to use.
    /// </summary>
    public enum CacheType
    {
        /// <summary>
        /// A local cache.
        /// </summary>
        /// <remarks>
        /// Implemented as a <see cref="ConcurrentDictionary{TKey, TValue}"/>.
        /// </remarks>
        Local,
        /// <summary>
        /// A distributed cache.
        /// </summary>
        /// <remarks>
        /// Implemented as an <see cref="DistributedCache"/>.
        /// </remarks>
        Distributed
    }
}
