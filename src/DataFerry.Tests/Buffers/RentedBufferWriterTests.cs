using lvlup.DataFerry.Buffers;
using lvlup.DataFerry.Collections;

namespace lvlup.DataFerry.Tests.Buffers
{
    [TestClass]
    public class RentedBufferWriterTests
    {
        private static readonly StackArrayPool<byte> _pool = new();

        [TestMethod]
        public void Advance_ShouldIncreaseIndex()
        {
            // Arrange
            var writer = new RentedBufferWriter<byte>(_pool);

            // Act
            var span = writer.GetSpan(500);
            writer.Advance(10);

            // Assert
            Assert.AreEqual(span.Length - 10, writer.FreeCapacity);
        }

        [TestMethod]
        public void GetMemory_ShouldReturnMemoryWithCorrectSize_IfGreaterThanDefault()
        {
            // Arrange
            var writer = new RentedBufferWriter<byte>(_pool);

            // Act
            var memory = writer.GetMemory(300);

            // Assert
            Assert.AreEqual(300, memory.Length);
        }

        [TestMethod]
        public void GetMemory_ShouldReturnMemoryWithDefaultSize_IfLessThanDefault()
        {
            // Arrange
            var writer = new RentedBufferWriter<byte>(_pool);

            // Act
            var memory = writer.GetMemory(100);

            // Assert
            Assert.AreEqual(256, memory.Length);
        }

        [TestMethod]
        public void GetSpan_ShouldReturnSpanWithCorrectSize_IfGreaterThanDefault()
        {
            // Arrange
            var writer = new RentedBufferWriter<byte>(_pool);

            // Act
            var span = writer.GetSpan(300);

            // Assert
            Assert.AreEqual(300, span.Length);
        }

        [TestMethod]
        public void GetSpan_ShouldReturnMemoryWithDefaultSize_IfLessThanDefault()
        {
            // Arrange
            var writer = new RentedBufferWriter<byte>(_pool);

            // Act
            var span = writer.GetSpan(100);

            // Assert
            Assert.AreEqual(256, span.Length);
        }

        [TestMethod]
        public void GetMemory_WithEmptyBuffer_ShouldReturnMemoryWithDefaultSize()
        {
            // Arrange
            var writer = new RentedBufferWriter<byte>(_pool);

            // Act
            var memory = writer.GetMemory(); // No sizeHint provided

            // Assert
            Assert.AreEqual(256, memory.Length);
        }

        [TestMethod]
        public void GetSpan_WithEmptyBuffer_ShouldReturnSpanWithDefaultSize()
        {
            // Arrange
            var writer = new RentedBufferWriter<byte>(_pool);

            // Act
            var span = writer.GetSpan(); // No sizeHint provided

            // Assert
            Assert.AreEqual(256, span.Length);
        }

        [TestMethod]
        public void Write_ShouldWriteToBuffer()
        {
            // Arrange
            var writer = new RentedBufferWriter<byte>(_pool);

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
            var writer = new RentedBufferWriter<byte>(_pool);

            // Act
            var span = writer.GetSpan(10);
            for (int i = 0; i < 10; i++)
            {
                span[i] = (byte)i;
            }
            writer.Advance(10);
            span = writer.GetSpan(100);

            // Assert
            Assert.IsTrue(span.Length >= 100);
        }

        [TestMethod]
        public void Dispose_ShouldReturnBufferToPool()
        {
            // Arrange
            var writer = new RentedBufferWriter<byte>(_pool);
            writer.GetSpan(100); // Force a buffer to be rented

            // Act
            writer.Dispose();

            // Assert
            Assert.AreEqual(0, writer.FreeCapacity); // Buffer should be empty after dispose
        }

        [TestMethod]
        public void WriteAndGetPosition_ShouldWriteToBufferAndReturnCorrectPosition()
        {
            // Arrange
            var writer = new RentedBufferWriter<byte>(_pool);
            var value = new byte[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 };

            // Act
            var (index, length) = writer.WriteAndGetPosition(value);
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
            var writer = new RentedBufferWriter<byte>(_pool);
            var value1 = new byte[] { 0, 1, 2, 3, 4 };
            var value2 = new byte[] { 5, 6, 7, 8, 9 };

            // Act
            var (index1, length1) = writer.WriteAndGetPosition(value1);
            var (index2, length2) = writer.WriteAndGetPosition(value2);
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
            var writer = new RentedBufferWriter<byte>(_pool);
            var value = new byte[300]; // Larger than the default buffer size

            // Act
            var (index, length) = writer.WriteAndGetPosition(value);
            var array = writer.ToArray();

            // Assert
            CollectionAssert.AreEqual(value, array);
            Assert.AreEqual(0, index);
            Assert.AreEqual(value.Length, length);
        }
    }
}