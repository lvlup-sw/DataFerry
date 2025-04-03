// ========================================================================
// <copyright file="ThreadLocalArrayPool.cs" company="Level Up Software">
// Copyright (c) Level Up Software. All rights reserved.
// </copyright>
// ========================================================================

using System.Buffers;
using System.Collections.Concurrent;
using System.Numerics;

namespace lvlup.DataFerry.Memory;

/// <summary>
/// An implementation of <see cref="ArrayPool{T}"/> utilizing a thread-local cache for single-item
/// caching per bucket and a central concurrent pool for broader sharing.
/// </summary>
/// <remarks>
/// <para>This class provides pooled arrays, attempting to reuse arrays to reduce allocation overhead.</para>
/// <para>It uses buckets sized as powers of 2.</para>
/// <para>A thread-local cache holds at most one array per bucket size for fast access by the same thread.</para>
/// <para>A central concurrent dictionary holds queues of arrays for sharing across threads.</para>
/// <para>This class is designed to be potentially injected into your application as a singleton.</para>
/// </remarks>
/// <typeparam name="T">The type of the elements in the arrays.</typeparam>
public class ThreadLocalArrayPool<T> : ArrayPool<T>
{
    /// <summary>
    /// The minimum power of 2 used for bucket sizes (2^4 = 16). Arrays smaller than this are typically cheaper to allocate directly.
    /// </summary>
    private const int MinPowOf2 = 4;

    /// <summary>
    /// The maximum power of 2 used for bucket sizes (2^30). Defines the maximum size of arrays that will be pooled.
    /// </summary>
    private const int MaxPowOf2 = 30;

    /// <summary>
    /// The central pool of arrays, keyed by bucket size (power of 2). Each key maps to a queue of available arrays of that size.
    /// </summary>
    private readonly ConcurrentDictionary<int, ConcurrentQueue<T[]>> _buckets;

    /// <summary>
    /// An array holding the calculated bucket sizes (powers of 2 from 2^MinPowOf2 to 2^MaxPowOf2).
    /// </summary>
    private readonly int[] _bucketSizes;

    /// <summary>
    /// The thread-local cache. Each thread holds its own array, where each index corresponds to a bucket size.
    /// This cache holds at most *one* returned array per bucket size for immediate reuse by the same thread.
    /// </summary>
    [ThreadStatic]
    private static T[][]? s_threadLocalCache;

    /// <summary>
    /// Initializes a new instance of the <see cref="ThreadLocalArrayPool{T}"/> class, pre-allocating arrays for efficient reuse.
    /// </summary>
    /// <param name="preallocation">The number of arrays to pre-allocate for each bucket size (default: 1). Set to 0 to disable preallocation.</param>
    public ThreadLocalArrayPool(int preallocation = 1)
    {
        _bucketSizes = Enumerable.Range(MinPowOf2, MaxPowOf2 - MinPowOf2 + 1)
            .Select(i => 1 << i)
            .ToArray();

        _buckets = new ConcurrentDictionary<int, ConcurrentQueue<T[]>>();

        // Preallocate arrays in each bucket if requested
        if (preallocation > 0)
        {
            foreach (var size in _bucketSizes)
            {
                var queue = new ConcurrentQueue<T[]>();
                for (int i = 0; i < preallocation; i++) queue.Enqueue(new T[size]);
                _buckets[size] = queue;
            }
        }
        else
        {
            // Ensure queues exist even if not reallocating
            foreach (var size in _bucketSizes)
            {
                _buckets[size] = new ConcurrentQueue<T[]>();
            }
        }
    }

    /// <summary>
    /// Rents an array from the pool with a minimum specified length.
    /// </summary>
    /// <param name="minimumLength">The minimum required length of the rented array.</param>
    /// <returns>An array from the pool with a length at least equal to <paramref name="minimumLength"/>.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if <paramref name="minimumLength"/> is greater than the maximum allowed array size (2^<see cref="MaxPowOf2"/>) or less than 0.</exception>
    public override T[] Rent(int minimumLength)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(minimumLength);

        // Check if requested size exceeds the maximum pooled size
        const int maxArrayLength = 1 << MaxPowOf2;
        if (minimumLength > maxArrayLength)
        {
            // As per ArrayPool<T> documentation, rent requests for arrays larger than max should allocate directly
             return GC.AllocateUninitializedArray<T>(minimumLength);
        }

        // Determine the bucket size (next power of 2 >= minimumLength)
        int actualBucketSize = GetBucketSize(minimumLength);
        int bucketIndex = GetBucketIndexFromSize(actualBucketSize);

        // Initialize thread-local cache if it's not already initialized for this thread
        s_threadLocalCache ??= new T[_bucketSizes.Length][];

        // Try to get from thread-local storage first
        var localArray = s_threadLocalCache[bucketIndex];

        // Check if cached array exists and is suitable
        if (localArray is not null)
        {
            s_threadLocalCache[bucketIndex] = null!;
            return localArray;
        }

        // Next, try to get an array from the central pool for the determined bucket size
        if (_buckets.TryGetValue(actualBucketSize, out var queue) && queue.TryDequeue(out var array))
        {
            return array;
        }

        // If no suitable array is found in cache or pool, allocate a new one of the appropriate bucket size
        // This ensures returned arrays match bucket sizes.
        return GC.AllocateUninitializedArray<T>(actualBucketSize);
    }

    /// <summary>
    /// Returns an array to the pool for potential reuse, optionally clearing its contents.
    /// </summary>
    /// <param name="array">The array to be returned to the pool. Must not be null.</param>
    /// <param name="clearArray">Indicates whether the array's contents should be cleared (set to default values) before returning it (default: false).</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="array"/> is null.</exception>
    public override void Return(T[] array, bool clearArray = false)
    {
        ArgumentNullException.ThrowIfNull(array);

        // Determine the bucket index based on the array's actual length
        int bucketIndex = GetBucketIndexFromLength(array.Length);

        // Discard if the array doesn't match a bucket size or is too large/small
        if (bucketIndex < 0) return;

        // Clear array contents if requested
        if (clearArray) Array.Clear(array, 0, array.Length);

        // Initialize thread-local cache if needed (e.g., Return called before Rent on a thread)
        s_threadLocalCache ??= new T[_bucketSizes.Length][];

        // Try to store in the thread-local cache *first*, replacing any existing array for that bucket.
        // This provides fast reuse for the same thread but only caches one array per size.
        if (s_threadLocalCache[bucketIndex] == null)
        {
             s_threadLocalCache[bucketIndex] = array;
             return; // Stored in local cache
        }

        // If local cache for this bucket is full (already has an array), return to the central pool.
         if (_buckets.TryGetValue(_bucketSizes[bucketIndex], out var queue))
        {
            queue.Enqueue(array);
        }
        // else: This case (queue not found) should theoretically not happen if the constructor initializes all queues.
        //       If it could, you might want to handle it, perhaps by recreating the queue, though that indicates a potential issue elsewhere.
    }

    /// <summary>
    /// Calculates the appropriate bucket size (power of 2) for a given minimum length.
    /// </summary>
    /// <param name="minimumLength">The minimum desired length.</param>
    /// <returns>The smallest power-of-2 bucket size that is >= <paramref name="minimumLength"/>.</returns>
    internal static int GetBucketSize(int minimumLength)
    {
        // Clamp minimum length to the smallest bucket size if it's smaller
        int BITS_IN_BYTE = 8;
        int size = Math.Max(1 << MinPowOf2, minimumLength);
        // Round up to the next power of 2
        // See: https://graphics.stanford.edu/~seander/bithacks.html#RoundUpPowerOf2
        size--;
        size |= size >> 1;
        size |= size >> 2;
        size |= size >> 4;
        size |= size >> 8;
        size |= size >> 16;
        size++;
        return size;

        // Alternative using BitOperations (available in .NET Core 3.0+ / .NET 5+)
        // return Math.Max(1 << MinPowOf2, (int)BitOperations.RoundUpToPowerOf2((uint)minimumLength));
    }

     /// <summary>
    /// Gets the index within the <see cref="_bucketSizes"/> and <see cref="s_threadLocalCache"/> arrays
    /// corresponding to a given *exact* bucket size (which must be a power of 2).
    /// </summary>
    /// <param name="bucketSize">The exact size of the bucket (must be a power of 2 within the valid range).</param>
    /// <returns>The bucket index, or -1 if the size is not a valid power-of-2 bucket size handled by this pool.</returns>
    internal static int GetBucketIndexFromSize(int bucketSize)
    {
        // Check if it's a power of 2 and within range
        bool isPowerOfTwo = BitOperations.IsPow2(bucketSize); // Use BitOperations helper
        bool isInRange = bucketSize >= (1 << MinPowOf2) && bucketSize <= (1 << MaxPowOf2);

        if (!isPowerOfTwo || !isInRange) return -1;

        // Calculate the index: Log2(size) - MinPowOf2
        return BitOperations.Log2((uint)bucketSize) - MinPowOf2;
    }


    /// <summary>
    /// Gets the bucket index for a given array length, ONLY if the length exactly matches a configured bucket size.
    /// </summary>
    /// <param name="length">The length of the array being returned.</param>
    /// <returns>The bucket index if the length matches a bucket size, otherwise -1.</returns>
    internal static int GetBucketIndexFromLength(int length)
    {
         // We only accept arrays back into the pool if their length *exactly* matches one of our bucket sizes.
        bool isPowerOfTwo = BitOperations.IsPow2(length);
        bool isInRange = length >= (1 << MinPowOf2) && length <= (1 << MaxPowOf2);

        if (!isPowerOfTwo || !isInRange) return -1;

        return BitOperations.Log2((uint)length) - MinPowOf2;
    }
}