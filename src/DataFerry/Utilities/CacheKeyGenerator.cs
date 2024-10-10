using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using static System.Numerics.BitOperations;

namespace DataFerry.Utilities
{
    public static class CacheKeyGenerator
    {
        /// <summary>
        /// Creates a cache key by hashing <paramref name="obj"/> into a MurmurHash3 represented as <see cref="uint"/>.
        /// <paramref name="prefix"/> is prepended onto the hash.
        /// </summary>
        /// <param name="obj">The object to be hashed.</param>
        /// <param name="prefix">The prefix to be prepended.</param>
        /// <param name="seed">The seed for this algorithm.</param>
        /// <returns><see cref="string"/></returns>
        public static string GenerateCacheKey<T>(T obj, string prefix = "1.0.0.0", uint seed = 0)
        {
            // Serialize the object
            ReadOnlySpan<byte> serializedObj = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(obj)).AsSpan();

            // Generate the hash
            var hash = Hash32(ref serializedObj, seed);

            // Construct the cache key
            return $"{prefix}:{hash}";
        }

        /// <summary>
        /// Creates a MurmurHash3 of <paramref name="obj"/> represented as <see cref="uint"/>.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="obj"></param>
        /// <param name="seed"></param>
        /// <returns><see cref="uint"/></returns>
        public static uint GenerateHash<T>(T obj, uint seed = 0)
        {
            // Serialize the object
            ReadOnlySpan<byte> serializedObj = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(obj)).AsSpan();

            // Generate the hash
            return Hash32(ref serializedObj, seed);
        }

        /// <summary>
        /// Hashes the <paramref name="bytes"/> into a MurmurHash3 as a <see cref="uint"/>.
        /// </summary>
        /// <param name="bytes">The span.</param>
        /// <param name="seed">The seed for this algorithm.</param>
        /// <returns><see cref="uint"/></returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static uint Hash32(ref ReadOnlySpan<byte> bytes, uint seed)
        {
            // Return invalid bytes
            if (bytes.Length == 0) return seed ^= 0;

            // Constants for hash calc
            // referencing MurmurHash3
            const uint A1 = 430675100;
            const uint A2 = 2048144789;
            const uint A3 = 1028477387;
            const uint C1 = 3432918353;
            const uint C2 = 461845907;

            // Setup references to first byte in span and end point
            ref byte bp = ref MemoryMarshal.GetReference(bytes);
            ref uint endPoint = ref Unsafe.Add(ref Unsafe.As<byte, uint>(ref bp), bytes.Length >> 2);

            // Process 4 bytes per iteration until end of span
            while (Unsafe.IsAddressLessThan(ref Unsafe.As<byte, uint>(ref bp), ref endPoint))
            {
                // Assign next 4 bytes
                var data = Unsafe.ReadUnaligned<uint>(ref bp);

                // Apply mm3 mixing function
                seed = RotateLeft(seed ^ RotateLeft(data * C1, 15) * C2, 13) * 5 - A1;

                // Move pointer to next 4 bytes
                bp = ref Unsafe.Add(ref bp, 4);
            }

            // Handle remaining bytes (<3)
            uint num = endPoint;
            if ((bytes.Length & 2) != 0)
                num ^= Unsafe.Add(ref endPoint, 1) << 8;
            if ((bytes.Length & 1) != 0)
                num ^= Unsafe.Add(ref endPoint, 2) << 16;
            seed ^= RotateLeft(num * C1, 15) * C2;

            // Final mixing and return
            seed ^= (uint)bytes.Length;
            seed = (uint)((seed ^ seed >> 16) * -A2);
            seed = (uint)((seed ^ seed >> 13) * -A3);
            return seed ^ seed >> 16;
        }
    }
}
