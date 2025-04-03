// ======================================================================
// <copyright file="PooledBufferWriter.cs" company="Level Up Software">
// Copyright (c) Level Up Software. All rights reserved.
// </copyright>
// ======================================================================

using System.Buffers;
using System.Numerics; // Required for BitOperations
using System.Runtime.CompilerServices;

namespace lvlup.DataFerry.Memory;

/// <summary>
/// Defines the buffer growth strategy for the PooledBufferWriter.
/// </summary>
public enum GrowthStrategy
{
    /// <summary>
    /// Grows the buffer linearly, typically by doubling the current size or adding the size hint, whichever is larger.
    /// </summary>
    Linear,

    /// <summary>
    /// Grows the buffer by doubling. If the required size exceeds a large threshold (1MB),
    /// it rounds the required size up to the nearest power of two to potentially reduce frequent large allocations.
    /// </summary>
    PowerOfTwo
}

/// <summary>
/// Provides configuration options for the <see cref="PooledBufferWriter{T}"/>.
/// </summary>
public class PooledBufferWriterOptions
{
    /// <summary>
    /// Gets or sets the initial capacity with which to initialize the underlying buffer.
    /// Defaults to 0, causing the buffer to be rented on the first write.
    /// </summary>
    public int InitialCapacity { get; set; } = 0;

    /// <summary>
    /// Gets or sets a value indicating whether the buffer should be cleared when returned to the pool.
    /// Defaults to true (safer). Setting to false may improve performance but requires caution with reference types.
    /// </summary>
    public bool ClearOnReturn { get; set; } = true;

    /// <summary>
    /// Gets or sets the strategy used for growing the internal buffer when more space is needed.
    /// Defaults to <see cref="GrowthStrategy.Linear"/>.
    /// </summary>
    public GrowthStrategy Strategy { get; set; } = GrowthStrategy.Linear;
}


/// <summary>
/// Represents a configurable buffer writer that rents arrays from an <see cref="ArrayPool{T}"/> to minimize new allocations.
/// </summary>
/// <remarks>This class implements <see cref="IBufferWriter{T}"/> and <see cref="IDisposable"/>.</remarks>
/// <typeparam name="T">The type of elements in the buffer.</typeparam>
public sealed class PooledBufferWriter<T> : IBufferWriter<T>, IDisposable
{
    private const int DefaultInitialBufferSize = 256;
    private const int PowerOfTwoThreshold = 1024 * 1024; // Threshold for power-of-two growth

    private readonly ArrayPool<T> _pool;
    private readonly bool _clearOnReturn;
    private readonly GrowthStrategy _growthStrategy;

    private T[] _buffer;
    private int _index;

    /// <summary>
    /// Initializes a new instance of the <see cref="PooledBufferWriter{T}"/> class using the shared <see cref="ArrayPool{T}"/>.
    /// </summary>
    /// <param name="options">Configuration options for the writer.</param>
    public PooledBufferWriter(PooledBufferWriterOptions? options = null)
        : this(ArrayPool<T>.Shared, options)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PooledBufferWriter{T}"/> class.
    /// </summary>
    /// <param name="pool">The <see cref="ArrayPool{T}"/> to use for renting buffers.</param>
    /// <param name="options">Configuration options for the writer.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="pool"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if <see cref="PooledBufferWriterOptions.InitialCapacity"/> is negative.</exception>
    public PooledBufferWriter(ArrayPool<T> pool, PooledBufferWriterOptions? options = null)
    {
        options ??= new PooledBufferWriterOptions(); // Use defaults if null provided

        ArgumentNullException.ThrowIfNull(pool);
        ArgumentOutOfRangeException.ThrowIfNegative(options.InitialCapacity);

        _pool = pool;
        _clearOnReturn = options.ClearOnReturn;
        _growthStrategy = options.Strategy;

        // Rent initial buffer if capacity is specified, otherwise start empty
        _buffer = options.InitialCapacity > 0
            ? _pool.Rent(options.InitialCapacity)
            : Array.Empty<T>();
        _index = 0;
    }

    /// <summary>
    /// Gets the <see cref="ArrayPool{T}"/> used by this writer.
    /// </summary>
    public ArrayPool<T> Pool => _pool;

    /// <summary>
    /// Gets the amount of data written to the underlying buffer so far.
    /// </summary>
    public int WrittenCount => _index;

    /// <summary>
    /// Gets the total capacity of the underlying buffer.
    /// </summary>
    public int Capacity => _buffer.Length;

    /// <summary>
    /// Gets the amount of free space available in the underlying buffer.
    /// </summary>
    public int FreeCapacity => _buffer.Length - _index;

    /// <summary>
    /// Gets a <see cref="ReadOnlyMemory{T}"/> representing the data written to the buffer so far.
    /// </summary>
    public ReadOnlyMemory<T> WrittenMemory => _buffer.AsMemory(0, _index);

    /// <summary>
    /// Gets a <see cref="ReadOnlySpan{T}"/> representing the data written to the buffer so far.
    /// </summary>
    public ReadOnlySpan<T> WrittenSpan => _buffer.AsSpan(0, _index);

    /// <summary>
    /// Creates a new array and copies the contents of the buffer to it.
    /// </summary>
    /// <returns>A new array containing the contents of the buffer.</returns>
    public T[] ToArray() => WrittenSpan.ToArray();

    /// <summary>
    /// Advances the current write position in the buffer.
    /// </summary>
    /// <param name="count">The number of elements to advance.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="count"/> is negative.</exception>
    /// <exception cref="ArgumentException">Advancing by <paramref name="count"/> would exceed the buffer's capacity.</exception>
    public void Advance(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count, nameof(count));

        if (_index > _buffer.Length - count)
        {
            ThrowArgumentExceptionForAdvancedTooFar();
        }

        _index += count;
    }

    /// <summary>
    /// Returns a <see cref="Memory{T}"/> representing a contiguous region of memory that can be written to.
    /// </summary>
    /// <param name="sizeHint">The minimum size of the returned memory. If the value is 0 or less, a non-empty memory is returned.</param>
    /// <returns>A <see cref="Memory{T}"/> representing a contiguous region of memory that can be written to.</returns>
    /// <exception cref="ArgumentOutOfRangeException">If <paramref name="sizeHint"/> causes the requested buffer size to exceed <see cref="Array.MaxLength"/>.</exception>
    public Memory<T> GetMemory(int sizeHint = 0)
    {
        CheckAndResizeBuffer(sizeHint);
        return _buffer.AsMemory(_index);
    }

    /// <summary>
    /// Returns a <see cref="Span{T}"/> representing a contiguous region of memory that can be written to.
    /// </summary>
    /// <param name="sizeHint">The minimum size of the returned span. If the value is 0 or less, a non-empty span is returned.</param>
    /// <returns>A <see cref="Span{T}"/> representing a contiguous region of memory that can be written to.</returns>
    /// <exception cref="ArgumentOutOfRangeException">If <paramref name="sizeHint"/> causes the requested buffer size to exceed <see cref="Array.MaxLength"/>.</exception>
    public Span<T> GetSpan(int sizeHint = 0)
    {
        CheckAndResizeBuffer(sizeHint);
        return _buffer.AsSpan(_index);
    }

    /// <summary>
    /// Clears the data written to the underlying buffer.
    /// </summary>
    /// <remarks>
    /// You must clear the <see cref="PooledBufferWriter{T}"/> before trying to re-use it.
    /// </remarks>
    public void Clear()
    {
        _buffer.AsSpan(0, _index).Clear();
        _index = 0;
    }

    /// <summary>
    /// Returns the internal buffer to the pool and resets the writer state.
    /// </summary>
    /// <remarks>
    /// The buffer is returned to the pool. This instance should not be used after disposal.
    /// </remarks>
    public void Dispose()
    {
        if (_buffer.Length > 0)
        {
            // Return the buffer to the pool, applying the clear setting
            ReturnBuffer(_buffer);
        }

        // Mark as disposed by replacing buffer with empty and resetting index
        _buffer = Array.Empty<T>();
        _index = -1; // Indicate disposed state if accessed again
    }

    /// <summary>
    /// Ensures that the buffer has enough capacity to accommodate the specified size hint.
    /// If necessary, the buffer is resized by renting a new array from the pool and copying the existing data.
    /// </summary>
    /// <param name="sizeHint">The minimum number of available elements the buffer should have after the call.</param>
    /// <exception cref="ArgumentOutOfRangeException">If <paramref name="sizeHint"/> causes the requested buffer size to exceed <see cref="Array.MaxLength"/>.</exception>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void CheckAndResizeBuffer(int sizeHint)
    {
        // Ensure we are not disposed
        ObjectDisposedException.ThrowIf(_index == -1, this);
        // Allow 0 size hint, but ensure we have at least 1 spot if capacity is 0.
        if (sizeHint <= 0) sizeHint = 1;

        if (sizeHint > FreeCapacity)
        {
            GrowBuffer(sizeHint);
        }
    }

    /// <summary>
    /// Selects the appropriate growth strategy and resizes the buffer.
    /// </summary>
    /// <param name="sizeHint">The minimum additional capacity required beyond the current index.</param>
    private void GrowBuffer(int sizeHint)
    {
        int minimumRequired = WrittenCount + sizeHint;

        // Calculate the new size based on the selected strategy
        int newSize = _growthStrategy switch
        {
            GrowthStrategy.Linear => CalculateLinearGrowthSize(minimumRequired, sizeHint),
            GrowthStrategy.PowerOfTwo => CalculatePowerOfTwoGrowthSize(minimumRequired),
            _ => throw new ArgumentOutOfRangeException(nameof(_growthStrategy), "Invalid growth strategy specified.") // Should not happen
        };

        // Clamp to Array.MaxLength
        if ((uint)newSize > Array.MaxLength)
        {
             // Check if the absolute minimum required size already exceeds the max length
            ArgumentOutOfRangeException.ThrowIfGreaterThan((uint)minimumRequired, (uint)Array.MaxLength, "Requested buffer size exceeds Array.MaxLength.");
            newSize = Array.MaxLength; // Clamp to max if possible
        }

        // Rent new buffer, copy data, and return the old one
        ResizeAndCopy(newSize);
    }

    /// <summary>
    /// Calculates the new buffer size using a linear growth strategy.
    /// </summary>
    private int CalculateLinearGrowthSize(int minimumRequired, int sizeHint)
    {
        int currentLength = _buffer.Length;

        // Grow by the larger of the sizeHint or current length (doubling)
        int growBy = Math.Max(sizeHint, currentLength);

        if (currentLength == 0)
        {
            growBy = Math.Max(growBy, DefaultInitialBufferSize);
        }

        // Calculate potential new size, checking for overflow
        int newSize = currentLength + growBy;
        if ((uint)newSize > Array.MaxLength) // Use uint for overflow check
        {
            // If doubling overflows, just use the minimum required size if it fits
            newSize = minimumRequired;
        }

        // Ensure we meet the minimum requirement if doubling was too small (can happen with large sizeHint)
        if (newSize < minimumRequired)
        {
             newSize = minimumRequired;
        }

        return newSize;
    }

    /// <summary>
    /// Calculates the new buffer size using a doubling strategy, rounding to power of two for large sizes.
    /// </summary>
    private int CalculatePowerOfTwoGrowthSize(int minimumRequired)
    {
        int currentLength = _buffer.Length;

        // Calculate next size based on doubling, ensuring it meets the minimum requirement.
        int newSize = Math.Max(currentLength * 2, minimumRequired);
        if (currentLength == 0) // Handle initial allocation
        {
            newSize = Math.Max(newSize, DefaultInitialBufferSize);
        }

        // If the required size exceeds the threshold, round up to the nearest power of two.
        // This matches the ArrayPoolBufferWriter behavior for potentially fewer large allocations.
        if (minimumRequired > PowerOfTwoThreshold)
        {
            // Ensure uint conversion is safe before calling BitOperations
            uint requiredUnsigned = (uint)minimumRequired;
            if (requiredUnsigned <= int.MaxValue) // Check fits in uint without wrap-around
            {
                 uint powerOfTwoSize = BitOperations.RoundUpToPowerOf2(requiredUnsigned);

                 // Check if powerOfTwoSize exceeds int.MaxValue before casting back
                 if(powerOfTwoSize <= int.MaxValue)
                 {
                     newSize = (int)powerOfTwoSize;
                 }
                 else
                 {
                     // If power of two calculation overflows int.MaxValue,
                     // fall back to the clamped minimum required size (handled later).
                     newSize = minimumRequired;
                 }
            }
            else
            {
                 // If minimumRequired itself is > int.MaxValue, it will be caught by the MaxLength clamp later.
                 newSize = minimumRequired;
            }
        }
         // Ensure we meet the minimum requirement if doubling/power-of-two was too small
         // (can happen if minimumRequired was just slightly larger than currentLength*2 but below threshold)
        if (newSize < minimumRequired)
        {
             newSize = minimumRequired;
        }


        return newSize;
    }

    /// <summary>
    /// Rents a new buffer of the specified size, copies data from the old buffer,
    /// and returns the old buffer to the pool.
    /// </summary>
    /// <param name="newSize">The size of the new buffer to rent.</param>
    [MethodImpl(MethodImplOptions.NoInlining)] // Avoid inlining resizing logic
    private void ResizeAndCopy(int newSize)
    {
        T[] oldBuffer = _buffer;
        _buffer = _pool.Rent(newSize); // Rent the new buffer

        // Copy existing data
        // Note: WrittenSpan cannot be used here as it relies on _buffer, which we are replacing.
        // Use oldBuffer directly with the current index.
        if (_index > 0) // Only copy if there's data
        {
             oldBuffer.AsSpan(0, _index).CopyTo(_buffer);
        }

        // Return the old buffer if it wasn't empty
        if (oldBuffer.Length != 0)
        {
            ReturnBuffer(oldBuffer);
        }
    }


    /// <summary>
    /// Returns the buffer to the pool, applying the configured clear setting.
    /// </summary>
    /// <param name="bufferToReturn">The buffer to return.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ReturnBuffer(T[] bufferToReturn) => _pool.Return(bufferToReturn, clearArray: _clearOnReturn);


    /// <summary>Throws an <see cref="ArgumentException"/> when advancing would exceed capacity.</summary>
    private static void ThrowArgumentExceptionForAdvancedTooFar()
    {
        throw new ArgumentException("Cannot advance past the end of the buffer.", "count");
    }

    /// <inheritdoc/>
    public override string? ToString()
    {
        if (_index == -1) return $"{GetType().Name} (Disposed)";

        // Provide a string representation similar to Memory<T> or Span<T>
        // Special case for char for readability
        if (typeof(T) == typeof(char) && _buffer is char[] chars)
        {
            return new string(chars, 0, _index);
        }

        return $"{GetType().Name}[{_index}]"; // e.g., PooledBufferWriter<Byte>[1024]
    }
}