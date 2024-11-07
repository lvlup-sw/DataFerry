using lvlup.DataFerry.Collections;
using lvlup.DataFerry.Serializers;
using lvlup.DataFerry.Tests.TestModels;
using Microsoft.Extensions.Logging;
using Microsoft.IO;
using Moq;
using System.Buffers;
using System.Diagnostics;
using System.Text.Json;

namespace lvlup.DataFerry.Tests.Serializers
{
    [TestClass]
    public class DataFerrySerializerTests
    {
        private static readonly RecyclableMemoryStreamManager _streamManager = new();
        private Mock<ILogger<DataFerrySerializer>> _mockLogger = default!;
        private DataFerrySerializer _serializer = default!;

        [TestInitialize]
        public void Setup()
        {
            _mockLogger = new Mock<ILogger<DataFerrySerializer>>();
            _serializer = new DataFerrySerializer(_streamManager, _mockLogger.Object);
        }

        [TestCleanup]
        public void Cleanup()
        {
            _mockLogger.VerifyAll();
        }

        [TestMethod]
        public async Task SerializeAsync_ShouldSerializeObjectToByteArray()
        {
            // Arrange
            var testObject = new { Name = "Test", Value = 123 };
            var expectedJson = JsonSerializer.Serialize(testObject);

            // Act
            var result = await _serializer.SerializeAsync(testObject);

            // Assert
            var actualJson = System.Text.Encoding.UTF8.GetString(result);
            Assert.AreEqual(expectedJson, actualJson);
        }

        [TestMethod]
        public void Serialize_WithBufferWriter_ShouldSerializeObjectToBuffer()
        {
            // Arrange
            var testObject = new { Name = "Test", Value = 123 };
            var expectedJson = JsonSerializer.Serialize(testObject);
            var bufferWriter = new ArrayBufferWriter<byte>();

            // Act
            _serializer.Serialize(testObject, bufferWriter);

            // Assert
            var actualJson = System.Text.Encoding.UTF8.GetString(bufferWriter.WrittenSpan);
            Assert.AreEqual(expectedJson, actualJson);
        }

        [TestMethod]
        public void Serialize_ShouldSerializeObjectToString()
        {
            // Arrange
            var testObject = new { Name = "Test", Value = 123 };
            var expectedJson = JsonSerializer.Serialize(testObject);

            // Act
            var result = _serializer.Serialize(testObject);

            // Assert
            Assert.AreEqual(expectedJson, result);
        }

        [TestMethod]
        public void SerializeToUtf8Bytes_ShouldSerializeObjectToUtf8Bytes()
        {
            // Arrange
            var testObject = new { Name = "Test", Value = 123 };
            var expectedJson = JsonSerializer.Serialize(testObject);

            // Act
            var result = _serializer.SerializeToUtf8Bytes(testObject);

            // Assert
            var actualJson = System.Text.Encoding.UTF8.GetString(result);
            Assert.AreEqual(expectedJson, actualJson);
        }

        [TestMethod]
        public async Task SerializeAsync_WithBufferWriter_ShouldSerializeObjectToBuffer()
        {
            // Arrange
            var testObject = new { Name = "Test", Value = 123 };
            var expectedJson = JsonSerializer.Serialize(testObject);
            var bufferWriter = new ArrayBufferWriter<byte>();

            // Act
            await _serializer.SerializeAsync(testObject, bufferWriter);

            // Assert
            var actualJson = System.Text.Encoding.UTF8.GetString(bufferWriter.WrittenSpan);
            Assert.AreEqual(expectedJson, actualJson);
        }

        [TestMethod]
        public void Deserialize_ShouldDeserializeObjectFromByteArray()
        {
            // Arrange
            var testPayload = new Payload
            {
                Identifier = "TestIdentifier",
                Data = "Some data",
                Property = true,
                Version = new decimal(1)
            };
            var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(testPayload);
            var source = new ReadOnlySequence<byte>(jsonBytes);

            // Act
            var result = _serializer.Deserialize<Payload>(source);

            // Assert
            Assert.AreEqual(testPayload.Identifier, result?.Identifier);
            Assert.AreEqual(testPayload.Data, result?.Data);
            Assert.AreEqual(testPayload.Property, result?.Property);
            Assert.AreEqual(testPayload.Version, result?.Version);
        }

        [TestMethod]
        public async Task DeserializeAsync_ShouldDeserializePayloadFromByteArray()
        {
            // Arrange
            var testPayload = new Payload
            {
                Identifier = "TestIdentifier",
                Data = "TestData",
                Property = true,
                Version = 1.0m
            };
            var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(testPayload);

            // Act
            var result = await _serializer.DeserializeAsync<Payload>(jsonBytes);

            // Assert
            Assert.AreEqual(testPayload.Identifier, result?.Identifier);
            Assert.AreEqual(testPayload.Data, result?.Data);
            Assert.AreEqual(testPayload.Property, result?.Property);
            Assert.AreEqual(testPayload.Version, result?.Version);
        }


        [TestMethod]
        public void Deserialize_ShouldHandleInvalidJson()
        {
            // Arrange
            var invalidJsonBytes = System.Text.Encoding.UTF8.GetBytes("{\"Name\": \"Test\",}"); // Invalid JSON
            var source = new ReadOnlySequence<byte>(invalidJsonBytes);

            // Act
            var result = _serializer.Deserialize<Payload>(source);

            // Assert
            Assert.IsNull(result);
            _mockLogger.Verify(x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Failed to deserialize the value.")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()!), Times.Once);
        }

        [TestMethod]
        public async Task DeserializeAsync_ShouldHandleInvalidJson()
        {
            // Arrange
            var invalidJsonBytes = System.Text.Encoding.UTF8.GetBytes("{\"Name\": \"Test\",}");

            // Act
            var result = await _serializer.DeserializeAsync<Payload>(invalidJsonBytes);

            // Assert
            Assert.IsNull(result);
            _mockLogger.Verify(x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Failed to deserialize the value.")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception, string>>()!), Times.Once);
        }

        [TestMethod]
        public void Deserialize_ShouldHandleEmptyInput()
        {
            // Arrange
            var emptySource = new ReadOnlySequence<byte>(Array.Empty<byte>());

            // Act
            var result = _serializer.Deserialize<Payload>(emptySource);

            // Assert
            Assert.IsNull(result);
        }

        [TestMethod]
        public async Task DeserializeAsync_ShouldHandleEmptyInput()
        {
            // Arrange
            var emptySource = Array.Empty<byte>();

            // Act
            var result = await _serializer.DeserializeAsync<Payload>(emptySource);

            // Assert
            Assert.IsNull(result);
        }

        [TestMethod]
        public void SerializeAsync_PerformanceTest()
        {
            // Arrange
            var largeObject = TestUtils.CreateLargePayload();
            var bufferWriter = new ArrayBufferWriter<byte>();

            // Act
            var stopwatch = Stopwatch.StartNew();
            _serializer.SerializeAsync(largeObject, bufferWriter).AsTask().Wait();
            stopwatch.Stop();

            // Assert
            Console.WriteLine($"Serialization time: {stopwatch.ElapsedMilliseconds} ms");

            // Add assertions based on your performance expectations
            Assert.IsTrue(stopwatch.ElapsedMilliseconds < 500, "Serialization took longer than expected.");
        }

        [TestMethod]
        public void Deserialize_ShouldHandleVeryLargeInput()
        {
            // Arrange
            var veryLargePayload = TestUtils.CreateLargePayload(100000);
            var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(veryLargePayload);
            var source = new ReadOnlySequence<byte>(jsonBytes);

            // Act
            var result = _serializer.Deserialize<Payload>(source);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(veryLargePayload.Identifier, result.Identifier);
            Assert.AreEqual(veryLargePayload.Data, result.Data);
            Assert.AreEqual(veryLargePayload.Version, result.Version);
            Assert.AreEqual(veryLargePayload.Property, result.Property);
        }

        [TestMethod]
        public async Task DeserializeAsync_ShouldHandleVeryLargeInput()
        {
            // Arrange
            var veryLargePayload = TestUtils.CreateLargePayload(100000);
            var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(veryLargePayload);

            // Act
            var result = await _serializer.DeserializeAsync<Payload>(jsonBytes);

            // Assert
            Assert.IsNotNull(result);
            Assert.AreEqual(veryLargePayload.Identifier, result.Identifier);
            Assert.AreEqual(veryLargePayload.Data, result.Data);
            Assert.AreEqual(veryLargePayload.Version, result.Version);
            Assert.AreEqual(veryLargePayload.Property, result.Property);
        }

        [TestMethod]
        public void Serialize_PerformanceTest_WithString()
        {
            // Arrange
            var largePayload = TestUtils.CreateLargePayload(100000);

            // Act
            var stopwatch = Stopwatch.StartNew();
            _serializer.Serialize(largePayload);
            stopwatch.Stop();

            // Assert
            Console.WriteLine($"Serialize (string) time: {stopwatch.ElapsedMilliseconds} ms");
            Assert.IsTrue(stopwatch.ElapsedMilliseconds < 1000, "Serialize (string) took longer than expected.");
        }

        [TestMethod]
        public void SerializeToUtf8Bytes_PerformanceTest()
        {
            // Arrange
            var largePayload = TestUtils.CreateLargePayload(100000);

            // Act
            var stopwatch = Stopwatch.StartNew();
            _serializer.SerializeToUtf8Bytes(largePayload);
            stopwatch.Stop();

            // Assert
            Console.WriteLine($"SerializeToUtf8Bytes time: {stopwatch.ElapsedMilliseconds} ms");
            Assert.IsTrue(stopwatch.ElapsedMilliseconds < 1000, "SerializeToUtf8Bytes took longer than expected.");
        }

        [TestMethod]
        public async Task SerializeAsync_ToByteArray_PerformanceTest()
        {
            // Arrange
            var largePayload = TestUtils.CreateLargePayload(100000);

            // Act
            var stopwatch = Stopwatch.StartNew();
            await _serializer.SerializeAsync(largePayload);
            stopwatch.Stop();

            // Assert
            Console.WriteLine($"SerializeAsync (to byte array) time: {stopwatch.ElapsedMilliseconds} ms");
            Assert.IsTrue(stopwatch.ElapsedMilliseconds < 1000, "SerializeAsync (to byte array) took longer than expected.");
        }

        [TestMethod]
        public async Task SerializeAsync_ToByteArray_ShouldSerializePayloadToByteArray()
        {
            // Arrange
            var testPayload = TestUtils.CreatePayloadRandom();
            var expectedJson = JsonSerializer.Serialize(testPayload);

            // Act
            var result = await _serializer.SerializeAsync(testPayload);

            // Assert
            var actualJson = System.Text.Encoding.UTF8.GetString(result);
            Assert.AreEqual(expectedJson, actualJson);
        }

        [TestMethod]
        public async Task SerializeAsync_ToByteArray_ShouldHandleVeryLargeInput()
        {
            // Arrange
            var veryLargePayload = TestUtils.CreateLargePayload(100000);

            // Act
            var result = await _serializer.SerializeAsync(veryLargePayload);

            // Assert
            Assert.IsNotNull(result);
            Assert.IsTrue(result.Length > 0);
        }

        [TestMethod]
        public async Task SerializeAsync_WithBufferWriter_ShouldSerializePayloadToBuffer()
        {
            // Arrange
            var testPayload = TestUtils.CreatePayloadRandom();
            var expectedJson = JsonSerializer.Serialize(testPayload);
            var bufferWriter = new ArrayBufferWriter<byte>();

            // Act
            await _serializer.SerializeAsync(testPayload, bufferWriter);

            // Assert
            var actualJson = System.Text.Encoding.UTF8.GetString(bufferWriter.WrittenSpan);
            Assert.AreEqual(expectedJson, actualJson);
        }

        [TestMethod]
        public async Task SerializeAsync_WithBufferWriter_ShouldHandleVeryLargeInput()
        {
            // Arrange
            var veryLargePayload = TestUtils.CreateLargePayload(100000);
            var bufferWriter = new ArrayBufferWriter<byte>();

            // Act
            await _serializer.SerializeAsync(veryLargePayload, bufferWriter);

            // Assert
            Assert.IsTrue(bufferWriter.WrittenCount > 0);
        }

        [TestMethod]
        public async Task SerializeAsync_WithBufferWriter_PerformanceTest()
        {
            // Arrange
            var largePayload = TestUtils.CreateLargePayload(100000);
            var bufferWriter = new ArrayBufferWriter<byte>();

            // Act
            var stopwatch = Stopwatch.StartNew();
            await _serializer.SerializeAsync(largePayload, bufferWriter);
            stopwatch.Stop();

            // Assert
            Console.WriteLine($"SerializeAsync (with IBufferWriter) time: {stopwatch.ElapsedMilliseconds} ms");
            Assert.IsTrue(stopwatch.ElapsedMilliseconds < 1000, "SerializeAsync (with IBufferWriter) took longer than expected.");
        }

        [TestMethod]
        public void Serialize_WithBufferWriter_ShouldSerializePayloadToBuffer()
        {
            // Arrange
            var testPayload = TestUtils.CreatePayloadRandom();
            var expectedJson = JsonSerializer.Serialize(testPayload);
            var bufferWriter = new ArrayBufferWriter<byte>();

            // Act
            _serializer.Serialize(testPayload, bufferWriter);

            // Assert
            var actualJson = System.Text.Encoding.UTF8.GetString(bufferWriter.WrittenSpan);
            Assert.AreEqual(expectedJson, actualJson);
        }

        [TestMethod]
        public void Serialize_WithBufferWriter_ShouldHandleVeryLargeInput()
        {
            // Arrange
            var veryLargePayload = TestUtils.CreateLargePayload(100000);
            var bufferWriter = new ArrayBufferWriter<byte>();

            // Act
            _serializer.Serialize(veryLargePayload, bufferWriter);

            // Assert
            Assert.IsTrue(bufferWriter.WrittenCount > 0);
        }

        [TestMethod]
        public void Serialize_WithBufferWriter_PerformanceTest()
        {
            // Arrange
            var largePayload = TestUtils.CreateLargePayload(100000);
            var bufferWriter = new ArrayBufferWriter<byte>();

            // Act
            var stopwatch = Stopwatch.StartNew();
            _serializer.Serialize(largePayload, bufferWriter);
            stopwatch.Stop();

            // Assert
            Console.WriteLine($"Serialize (with IBufferWriter) time: {stopwatch.ElapsedMilliseconds} ms");
            Assert.IsTrue(stopwatch.ElapsedMilliseconds < 1000, "Serialize (with IBufferWriter) took longer than expected.");
        }
    }
}
