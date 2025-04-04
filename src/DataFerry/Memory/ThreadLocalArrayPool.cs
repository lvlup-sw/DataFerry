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
            // Rent requests for arrays larger than max should allocate directly
            return GC.AllocateUninitializedArray<T>(minimumLength);
        }

        // Determine the bucket size (next power of 2 >= minimumLength)
        int actualBucketSize = GetBucketSize(minimumLength);
        int bucketIndex = GetBucketIndex(actualBucketSize);

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

        // If no suitable array is found in cache or pool, allocate a new one
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

        int bucketIndex = GetBucketIndex(array.Length);

        // Discard if the array doesn't match a bucket size or is too large/small
        if (bucketIndex < 0) return;

        // Clear array contents if requested
        if (clearArray) Array.Clear(array, 0, array.Length);

        // Initialize thread-local cache if needed (e.g., Return called before Rent on a thread)
        s_threadLocalCache ??= new T[_bucketSizes.Length][];

        // Try to store in the thread-local cache *first*
        // This provides fast reuse for the same thread
        if (s_threadLocalCache[bucketIndex] is null)
        {
             s_threadLocalCache[bucketIndex] = array;
             return;
        }

        // If local cache for this bucket is full, return to the central pool
        if (_buckets.TryGetValue(_bucketSizes[bucketIndex], out var queue))
        {
            queue.Enqueue(array);
        }
    }

    /// <summary>
    /// Calculates the appropriate bucket size (power of 2) for a given minimum length.
    /// </summary>
    /// <param name="minimumLength">The minimum desired length.</param>
    /// <returns>The smallest power-of-2 bucket size that is >= <paramref name="minimumLength"/> and >= the minimum pool size.</returns>
    internal static int GetBucketSize(int minimumLength)
    {
        // Clamp minimum length to the smallest bucket size (e.g., 1 << MinPowOf2) if it's smaller
        // Also handles potential negative input gracefully by ensuring a positive result before rounding
        int size = Math.Max(1 << MinPowOf2, minimumLength);

        // Round up to the next power of 2 using BitOperations
        // Handles the case where size is already a power of 2 correctly
        size = (int)BitOperations.RoundUpToPowerOf2((uint)size);

        return size;
    }

    /// <summary>
    /// Gets the bucket index for a given size, ONLY if the size exactly matches a configured power-of-2 bucket size within the pool's range.
    /// </summary>
    /// <remarks>
    /// This method is used both to find the index for a calculated target bucket size (e.g., in Rent)
    /// and to validate the length of an array being returned to the pool (e.g., in Return).
    /// </remarks>
    /// <param name="size">The size (e.g., a calculated bucket size or an array length) to check.</param>
    /// <returns>The bucket index if the size represents a valid, configured bucket size for this pool; otherwise, -1.</returns>
    internal static int GetBucketIndex(int size)
    {
        // Check if the size is an exact power of 2
        bool isPowerOfTwo = BitOperations.IsPow2(size);

        // Check if the size is within the configured range of bucket sizes for this pool
        bool isInRange = size is >= 1 << MinPowOf2 and <= 1 << MaxPowOf2;

        // The size is valid only if it's a power of two AND within the pool's range
        if (!isPowerOfTwo || !isInRange) return -1;

        // Calculate the index based on the power of 2
        return BitOperations.Log2((uint)size) - MinPowOf2;
    }
}