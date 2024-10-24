using lvlup.DataFerry.Caches.Abstractions;
using lvlup.DataFerry.Properties;
using Moq;
using StackExchange.Redis;
using System.Buffers;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using lvlup.DataFerry.Tests.TestModels;
using System.Text.Json;
using lvlup.DataFerry.Orchestrators;
using Microsoft.Extensions.Caching.Distributed;

namespace lvlup.DataFerry.Tests
{
    [TestClass]
    public class SparseDistributedCacheTests
    {
        private CacheOrchestrator _cache = default!;
        private Mock<IConnectionMultiplexer> _redis = default!;
        private Mock<ILfuMemCache<string, byte[]>> _memCache = default!;
        private Mock<ILogger<CacheOrchestrator>> _logger = default!;
        private readonly IOptions<CacheSettings> _settings = Options.Create(new CacheSettings
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
        
        [TestInitialize]
        public void Setup()
        {
            // Setup dependencies
            _redis = new Mock<IConnectionMultiplexer>();
            _memCache = new Mock<ILfuMemCache<string, byte[]>>();
            _logger = new Mock<ILogger<CacheOrchestrator>>();

            // Create the CacheOrchestrator instance
            _cache = new CacheOrchestrator(
                _redis.Object,
                _memCache.Object,
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
            byte[] serializedByte = JsonSerializer.SerializeToUtf8Bytes(expectedValue);

            _memCache.Setup(cache => cache.TryGet(key, out serializedByte)).Returns(true);
            var destination = new ArrayBufferWriter<byte>();

            // Act
            _cache.GetFromCache(key, destination);

            // Assert
            _memCache.Verify(cache => cache.TryGet(key, out serializedByte), Times.Once);
            CollectionAssert.AreEqual(serializedByte, destination.WrittenMemory.ToArray());
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
            byte[] serializedByte = JsonSerializer.SerializeToUtf8Bytes(expectedValue);

            RedisValue redisSerializedValue = (RedisValue)serializedByte;
            byte[]? nullValue = default;
            _memCache.Setup(cache => cache.TryGet(key, out nullValue)).Returns(false);
            Mock<IDatabase> database = new();
            database.Setup(cache => cache.StringGet(key, CommandFlags.PreferReplica)).Returns(redisSerializedValue);
            _redis.Setup(db => db.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(database.Object);
            var destination = new ArrayBufferWriter<byte>();

            // Act: Call the method under test
            _cache.GetFromCache(key, destination);

            // Assert: Verify the result
            _memCache.Verify(cache => cache.TryGet(key, out nullValue), Times.Once);
            database.Verify(cache => cache.StringGet(key, CommandFlags.PreferReplica), Times.Once);
            CollectionAssert.AreEqual(serializedByte, destination.WrittenMemory.ToArray());
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
            byte[] serializedByte = JsonSerializer.SerializeToUtf8Bytes(expectedValue);

            byte[]? nullValue = default;
            _memCache.Setup(cache => cache.TryGet(key, out nullValue)).Returns(false);
            Mock<IDatabase> database = new();
            database.Setup(cache => cache.StringGet(key, CommandFlags.PreferReplica)).Returns(RedisValue.Null);
            _redis.Setup(db => db.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(database.Object);
            var destination = new ArrayBufferWriter<byte>();

            // Act: Call the method under test
            _cache.GetFromCache(key, destination);

            // Assert: Verify the result
            _memCache.Verify(cache => cache.TryGet(key, out nullValue), Times.Once);
            database.Verify(cache => cache.StringGet(key, CommandFlags.PreferReplica), Times.Once);
            CollectionAssert.AreNotEqual(serializedByte, destination.WrittenMemory.ToArray());
            Assert.IsTrue(destination.FreeCapacity == destination.WrittenSpan.Length);
        }

        #endregion
        #region SET

        [DataTestMethod]
        [DataRow("testKey")]
        [DataRow("payload123")]
        [DataRow("levelupsoftware")]
        [TestMethod]
        public void SetInCache_SetsValuesInCaches_AndReturnsTrue(string key)
        {
            // Arrange
            Payload expectedValue = TestUtils.CreatePayloadWithInput(key);
            byte[] serializedValue = JsonSerializer.SerializeToUtf8Bytes(expectedValue);

            var options = new DistributedCacheEntryOptions { SlidingExpiration = TimeSpan.FromMinutes(5) };
            Mock<IDatabase> database = new();
            database.Setup(cache => cache.StringSet(key, serializedValue, TimeSpan.FromMinutes(5), It.IsAny<When>())).Returns(true);
            _redis.Setup(db => db.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(database.Object);

            // Act
            _cache.SetInCache(key, serializedValue, options);

            // Assert
            _memCache.Verify(x => x.CheckBackplane(), Times.Once);
            _memCache.Verify(x => x.AddOrUpdate(key, serializedValue, TimeSpan.FromMinutes(5)), Times.Once);
            database.Verify(x => x.StringSet(key, serializedValue, TimeSpan.FromMinutes(5), It.IsAny<When>()), Times.Once);
        }

        
        [TestMethod]
        public void SetInCache_WhenCalled_ShouldCallDatabaseStringSet()
        {
            // Arrange
            var key = "test-key";
            var value = new ReadOnlySequence<byte>(new byte[0]);
            var options = new DistributedCacheEntryOptions();
            var mockDatabase = new Mock<IDatabase>();
            _redis.Setup(x => x.GetDatabase()).Returns(mockDatabase.Object);

            // Act
            _cache.SetInCache(key, value, options);

            // Assert
            mockDatabase.Verify(x => x.StringSet(key, It.IsAny<byte[]>(), It.IsAny<TimeSpan>()), Times.Once);
        }

        [TestMethod]
        public void SetInCache_WhenCalled_ReturnsExpectedResult()
        {
            // Arrange
            var key = "test-key";
            var value = new ReadOnlySequence<byte>(new byte[0]);
            var options = new DistributedCacheEntryOptions();
            var mockDatabase = new Mock<IDatabase>();
            mockDatabase.Setup(x => x.StringSet(It.IsAny<RedisKey>(), It.IsAny<RedisValue>(), It.IsAny<TimeSpan>())).Returns(true);
            _redis.Setup(x => x.GetDatabase()).Returns(mockDatabase.Object);

            // Act
            var result = _cache.SetInCache(key, value, options);

            // Assert
            Assert.IsTrue(result);
        }
        */

        #endregion
        #region REFRESH

        #endregion
        #region REMOVE

        #endregion
    }
}
