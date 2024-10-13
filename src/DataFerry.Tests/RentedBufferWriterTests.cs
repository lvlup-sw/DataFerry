using lvlup.DataFerry.Collections;

namespace lvlup.DataFerry.Tests
{
    [TestClass]
    public class RentedBufferWriterTests
    {
        private StackArrayPool<byte> _pool;

        [TestInitialize]
        public void Initialize()
        {
            _pool = new StackArrayPool<byte>();
        }

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
    }
}