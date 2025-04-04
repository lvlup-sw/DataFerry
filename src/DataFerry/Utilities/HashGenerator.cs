// =================================================================
// <copyright file="HashGenerator.cs" company="Level Up Software">
// Copyright (c) Level Up Software. All rights reserved.
// </copyright>
// =================================================================

using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using static System.Numerics.BitOperations;

namespace lvlup.DataFerry.Utilities;

/// <summary>
/// Provides utility methods for generating MurmurHash3 hashes for various object types.
/// This class is static and cannot be instantiated.
/// </summary>
public static class HashGenerator
{
    /// <summary>
    /// Creates a cache key by hashing the provided object into a 32-bit MurmurHash3 value,
    /// serializing it into a base64 string, and prepending a specified prefix.
    /// </summary>
    /// <typeparam name="T">The type of the object to hash.</typeparam>
    /// <param name="obj">The object to be hashed. Cannot be null.</param>
    /// <param name="prefix">A string prefix to prepend to the generated hash string (e.g., a version identifier). Defaults to "1.0.0.0".</param>
    /// <param name="seed">The seed value for the MurmurHash3 algorithm. Defaults to 0.</param>
    /// <returns>A string representing the cache key in the format "prefix:base64hash".</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="obj"/> is null.</exception>
    public static string GenerateCacheKey<T>(T obj, string prefix = "1.0.0.0", uint seed = 0)
    {
        ArgumentNullException.ThrowIfNull(obj, nameof(obj));

        // Read the object as bytes
        ReadOnlySpan<byte> bytes = ConvertToBytes(obj);

        // Return the prefix + hashed bytes converted to Base64 string
        return $"{prefix}:{Convert.ToBase64String(BitConverter.GetBytes(Hash32(ref bytes, seed)))}";
    }

    /// <summary>
    /// Generates a 32-bit MurmurHash3 hash of the provided object, returned as an <see cref="int"/>.
    /// Note: The hash is generated as an uint and then cast to int using unchecked conversion.
    /// </summary>
    /// <typeparam name="T">The type of the object to hash.</typeparam>
    /// <param name="obj">The object to be hashed. Cannot be null.</param>
    /// <param name="seed">The seed value for the MurmurHash3 algorithm. Defaults to 0.</param>
    /// <returns>An <see cref="int"/> representing the 32-bit MurmurHash3 hash of the object.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="obj"/> is null.</exception>
    public static int GenerateHash<T>(T obj, uint seed = 0)
    {
        ArgumentNullException.ThrowIfNull(obj, nameof(obj));

        // Read the object as bytes
        ReadOnlySpan<byte> bytes = ConvertToBytes(obj);

        // Generate the hash
        return unchecked((int)Hash32(ref bytes, seed));
    }

    /// <summary>
    /// Converts the input object into a read-only span of bytes for hashing.
    /// Handles common primitive types, strings, byte arrays, and uses JSON serialization as a fallback.
    /// </summary>
    /// <typeparam name="T">The type of the object to convert.</typeparam>
    /// <param name="obj">The object to convert to bytes.</param>
    /// <returns>A <see cref="ReadOnlySpan{T}"/> containing the byte representation of the object.</returns>
    private static ReadOnlySpan<byte> ConvertToBytes<T>(T obj)
    {
        return obj switch
        {
            byte[] byteArray => byteArray,
            string str => Encoding.UTF8.GetBytes(str),
            int i => BitConverter.GetBytes(i),
            long l => BitConverter.GetBytes(l),
            float f => BitConverter.GetBytes(f),
            double d => BitConverter.GetBytes(d),
            decimal d => decimal.GetBits(d).SelectMany(BitConverter.GetBytes).ToArray(),
            _ => JsonSerializer.SerializeToUtf8Bytes(obj)
        };
    }

    /// <summary>
    /// Computes the 32-bit MurmurHash3 hash for the given sequence of bytes.
    /// This is an internal implementation detail based on the MurmurHash3 algorithm.
    /// It uses unsafe code and direct memory manipulation for performance.
    /// </summary>
    /// <param name="bytes">The read-only span of bytes to hash.</param>
    /// <param name="seed">The seed value for the hash algorithm.</param>
    /// <returns>A <see cref="uint"/> representing the 32-bit MurmurHash3 hash.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static uint Hash32(ref ReadOnlySpan<byte> bytes, uint seed)
    {
        // Return invalid bytes
        if (bytes.Length == 0) return seed ^ 0;

        // Constants for hash calc referencing MurmurHash3
        const uint a1 = 430675100;
        const uint a2 = 2048144789;
        const uint a3 = 1028477387;
        const uint c1 = 3432918353;
        const uint c2 = 461845907;

        // Setup references to first byte in span and end point
        ref byte bp = ref MemoryMarshal.GetReference(bytes);
        ref uint endPoint = ref Unsafe.Add(ref Unsafe.As<byte, uint>(ref bp), bytes.Length >> 2);

        // Process 4 bytes per iteration until end of span
        while (Unsafe.IsAddressLessThan(ref Unsafe.As<byte, uint>(ref bp), ref endPoint))
        {
            // Assign next 4 bytes
            var data = Unsafe.ReadUnaligned<uint>(ref bp);

            // Apply mm3 mixing function
            seed = (RotateLeft(seed ^ RotateLeft(data * c1, 15) * c2, 13) * 5) - a1;

            // Move pointer to next 4 bytes
            bp = ref Unsafe.Add(ref bp, 4);
        }

        // Handle remaining bytes (<3)
        uint num = endPoint;
        if ((bytes.Length & 2) != 0)
            num ^= Unsafe.Add(ref endPoint, 1) << 8;
        if ((bytes.Length & 1) != 0)
            num ^= Unsafe.Add(ref endPoint, 2) << 16;
        seed ^= RotateLeft(num * c1, 15) * c2;

        // Final mixing and return
        seed ^= (uint)bytes.Length;
        seed = (uint)((seed ^ seed >> 16) * -a2);
        seed = (uint)((seed ^ seed >> 13) * -a3);
        return seed ^ seed >> 16;
    }
}