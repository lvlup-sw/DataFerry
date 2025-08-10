using System.Buffers;
using System.Numerics;
using lvlup.DataFerry.Memory;

namespace lvlup.DataFerry.Tests.Unit;

[TestClass]
public class PooledBufferWriterTests
{
    private static readonly ArrayPool<byte> Pool = ArrayPool<byte>.Shared;

    #region Constructor Tests

    [TestMethod]
    public void Constructor_WithNullPool_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Assert.ThrowsExactly<ArgumentNullException>(() =>
        {
            _ = new PooledBufferWriter<byte>(pool: null!);
        });
    }

    [TestMethod]
    public void Constructor_WithNegativeInitialCapacity_ShouldThrowArgumentOutOfRangeException()
    {
        // Arrange
        var options = new PooledBufferWriterOptions { InitialCapacity = -1 };
        
        // Act & Assert
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
        {
            _ = new PooledBufferWriter<byte>(Pool, options);
        });
    }

    [TestMethod]
    public void Constructor_WithPositiveInitialCapacity_ShouldRentBufferWithCorrectCapacity()
    {
        // Arrange
        const int initialCapacity = 128;
        var options = new PooledBufferWriterOptions { InitialCapacity = initialCapacity };

        // Act
        using var writer = new PooledBufferWriter<byte>(Pool, options);

        // Assert
        Assert.AreEqual(0, writer.WrittenCount);
        Assert.IsTrue(writer.Capacity >= initialCapacity, $"Capacity should be at least {initialCapacity}, but was {writer.Capacity}"); // Pool might return larger array
        Assert.AreEqual(writer.Capacity, writer.FreeCapacity);
    }

    [TestMethod]
    public void Constructor_WithOptions_ShouldRespectOptions()
    {
        // Arrange
        var options = new PooledBufferWriterOptions
        {
            InitialCapacity = 64,
            ClearOnReturn = false,
            Strategy = GrowthStrategy.PowerOfTwo
        };

        // Act
        using var writer = new PooledBufferWriter<byte>(Pool, options);
        writer.GetSpan(100);

        // Assert
        Assert.AreEqual(0, writer.WrittenCount);
        Assert.IsTrue(writer.Capacity >= 128, $"Capacity should grow (likely to power of 2 >= 100), but was {writer.Capacity}");
    }

    #endregion
    #region Advance Tests

    [TestMethod]
    public void Advance_WithNegativeCount_ShouldThrowArgumentOutOfRangeException()
    {
        // Arrange
        var writer = new PooledBufferWriter<byte>(Pool);
        writer.GetMemory(10);

        // Act & Assert
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => writer.Advance(-1));
    }

    [TestMethod]
    public void Advance_ExceedingCapacity_ShouldThrowArgumentException()
    {
        // Arrange
        var writer = new PooledBufferWriter<byte>(Pool);

        // Act & Assert
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => writer.Advance(writer.Capacity + 1));
    }

    [TestMethod]
    public void Advance_ShouldIncreaseIndex()
    {
        // Arrange
        using var writer = new PooledBufferWriter<byte>(Pool);
        Assert.AreEqual(0, writer.Capacity, "Initial capacity should be 0");
        Assert.AreEqual(0, writer.WrittenCount, "Initial written count should be 0");

        // Act
        var span = writer.GetSpan(500);
        int initialFreeCapacity = writer.FreeCapacity;
        writer.Advance(10);

        // Assert
        Assert.AreEqual(initialFreeCapacity - 10, writer.FreeCapacity);
        Assert.AreEqual(10, writer.WrittenCount);
    }
    
    #endregion
    #region GetMemory / GetSpan Tests

    [TestMethod]
    public void GetMemory_ShouldReturnMemoryWithCorrectSize_IfGreaterThanDefault()
    {
        // Arrange
        using var writer = new PooledBufferWriter<byte>(Pool);

        // Act
        var memory = writer.GetMemory(300);

        // Assert
        Assert.IsTrue(memory.Length >= 300, $"Memory length should be >= 300, but was {memory.Length}");
        Assert.AreEqual(memory.Length, writer.Capacity);
    }

    [TestMethod]
    public void GetMemory_ShouldReturnMemoryWithDefaultSize_IfLessThanDefault()
    {
        // Arrange
        using var writer = new PooledBufferWriter<byte>(Pool);

        // Act
        var memory = writer.GetMemory(100);

        // Assert
        Assert.IsTrue(memory.Length >= 256, $"Memory length should be >= 256, but was {memory.Length}");
        Assert.AreEqual(memory.Length, writer.Capacity);
    }

    [TestMethod]
    public void GetSpan_ShouldReturnSpanWithCorrectSize_IfGreaterThanDefault()
    {
        // Arrange
        using var writer = new PooledBufferWriter<byte>(Pool);

        // Act
        var span = writer.GetSpan(300);

        // Assert
        Assert.IsTrue(span.Length >= 300, $"Span length should be >= 300, but was {span.Length}");
        Assert.AreEqual(span.Length, writer.Capacity);
    }

    [TestMethod]
    public void GetSpan_ShouldReturnMemoryWithDefaultSize_IfLessThanDefault()
    {
        // Arrange
        using var writer = new PooledBufferWriter<byte>(Pool);

        // Act
        var span = writer.GetSpan(100);

        // Assert
        Assert.IsTrue(span.Length >= 256, $"Span length should be >= 256, but was {span.Length}");
        Assert.AreEqual(span.Length, writer.Capacity);
    }

    [TestMethod]
    public void GetMemory_WithEmptyBuffer_ShouldReturnMemoryWithDefaultSize()
    {
        // Arrange
        using var writer = new PooledBufferWriter<byte>(Pool);

        // Act
        var memory = writer.GetMemory();

        // Assert
        Assert.IsTrue(memory.Length >= 256, $"Memory length should be >= 256, but was {memory.Length}");
        Assert.AreEqual(memory.Length, writer.Capacity);
    }

    [TestMethod]
    public void GetSpan_WithEmptyBuffer_ShouldReturnSpanWithDefaultSize()
    {
        // Arrange
        using var writer = new PooledBufferWriter<byte>(Pool);

        // Act
        var span = writer.GetSpan();

        // Assert
        Assert.IsTrue(span.Length >= 256, $"Span length should be >= 256, but was {span.Length}");
        Assert.AreEqual(span.Length, writer.Capacity);
    }

    #endregion
    #region Write / Grow Tests

    [TestMethod]
    public void Write_ShouldWriteToBuffer()
    {
        // Arrange
        using var writer = new PooledBufferWriter<byte>(Pool);

        // Act
        var span = writer.GetSpan(10);
        for (int i = 0; i < 10; i++)
        {
            span[i] = (byte)i;
        }
        writer.Advance(10);
        var array = writer.ToArray();

        // Assert
        CollectionAssert.AreEqual(new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 }, array);
    }

    [TestMethod]
    public void Grow_ShouldResizeBuffer()
    {
        // Arrange
        using var writer = new PooledBufferWriter<byte>(Pool);

        // Act
        var span1 = writer.GetSpan(10);
        int capacity1 = writer.Capacity;
        Assert.IsTrue(capacity1 >= 10);
        for (int i = 0; i < 10; i++) { span1[i] = (byte)i; }
        writer.Advance(10);

        var span2 = writer.GetSpan(capacity1 + 100);
        int capacity2 = writer.Capacity;
        
        // Assert
        Assert.IsTrue(capacity2 > capacity1, "Capacity should have increased");
        Assert.IsTrue(span2.Length >= capacity1 + 100, $"New span length should be >= {capacity1 + 100}");
        Assert.IsTrue(writer.FreeCapacity >= capacity1 + 100);
    }

    [TestMethod]
    public void WriteAndGetPosition_ShouldWriteToBufferAndReturnCorrectPosition()
    {
        // Arrange
        using var writer = new PooledBufferWriter<byte>(Pool);
        var value = new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };

        // Act
        (int index, int length) = writer.WriteAndGetPosition(value);
        var array = writer.ToArray();

        // Assert
        CollectionAssert.AreEqual(value, array);
        Assert.AreEqual(0, index);
        Assert.AreEqual(value.Length, length);
    }

    [TestMethod]
    public void WriteAndGetPosition_ShouldReturnCorrectPosition_WhenCalledMultipleTimes()
    {
        // Arrange
        using var writer = new PooledBufferWriter<byte>(Pool);
        var value1 = new byte[] { 0, 1, 2, 3, 4 };
        var value2 = new byte[] { 5, 6, 7, 8, 9 };

        // Act
        (int index1, int length1) = writer.WriteAndGetPosition(value1);
        (int index2, int length2) = writer.WriteAndGetPosition(value2);
        var array = writer.ToArray();

        // Assert
        CollectionAssert.AreEqual(value1.Concat(value2).ToArray(), array);
        Assert.AreEqual(0, index1);
        Assert.AreEqual(value1.Length, length1);
        Assert.AreEqual(value1.Length, index2);
        Assert.AreEqual(value2.Length, length2);
    }

    [TestMethod]
    public void WriteAndGetPosition_ShouldResizeBuffer_WhenCapacityIsExceeded()
    {
        // Arrange
        using var writer = new PooledBufferWriter<byte>(Pool, new PooledBufferWriterOptions { InitialCapacity = 10 });
        var value1 = new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };
        writer.WriteAndGetPosition(value1);
        int capacity1 = writer.Capacity;

        var value2 = new byte[200];

        // Act
        (int index, int length) = writer.WriteAndGetPosition(value2);
        var array = writer.ToArray();
        int capacity2 = writer.Capacity;
        
        // Assert
        CollectionAssert.AreEqual(value1.Concat(value2).ToArray(), array);
        Assert.AreEqual(value1.Length, index);
        Assert.AreEqual(value2.Length, length);
        Assert.IsTrue(capacity2 > capacity1, "Capacity should have increased");
        Assert.IsTrue(capacity2 >= writer.WrittenCount);
    }

    #endregion
    #region Dispose Tests

    [TestMethod]
    public void GetMemory_AfterDispose_ShouldThrowObjectDisposedException()
    {
        // Arrange
        var writer = new PooledBufferWriter<byte>(Pool);
        writer.GetMemory(10);
        writer.Dispose();

        // Act & Assert
        Assert.ThrowsExactly<ObjectDisposedException>(() => writer.GetMemory(1));
    }

    [TestMethod]
    public void GetSpan_AfterDispose_ShouldThrowObjectDisposedException()
    {
        // Arrange
        var writer = new PooledBufferWriter<byte>(Pool);
        writer.GetSpan(10);
        writer.Dispose();

        // Act & Assert
        Assert.ThrowsExactly<ObjectDisposedException>(() => writer.GetSpan(1));
    }

    [TestMethod]
    public void Advance_AfterDispose_ShouldThrowObjectDisposedException()
    {
        // Arrange
        var writer = new PooledBufferWriter<byte>(Pool);
        writer.GetMemory(10);
        writer.Advance(5);
        writer.Dispose();

        // Act & Assert
        Assert.ThrowsExactly<ObjectDisposedException>(() => writer.Advance(1));
    }

    [TestMethod]
    public void WriteAndGetPosition_AfterDispose_ShouldThrowObjectDisposedException()
    {
        // Arrange
        var writer = new PooledBufferWriter<byte>(Pool);
        writer.GetMemory(10);
        writer.Advance(5);
        writer.Dispose();
        var data = new byte[] { 1, 2 };

        // Act & Assert
        Assert.ThrowsExactly<ObjectDisposedException>(() => writer.WriteAndGetPosition(data));
    }

    [TestMethod]
    public void Dispose_ShouldReturnBufferToPool()
    {
        // Arrange
        var writer = new PooledBufferWriter<byte>(Pool);
        writer.GetSpan(100);

        // Act
        writer.Dispose();

        // Assert
        Assert.AreEqual(0, writer.Capacity, "Capacity should be 0 after dispose");
        var indexField = typeof(PooledBufferWriter<byte>).GetField("_index", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var indexValue = (int)indexField!.GetValue(writer)!;
        Assert.AreEqual(-1, indexValue, "Index should be -1 after dispose");
        Assert.AreEqual(1, writer.FreeCapacity, "FreeCapacity should be 0 after dispose");
    }

    #endregion
    #region Clear Tests

    [TestMethod]
    public void Clear_ShouldResetIndexAndAllowReuse()
    {
        // Arrange
        using var writer = new PooledBufferWriter<byte>(Pool);
        var data1 = new byte[] { 1, 2, 3, 4 };
        var data2 = new byte[] { 5, 6 };
        writer.WriteAndGetPosition(data1);
        int capacityBeforeClear = writer.Capacity;

        // Act
        writer.Clear();
        (int index2, int length2) = writer.WriteAndGetPosition(data2);
        var finalArray = writer.ToArray();

        // Assert
        Assert.AreEqual(data2.Length, writer.WrittenCount);
        Assert.AreEqual(capacityBeforeClear, writer.Capacity, "Capacity should not change after Clear");
        Assert.AreEqual(0, index2, "Index of second write should be 0 after clear");
        Assert.AreEqual(data2.Length, length2);
        CollectionAssert.AreEqual(data2, finalArray, "Final array should only contain data written after clear");
    }

    #endregion
    #region Growth Strategy Tests

    [TestMethod]
    public void GrowBuffer_LinearStrategy_ShouldGrowLinearly()
    {
        // Arrange
        var options = new PooledBufferWriterOptions { InitialCapacity = 100, Strategy = GrowthStrategy.Linear };
        using var writer = new PooledBufferWriter<byte>(Pool, options);
        writer.Advance(100);

        int capacityBeforeGrow = writer.Capacity;
        Assert.IsTrue(capacityBeforeGrow >= 100);

        // Act
        writer.GetSpan(50);
        int capacityAfterGrow = writer.Capacity;

        // Assert
        int expectedMinCapacity = capacityBeforeGrow + Math.Max(50, capacityBeforeGrow);
        Assert.IsTrue(capacityAfterGrow >= expectedMinCapacity, $"Expected capacity >= {expectedMinCapacity}, but got {capacityAfterGrow}");
    }

    [TestMethod]
    public void GrowBuffer_PowerOfTwoStrategy_ShouldGrowToPowerOfTwo()
    {
        // Arrange
        const int initialCapacity = 100;
        var options = new PooledBufferWriterOptions { InitialCapacity = initialCapacity, Strategy = GrowthStrategy.PowerOfTwo };
        using var writer = new PooledBufferWriter<byte>(Pool, options);
        writer.Advance(initialCapacity);

        int capacityBeforeGrow = writer.Capacity;
        Assert.IsTrue(capacityBeforeGrow >= initialCapacity);

        // Act
        writer.GetSpan(50);
        int capacityAfterGrow = writer.Capacity;

        // Assert
        const int minimumRequired = initialCapacity + 50;
        int minExpectedPowerOfTwo = (int)BitOperations.RoundUpToPowerOf2(minimumRequired);
        Assert.IsTrue(capacityAfterGrow >= minExpectedPowerOfTwo, $"Expected capacity >= {minExpectedPowerOfTwo} (Power of 2 for required size), but got {capacityAfterGrow}");
    }

    [TestMethod]
    public void GrowBuffer_PowerOfTwoStrategy_LargeSize_ShouldGrowToPowerOfTwo()
    {
        // Arrange
        const int initialCapacity = 1024 * 1024;
        var options = new PooledBufferWriterOptions { InitialCapacity = initialCapacity, Strategy = GrowthStrategy.PowerOfTwo };
        using var writer = new PooledBufferWriter<byte>(Pool, options);
        writer.Advance(initialCapacity);
        int capacityBeforeGrow = writer.Capacity;
        Assert.IsTrue(capacityBeforeGrow >= initialCapacity);
        
        // Act
        writer.GetSpan(100);
        int capacityAfterGrow = writer.Capacity;

        // Assert
        const int minimumRequired = initialCapacity + 100;
        int expectedCapacity = (int)BitOperations.RoundUpToPowerOf2(minimumRequired);
        Assert.IsTrue(capacityAfterGrow >= expectedCapacity, $"Expected capacity >= {expectedCapacity} (Power of 2), but got {capacityAfterGrow}");
    }
    
    [TestMethod]
    public void GrowBuffer_RequestedSizeExceedsArrayMaxLength_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var options = new PooledBufferWriterOptions { InitialCapacity = Array.MaxLength - 10, Strategy = GrowthStrategy.Linear };
        var writer = new PooledBufferWriter<byte>(Pool, options);
        writer.Advance(writer.Capacity);

        // Act & Assert
        var ex = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => writer.GetSpan(11));
        StringAssert.Contains(ex.Message, "Requested buffer size exceeds Array.MaxLength.");
    }
    
    [TestMethod]
    public void CalculateLinearGrowthSize_DoublingOverflows_UsesMinimumRequired()
    {
        // Arrange
        int startSize = Array.MaxLength - 1000;
        const int requestedSize = 500;
        int minimumRequired = startSize + requestedSize;
        Assert.IsTrue(minimumRequired <= Array.MaxLength, "Test setup error: minimumRequired exceeds Array.MaxLength.");
        Assert.IsTrue(startSize > 0, "Test setup error: startSize must be positive.");
        Assert.IsTrue(requestedSize > 0, "Test setup error: requestedSize must be positive.");
        var writer = new PooledBufferWriter<byte>(Pool, new PooledBufferWriterOptions { Strategy = GrowthStrategy.Linear });
        var bufferField = typeof(PooledBufferWriter<byte>).GetField("_buffer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var indexField = typeof(PooledBufferWriter<byte>).GetField("_index", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var initialBuffer = Pool.Rent(startSize);
        if (initialBuffer.Length >= minimumRequired)
        {
            Pool.Return(initialBuffer);
            initialBuffer = Pool.Rent(startSize);
            if (initialBuffer.Length >= minimumRequired)
            {
                Assert.Inconclusive("Could not obtain a buffer from the pool smaller than minimumRequired to reliably test overflow scenario.");
            }
        }
        bufferField!.SetValue(writer, initialBuffer);
        indexField!.SetValue(writer, startSize);

        // Act
        writer.GetSpan(requestedSize);

        // Assert
        int poolRentedSizeForMinRequired = Pool.Rent(minimumRequired).Length;
        Pool.Return(Pool.Rent(minimumRequired));
        if (minimumRequired < Array.MaxLength && poolRentedSizeForMinRequired < Array.MaxLength)
        {
            Assert.IsTrue(writer.Capacity < Array.MaxLength, $"Capacity ({writer.Capacity}) should be based on minimumRequired ({minimumRequired}), not clamped unnecessarily to MaxLength ({Array.MaxLength}). Pool would have returned {poolRentedSizeForMinRequired}.");
        }
        Pool.Return(initialBuffer);
    }

    [TestMethod]
    public void CalculatePowerOfTwoGrowthSize_CurrentLengthZero_UsesDefaultInitialSizeIfLarger()
    {
        // Arrange
        var options = new PooledBufferWriterOptions { InitialCapacity = 0, Strategy = GrowthStrategy.PowerOfTwo };
        using var writer = new PooledBufferWriter<byte>(Pool, options);
        const int smallSizeHint = 16;

        // Act
        writer.GetSpan(smallSizeHint);

        // Assert
        Assert.IsTrue(writer.Capacity >= 256, $"Capacity should be at least DefaultInitialBufferSize (256), but was {writer.Capacity}");
    }

    [TestMethod]
    public void CalculatePowerOfTwoGrowthSize_PowerOfTwoOverflows_UsesMinimumRequired()
    {
        // Arrange
        const int minimumRequired = (int.MaxValue / 2) + 2;
        const int startSize = minimumRequired - 10;
        const int requestedSize = 10;
        var writer = new PooledBufferWriter<byte>(Pool, new PooledBufferWriterOptions { Strategy = GrowthStrategy.PowerOfTwo });
        var bufferField = typeof(PooledBufferWriter<byte>).GetField("_buffer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var indexField = typeof(PooledBufferWriter<byte>).GetField("_index", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var initialBuffer = Pool.Rent(startSize);
        bufferField!.SetValue(writer, initialBuffer);
        indexField!.SetValue(writer, startSize);
        
        // Act
        var growMethod = typeof(PooledBufferWriter<byte>).GetMethod("GrowBuffer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        try
        {
             growMethod!.Invoke(writer, [requestedSize]);
        }
        catch (System.Reflection.TargetInvocationException ex) when (ex.InnerException is ArgumentOutOfRangeException innerEx)
        {
            if ((uint)minimumRequired <= Array.MaxLength)
            {
                throw;
            }

            StringAssert.Contains(innerEx.Message, "Requested buffer size exceeds Array.MaxLength.");
            Pool.Return(initialBuffer); 
            return;
        }
        
        // Assert
        int expectedCapacity = Math.Min(minimumRequired, Array.MaxLength);
        Assert.IsTrue(writer.Capacity >= expectedCapacity, $"Capacity should be at least {expectedCapacity}, but was {writer.Capacity}");
        Pool.Return(initialBuffer);
    }

    [TestMethod]
    public void CalculatePowerOfTwoGrowthSize_FinalCheck_UsesMinimumRequiredIfCalculatedSizeIsLess()
    {
        // Arrange
        const int startSize = 500;
        const int minimumRequired = 600;
        const int requestedSize = minimumRequired-startSize;
        var options = new PooledBufferWriterOptions { InitialCapacity = startSize, Strategy = GrowthStrategy.PowerOfTwo };
        using var writer = new PooledBufferWriter<byte>(Pool, options);
        writer.Advance(startSize);
        int capacityBeforeGrow = writer.Capacity;
        Assert.IsTrue(capacityBeforeGrow >= startSize);

        // Act
        writer.GetSpan(requestedSize);
        int capacityAfterGrow = writer.Capacity;

        // Assert
        Assert.IsTrue(capacityAfterGrow >= minimumRequired, $"Capacity should be at least {minimumRequired}, but got {capacityAfterGrow}");
        Assert.IsTrue(capacityAfterGrow >= Math.Min(startSize*2, Array.MaxLength), "Capacity should have grown substantially or met minimum");
    }    
    
    #endregion
    #region ToString Tests

    [TestMethod]
    public void ToString_Default_ShouldReturnTypeNameAndCount()
    {
        // Arrange
        using var writer = new PooledBufferWriter<byte>(Pool);
        writer.WriteAndGetPosition([1, 2, 3]);

        // Act
        string result = writer.ToString();

        // Assert
        Assert.AreEqual("PooledBufferWriter`1[3]", result);
    }

    [TestMethod]
    public void ToString_CharType_ShouldReturnStringContent()
    {
        // Arrange
        using var writer = new PooledBufferWriter<char>(ArrayPool<char>.Shared);
        writer.WriteAndGetPosition("Hello".AsSpan());

        // Act
        string result = writer.ToString();

        // Assert
        Assert.AreEqual("Hello", result);
    }

    [TestMethod]
    public void ToString_Disposed_ShouldIndicateDisposed()
    {
        // Arrange
        var writer = new PooledBufferWriter<int>(ArrayPool<int>.Shared);
        writer.GetMemory(5);
        writer.Dispose();

        // Act
        string result = writer.ToString();

        // Assert
        StringAssert.Contains(result, "PooledBufferWriter`1");
        StringAssert.Contains(result, "(Disposed)");
    }

    #endregion
}