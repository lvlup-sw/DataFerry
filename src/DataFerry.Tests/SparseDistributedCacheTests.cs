using lvlup.DataFerry.Caches.Abstractions;
using lvlup.DataFerry.Caches;
using lvlup.DataFerry.Collections;
using lvlup.DataFerry.Properties;
using lvlup.DataFerry.Serializers.Abstractions;
using Moq;
using StackExchange.Redis;
using System.Buffers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using lvlup.DataFerry.Tests.TestModels;
using System.Text.Json;
using System.Text;

namespace lvlup.DataFerry.Tests
{
    [TestClass]
    public class SparseDistributedCacheTests
    {
        private SparseDistributedCache _cache = default!;
        private Mock<IConnectionMultiplexer> _redis = default!;
        private Mock<ILfuMemCache<string, byte[]>> _memCache = default!;
        private Mock<IDataFerrySerializer> _serializer = default!;
        private IOptions<CacheSettings> _settings = default!;
        private Mock<ILogger<SparseDistributedCache>> _logger = default!;

        [TestInitialize]
        public void Setup()
        {
            // Setup dependencies
            _redis = new Mock<IConnectionMultiplexer>();
            _memCache = new Mock<ILfuMemCache<string, byte[]>>();
            _serializer = new Mock<IDataFerrySerializer>();
            _settings = Options.Create(new CacheSettings
            {
                DesiredPolicy = ResiliencyPatterns.Advanced,
                TimeoutInterval = 30,
                BulkheadMaxParallelization = 10,
                BulkheadMaxQueuingActions = 100,
                RetryCount = 1,
                UseExponentialBackoff = true,
                RetryInterval = 2,
                CircuitBreakerCount = 3,
                CircuitBreakerInterval = 1,
                AbsoluteExpiration = 24,
                InMemoryAbsoluteExpiration = 60,
                UseMemoryCache = true
            });
            _logger = new Mock<ILogger<SparseDistributedCache>>();

            // Create the SparseDistributedCache instance
            _cache = new SparseDistributedCache(
                _redis.Object,
                _memCache.Object,
                _serializer.Object,
                _settings,
                _logger.Object
            );
        }

        [TestCleanup]
        public void Cleanup()
        {
            _redis.VerifyAll();
            _memCache.VerifyAll();
            _redis.Reset();
            _memCache.Reset();
        }

        #region GET

        [DataTestMethod]
        [DataRow("testKey")]
        [DataRow("payload123")]
        [DataRow("levelupsoftware")]
        [TestMethod]
        public void GetFromCache_KeyInMemCache_ReturnsValue(string key)
        {
            // Arrange
            Payload expectedValue = TestUtils.CreatePayloadWithInput(key);
            string serializedValue = JsonSerializer.Serialize(expectedValue);
            byte[] serializedByte = Encoding.UTF8.GetBytes(serializedValue);
            byte[] deserializedByte = JsonSerializer.SerializeToUtf8Bytes(expectedValue);

            _memCache.Setup(cache => cache.TryGet(key, out serializedByte)).Returns(true);
            _serializer.Setup(m => m.Deserialize<byte[]>(It.IsAny<ReadOnlySequence<byte>>(), default)).Returns(deserializedByte);
            var destination = new ArrayBufferWriter<byte>();

            // Act
            _cache.GetFromCache(key, destination);

            // Assert
            _memCache.Verify(cache => cache.TryGet(key, out serializedByte), Times.Once);
            _serializer.Verify(s => s.Deserialize<byte[]>(It.IsAny<ReadOnlySequence<byte>>(), default), Times.Once);
            CollectionAssert.AreEqual(deserializedByte, destination.WrittenMemory.ToArray());
        }

        [DataTestMethod]
        [DataRow("testKey")]
        [DataRow("payload123")]
        [DataRow("levelupsoftware")]
        [TestMethod]
        public void GetFromCache_KeyNotInMemCache_ButInRedis_ReturnsValue(string key)
        {
            // Arrange
            Payload expectedValue = TestUtils.CreatePayloadWithInput(key);
            string serializedValue = JsonSerializer.Serialize(expectedValue);
            byte[] serializedByte = Encoding.UTF8.GetBytes(serializedValue);
            byte[] deserializedByte = JsonSerializer.SerializeToUtf8Bytes(expectedValue);

            RedisValue redisSerializedValue = new(serializedByte.AsSpan().ToString());
            byte[]? memValue = default;
            _memCache.Setup(cache => cache.TryGet(key, out memValue)).Returns(false);
            Mock<IDatabase> database = new();
            database.Setup(cache => cache.StringGet(key, CommandFlags.PreferReplica)).Returns(redisSerializedValue);
            _serializer.Setup(m => m.Deserialize<byte[]>(It.IsAny<ReadOnlySequence<byte>>(), default)).Returns(deserializedByte);
            _redis.Setup(db => db.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(database.Object);
            var destination = new ArrayBufferWriter<byte>();

            // Act: Call the method under test
            _cache.GetFromCache(key, destination);

            // Assert: Verify the result
            _memCache.Verify(cache => cache.TryGet(key, out serializedByte), Times.Once);
            _serializer.Verify(s => s.Deserialize<byte[]>(It.IsAny<ReadOnlySequence<byte>>(), default), Times.Once);
            database.Verify(cache => cache.StringGet(key, CommandFlags.PreferReplica), Times.Once);
            CollectionAssert.AreEqual(deserializedByte, destination.WrittenMemory.ToArray());
        }

        [DataTestMethod]
        [DataRow("testKey")]
        [DataRow("payload123")]
        [DataRow("levelupsoftware")]
        [TestMethod]
        public void GetFromCache_KeyNotFound_ReturnsNull(string key)
        {
            // Arrange
            Payload expectedValue = TestUtils.CreatePayloadWithInput(key);
            byte[] deserializedByte = JsonSerializer.SerializeToUtf8Bytes(expectedValue);

            byte[]? nullValue = default;
            _memCache.Setup(cache => cache.TryGet(key, out nullValue)).Returns(false);
            Mock<IDatabase> database = new();
            _serializer.Setup(m => m.Deserialize<byte[]>(It.IsAny<ReadOnlySequence<byte>>(), default)).Returns(deserializedByte);
            database.Setup(cache => cache.StringGet(key, CommandFlags.PreferReplica)).Returns(nullValue);
            _redis.Setup(db => db.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(database.Object);
            var destination = new ArrayBufferWriter<byte>();

            // Act: Call the method under test
            _cache.GetFromCache(key, destination);

            // Assert: Verify the result
            _memCache.Verify(cache => cache.TryGet(key, out nullValue), Times.Once);
            _serializer.Verify(s => s.Deserialize<byte[]>(It.IsAny<ReadOnlySequence<byte>>(), default), Times.Never);
            database.Verify(cache => cache.StringGet(key, CommandFlags.PreferReplica), Times.Once);
            CollectionAssert.AreNotEqual(deserializedByte, destination.WrittenMemory.ToArray());
            Assert.IsTrue(destination.FreeCapacity == destination.WrittenSpan.Length);
        }

        [TestMethod]
        public void GetFromCache_SerializationException_ReturnsFalse()
        {
            // Arrange
            var key = "testKey";
            var data = new byte[] { 1, 2, 3 };
            _memCache.Setup(m => m.TryGet(key, out data)).Returns(true);
            Exception exception = new("Serialization failed.");

            _serializer.Setup(m => m.Deserialize<byte[]>(It.IsAny<ReadOnlySequence<byte>>(), default)).Throws(exception);
            var destination = new ArrayBufferWriter<byte>();

            // Act and Assert
            Assert.ThrowsException<Exception>(() => _cache.GetFromCache(key, destination));
            _serializer.Verify(m => m.Deserialize<byte[]>(It.IsAny<ReadOnlySequence<byte>>(), default), Times.Once);
        }

        #endregion
        #region SET

        /*
        [DataTestMethod]
        [DataRow("testKey")]
        [DataRow("payload123")]
        [DataRow("levelupsoftware")]
        [TestMethod]
        public void SetInCache_UseMemoryCacheTrue_StringSetReturnsTrue()
        {
            // Arrange
            // ...

            // Act
            var result = _cache.SetInCache(key, value, options);

            // Assert
            Assert.IsTrue(result);
            // ...
        }

        [DataTestMethod]
        [DataRow("testKey")]
        [DataRow("payload123")]
        [DataRow("levelupsoftware")]
        [TestMethod]
        public void SetInCache_UseMemoryCacheTrue_StringSetReturnsFalse()
        {
            // Arrange
            // ...

            // Act
            var result = _cache.SetInCache(key, value, options);

            // Assert
            Assert.IsFalse(result);
            // ...
        }

        [DataTestMethod]
        [DataRow("testKey")]
        [DataRow("payload123")]
        [DataRow("levelupsoftware")]
        [TestMethod]
        public void SetInCache_UseMemoryCacheFalse_StringSetReturnsTrue()
        {
            // Arrange
            // ...

            // Act
            var result = _cache.SetInCache(key, value, options);

            // Assert
            Assert.IsTrue(result);
            // ...
        }

        [DataTestMethod]
        [DataRow("testKey")]
        [DataRow("payload123")]
        [DataRow("levelupsoftware")]
        [TestMethod]
        public void SetInCache_UseMemoryCacheFalse_StringSetReturnsFalse()
        {
            // Arrange
            // ...

            // Act
            var result = _cache.SetInCache(key, value, options);

            // Assert
            Assert.IsFalse(result);
            // ...
        }

        [DataTestMethod]
        [DataRow("testKey")]
        [DataRow("payload123")]
        [DataRow("levelupsoftware")]
        [TestMethod]
        public void SetInCache_OptionsNull_StringSetReturnsTrue()
        {
            // Arrange
            // ...

            // Act
            var result = _cache.SetInCache(key, value, null);

            // Assert
            Assert.IsTrue(result);
            // ...
        }

        [DataTestMethod]
        [DataRow("testKey")]
        [DataRow("payload123")]
        [DataRow("levelupsoftware")]
        [TestMethod]
        public void SetInCache_OptionsNull_StringSetReturnsFalse()
        {
            // Arrange
            // ...

            // Act
            var result = _cache.SetInCache(key, value, null);

            // Assert
            Assert.IsFalse(result);
            // ...
        }
        */

        #endregion
        #region REFRESH

        #endregion
        #region REMOVE

        #endregion
    }
}
