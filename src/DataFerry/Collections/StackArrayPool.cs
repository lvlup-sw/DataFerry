using System.Buffers;
using System.Collections.Concurrent;
using System.Numerics;

namespace DataFerry.Collections
{
    /// <summary>
    /// An implementation of <see cref="ArrayPool{T}"/> utilizing a thread-local cache.
    /// Resource eviction is handled by <see cref="FastMemCache{TKey, TValue}"/> behind the scenes.
    /// </summary>
    /// <remarks>This class is designed to be injected into your application as a singleton using <see cref="ArrayPool{T}.Shared"/>.</remarks>
    /// <typeparam name="T"></typeparam>
    public class StackArrayPool<T> : ArrayPool<T>
    {
        // Any array less than 2^4 is cheaper to create than rent
        // We approach max length at 2^30, thus it forms our outer bound
        private const int MinPowOf2 = 4;
        private const int MaxPowOf2 = 30;

        // This is our central pool of arrays
        private readonly FastMemCache<int, ConcurrentQueue<T[]>> _buckets;
        private readonly int[] _bucketSizes;

        // This is our local cache on each thread
        // ThreadStatic ensures that each thread has its own independent copy of the local cache
        // Therefore, we don't need to implement any explicit locking mechanism to handle contention
        [ThreadStatic]
        private static T[][]? threadLocalCache;

        // Time-to-live; tracked by FastMemCache
        public TimeSpan ArrayTTL { get; set; } = TimeSpan.FromMinutes(5);

        /// <summary>
        /// Initializes a new instance of the `StackArrayPool` class, pre-allocating arrays for efficient reuse.
        /// </summary>
        /// <param name="preallocation">The number of arrays to pre-allocate for each bucket size (default: 1).</param>
        public StackArrayPool(int preallocation = 1)
        {
            _bucketSizes = Enumerable.Range(MinPowOf2, MaxPowOf2 - MinPowOf2 + 1)
                .Select(i => (int)Math.Pow(2, i))
                .ToArray();

            _buckets = new();

            // Preallocate arrays in each bucket
            for (int i = 0; i < _bucketSizes.Length; i++)
            {
                var preallocatedArrays = Enumerable.Range(0, preallocation)
                    .Select(_ => new T[_bucketSizes[i]]);

                _buckets.AddOrUpdate(
                    _bucketSizes[i], 
                    new ConcurrentQueue<T[]>(preallocatedArrays), 
                    ArrayTTL
                );
            }
        }

        /// <summary>
        /// Rents an array from the pool with a minimum specified length.
        /// </summary>
        /// <param name="minimumLength">The minimum required length of the rented array.</param>
        /// <returns>An array from the pool with a length at least equal to `minimumLength`.</returns>
        public override T[] Rent(int minimumLength)
        {
            if (minimumLength > (1 << MaxPowOf2))
            {
                throw new ArgumentOutOfRangeException($"Requested array size out of range: {minimumLength}");
            }

            // Check if capacity of request is within bounds
            var bucketIndex = StackArrayPool<T>.GetBucketIndex(minimumLength);
            if (bucketIndex < 0 || bucketIndex >= _bucketSizes.Length)
            {
                // Return new if out of bounds
                return new T[minimumLength];
            }

            // Initialize thread-local cache if it's not already initialized
            threadLocalCache ??= new T[_bucketSizes.Length][];

            // Try to get from thread-local storage first
            var localArray = threadLocalCache[bucketIndex];
            if (localArray is not null && localArray.Length >= minimumLength)
            {
                threadLocalCache[bucketIndex] = null!;
                return localArray;
            }

            // Next, try to get an array from the central pool
            if (_buckets.TryGet(_bucketSizes[bucketIndex], out var queue) && queue.TryDequeue(out var array))
            {
                return array;
            }

            // If no suitable array is found, allocate a new one
            return new T[_bucketSizes[bucketIndex]];
        }

        /// <summary>
        /// Returns an array to the pool for potential reuse, optionally clearing its contents.
        /// </summary>
        /// <param name="array">The array to be returned to the pool.</param>
        /// <param name="clearArray">Indicates whether the array's contents should be replaced with default values before returning it (false by default).</param>
        public override void Return(T[] array, bool clearArray = false)
        {
            ArgumentNullException.ThrowIfNull(array);

            if (clearArray)
            {   // Replaces array contents with default values
                Array.Clear(array, 0, array.Length);
            }

            // Check if capacity of array is within bounds
            var bucketIndex = StackArrayPool<T>.GetBucketIndex(array.Length);
            if (bucketIndex < 0 || bucketIndex >= _bucketSizes.Length)
            {   // Discard if out of bounds
                return;
            }

            // Try to store in thread-local storage first
            if (threadLocalCache is not null && threadLocalCache[bucketIndex] is null)
            {
                threadLocalCache[bucketIndex] = array;
                return;
            }

            // Otherwise, add to the central pool
            _buckets.AddOrUpdate(
                _bucketSizes[bucketIndex],
                new ConcurrentQueue<T[]>([array]),
                ArrayTTL
            );
        }

        // Since our sizing follows an exponential scale, we can calculate this based on Log2
        internal static int GetBucketIndex(int length)
        {
            // Ensure length is within the valid range and is a power of 2
            // Note we're checking the binary representation here
            bool isValidLength = (length & (length - 1)) == 0 
                && length >= (1 << MinPowOf2) 
                && length <= (1 << MaxPowOf2);

            if (!isValidLength) return -1;

            // Calculate the bucket index 
            int bucketIndex = BitOperations.Log2((uint)length) - MinPowOf2;

            return bucketIndex;
        }
    }
}