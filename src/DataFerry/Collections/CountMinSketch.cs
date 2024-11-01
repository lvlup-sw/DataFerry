using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.Intrinsics.X86;
using System.Runtime.Intrinsics;

namespace lvlup.DataFerry.Collections
{
    /// <summary>
    /// A probabilistic data structure that provides an approximate count for the frequency of an item in a stream.
    /// </summary>
    /// <remarks>Heavily based on the implementations by the Caffeine and BitFaster libraries.</remarks>
    /// <typeparam name="T">The type of the items to be counted.</typeparam>
    public class CountMinSketch<T> where T : notnull
    {
        // Bit masks
        private const long ResetMask = 0x7777777777777777L;
        private const long OneMask = 0x1111111111111111L;

        // Backing fields
        private long[] table;
        private int sampleSize;
        private int blockMask;
        private int size;

        // Hashing function
        private readonly Func<T, int> hashFunction;

        /// <summary>
        /// Initializes a new instance of the <see cref="CountMinSketch{T}"/> class with the specified maximum size.
        /// </summary>
        /// <param name="maximumSize">The maximum size of the sketch.</param>
        public CountMinSketch(int maximumSize)
        {
            EnsureCapacity(maximumSize);

            if (typeof(T).IsPrimitive || typeof(T) == typeof(string))
            {
                // For primitive types and string, use the default .NET GetHashCode
                hashFunction = obj => obj.GetHashCode();
            }
            else
            {
                // For other types, use MurmurHash3
                // This is slower than the default hash function
                // but has a better distribution of values
                hashFunction = obj => HashGenerator.GenerateHash(obj);
            }
        }

        /// <summary>
        /// Gets the size of the sketch after the last reset.
        /// </summary>
        public int ResetSampleSize => sampleSize;

        /// <summary>
        /// Gets the current size of the sketch.
        /// </summary>
        public int Size => size;

        /// <summary>
        /// Estimates the frequency of the specified item in the sketch.
        /// </summary>
        /// <remarks>Utilizes <see cref="Avx2"/> instructions; only compatible with Netstandard 2.1 or newer.</remarks>
        /// <param name="value">The item to estimate the frequency of.</param>
        /// <returns>The estimated frequency of the item.</returns>
        public unsafe int EstimateFrequency(T value)
        {
            // Calculate block and counter hashes
            int blockHash = Spread(hashFunction(value));
            int counterHash = Rehash(blockHash);

            // Calculate block index
            int block = (blockHash & blockMask) << 3;

            // Create a vector from the counter hash and shift each element by 8 bits
            Vector128<int> counterHashVector = Vector128.Create(counterHash);
            counterHashVector = Avx2.ShiftRightLogicalVariable(counterHashVector.AsUInt32(), Vector128.Create(0U, 8U, 16U, 24U)).AsInt32();

            // Calculate index and offset vectors
            Vector128<int> index = Sse2.ShiftRightLogical(counterHashVector, 1);
            index = Sse2.And(index, Vector128.Create(15));
            Vector128<int> offset = Sse2.And(counterHashVector, Vector128.Create(1));
            Vector128<int> blockOffset = Sse2.Add(Vector128.Create(block), offset);
            blockOffset = Sse2.Add(blockOffset, Vector128.Create(0, 2, 4, 6));

            // Gather elements from the table into a vector using the block offset
            fixed (long* tablePtr = table)
            {
                Vector256<long> tableVector = Avx2.GatherVector256(tablePtr, blockOffset, 8);

                // Shift the table vector elements by the index
                index = Sse2.ShiftLeftLogical(index, 2);
                Vector256<long> indexLong = Vector256.Create(index, Vector128<int>.Zero).AsInt64();
                Vector256<int> permuteMask2 = Vector256.Create(0, 4, 1, 5, 2, 5, 3, 7);
                indexLong = Avx2.PermuteVar8x32(indexLong.AsInt32(), permuteMask2).AsInt64();
                tableVector = Avx2.ShiftRightLogicalVariable(tableVector, indexLong.AsUInt64());
                tableVector = Avx2.And(tableVector, Vector256.Create(0xfL));

                // Permute the table vector and get the lower half
                Vector256<int> permuteMask = Vector256.Create(0, 2, 4, 6, 1, 3, 5, 7);
                Vector128<ushort> count = Avx2.PermuteVar8x32(tableVector.AsInt32(), permuteMask).GetLower().AsUInt16();

                // Set the zeroed high parts of the long value to ushort.Max
                count = Sse41.Blend(count, Vector128.Create(ushort.MaxValue), 0b10101010);

                // Return the minimum value in the count vector
                return Sse41.MinHorizontal(count).GetElement(0);
            }
        }

        /// <summary>
        /// Increments the count of the specified item in the sketch.
        /// </summary>
        /// <remarks>Utilizes <see cref="Avx2"/> instructions; only compatible with Netstandard 2.1 or newer.</remarks>
        /// <param name="value">The item to increment the count of.</param>
        public unsafe void Increment(T value)
        {
            // Calculate block and counter hashes
            int blockHash = Spread(hashFunction(value));
            int counterHash = Rehash(blockHash);

            // Calculate block index
            int block = (blockHash & blockMask) << 3;

            // Create a vector from the counter hash and shift each element by 8 bits
            Vector128<int> counterHashVector = Vector128.Create(counterHash);
            counterHashVector = Avx2.ShiftRightLogicalVariable(counterHashVector.AsUInt32(), Vector128.Create(0U, 8U, 16U, 24U)).AsInt32();

            // Calculate index and offset vectors
            Vector128<int> index = Sse2.ShiftRightLogical(counterHashVector, 1);
            index = Sse2.And(index, Vector128.Create(15));
            Vector128<int> offset = Sse2.And(counterHashVector, Vector128.Create(1));
            Vector128<int> blockOffset = Sse2.Add(Vector128.Create(block), offset);
            blockOffset = Sse2.Add(blockOffset, Vector128.Create(0, 2, 4, 6));

            // Gather elements from the table into a vector using the block offset
            fixed (long* tablePtr = table)
            {
                Vector256<long> tableVector = Avx2.GatherVector256(tablePtr, blockOffset, 8);

                // Shift the index left by 2 bits to prepare for the mask calculation
                index = Sse2.ShiftLeftLogical(index, 2);

                // Convert index from int to long via permute
                Vector256<long> offsetLong = Vector256.Create(index, Vector128<int>.Zero).AsInt64();
                Vector256<int> permuteMask = Vector256.Create(0, 4, 1, 5, 2, 5, 3, 7);
                offsetLong = Avx2.PermuteVar8x32(offsetLong.AsInt32(), permuteMask).AsInt64();

                // Calculate the mask
                Vector256<long> mask = Avx2.ShiftLeftLogicalVariable(Vector256.Create(0xfL), offsetLong.AsUInt64());

                // Calculate the masked elements of the table vector
                Vector256<long> masked = Avx2.CompareEqual(Avx2.And(tableVector, mask), mask);

                // Calculate the increment vector
                Vector256<long> inc = Avx2.ShiftLeftLogicalVariable(Vector256.Create(1L), offsetLong.AsUInt64());

                // Zero out the elements of the increment vector that should not be incremented
                inc = Avx2.AndNot(masked, inc);

                // Check if any elements were incremented
                Vector256<byte> result = Avx2.CompareEqual(masked.AsByte(), Vector256<byte>.Zero);
                bool wasInc = Avx2.MoveMask(result.AsByte()) == unchecked((int)0b1111_1111_1111_1111_1111_1111_1111_1111);

                // Increment the elements of the table
                tablePtr[blockOffset.GetElement(0)] += inc.GetElement(0);
                tablePtr[blockOffset.GetElement(1)] += inc.GetElement(1);
                tablePtr[blockOffset.GetElement(2)] += inc.GetElement(2);
                tablePtr[blockOffset.GetElement(3)] += inc.GetElement(3);

                // If the size has reached the sample size, reset the sketch
                if (wasInc && ++size == sampleSize)
                {
                    Reset();
                }
            }
        }

        /// <summary>
        /// Clears the sketch, resetting all counts to zero.
        /// </summary>
        public void Clear()
        {
            table = new long[table.Length];
            size = 0;
        }

        /// <summary>
        /// Ensures that the capacity of the sketch is at least the specified value.
        /// </summary>
        /// <param name="maximumSize">The minimum capacity of the sketch.</param>
        [MemberNotNull(nameof(table))]
        private void EnsureCapacity(int maximumSize)
        {
            // Calculate the maximum size, ensuring it doesn't exceed int.MaxValue / 2
            int maximum = Math.Min(maximumSize, int.MaxValue >> 1);

            // Create a new table with a size that is a power of 2 and at least 8
            table = new long[Math.Max(BitOperations.Log2((uint)maximum), 8)];

            // Calculate the block mask and sample size
            blockMask = (int)((uint)table.Length >> 3) - 1;
            sampleSize = maximumSize == 0 ? 10 : 10 * maximum;

            // Reset the size
            size = 0;
        }

        /// <summary>
        /// Rehashes the specified value using a simple mixing function.
        /// </summary>
        /// <param name="x">The value to rehash.</param>
        /// <returns>The rehashed value.</returns>
        private static int Rehash(int x)
        {
            // Multiply by a constant, then right shift and XOR to mix the bits
            x = x * 0x31848bab;
            x ^= (int)((uint)x >> 14);
            return x;
        }

        /// <summary>
        /// Spreads the bits of the specified value to ensure a good distribution.
        /// </summary>
        /// <param name="x">The value to spread.</param>
        /// <returns>The value with its bits spread.</returns>
        private static int Spread(int x)
        {
            // Right shift and XOR, multiply by a constant, then repeat to mix the bits
            x ^= (int)((uint)x >> 17);
            x = (int)(x * 0xed5ad4bb);
            x ^= (int)((uint)x >> 11);
            x = (int)(x * 0xac4c1b51);
            x ^= (int)((uint)x >> 15);
            return x;
        }

        /// <summary>
        /// Resets the sketch, halving the count of each item.
        /// </summary>
        private void Reset()
        {
            // Initialize counters for each group of 4 elements
            int count0 = 0;
            int count1 = 0;
            int count2 = 0;
            int count3 = 0;

            // For each group of 4 elements in the table
            for (int i = 0; i < table.Length; i += 4)
            {
                // Count the set bits in each element and add to the corresponding counter
                count0 += BitOperations.PopCount((ulong)(table[i] & OneMask));
                count1 += BitOperations.PopCount((ulong)(table[i + 1] & OneMask));
                count2 += BitOperations.PopCount((ulong)(table[i + 2] & OneMask));
                count3 += BitOperations.PopCount((ulong)(table[i + 3] & OneMask));

                // Right shift each element and apply the reset mask
                table[i] = (long)((ulong)table[i] >> 1) & ResetMask;
                table[i + 1] = (long)((ulong)table[i + 1] >> 1) & ResetMask;
                table[i + 2] = (long)((ulong)table[i + 2] >> 1) & ResetMask;
                table[i + 3] = (long)((ulong)table[i + 3] >> 1) & ResetMask;
            }

            // Add up the counters
            count0 = count0 + count1 + count2 + count3;

            // Update the size
            size = size - (count0 >> 2) >> 1;
        }
    }
}