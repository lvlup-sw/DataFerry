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
    public class CacheOrchestratorTests
    {
        private CacheOrchestrator _cache = default!;
        private Mock<IConnectionMultiplexer> _redis = default!;
        private Mock<IMemCache<string, byte[]>> _memCache = default!;
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
            _memCache = new Mock<IMemCache<string, byte[]>>();
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

        #region SYNCHRONOUS OPERATIONS
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
            database.Setup(cache => cache.StringSet(key, serializedValue, TimeSpan.FromMinutes(5), false, When.Always, CommandFlags.None)).Returns(true);
            _redis.Setup(db => db.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(database.Object);

            // Act
            bool result = _cache.SetInCache(key, serializedValue, options);

            // Assert
            Assert.IsTrue(result);
            _memCache.Verify(x => x.CheckBackplane(), Times.Once);
            _memCache.Verify(x => x.AddOrUpdate(key, serializedValue, TimeSpan.FromMinutes(_settings.Value.InMemoryAbsoluteExpiration)), Times.Once);
            database.Verify(x => x.StringSet(key, serializedValue, TimeSpan.FromMinutes(5), It.IsAny<bool>(), It.IsAny<When>(), It.IsAny<CommandFlags>()), Times.Once);
        }

        [DataTestMethod]
        [DataRow("testKey")]
        [DataRow("payload123")]
        [DataRow("levelupsoftware")]
        [TestMethod]
        public void SetInCache_RemoteCacheOperationFails_AndReturnsFalse(string key)
        {
            // Arrange
            Payload expectedValue = TestUtils.CreatePayloadWithInput(key);
            byte[] serializedValue = JsonSerializer.SerializeToUtf8Bytes(expectedValue);

            var options = new DistributedCacheEntryOptions { SlidingExpiration = TimeSpan.FromMinutes(5) };
            Mock<IDatabase> database = new();
            database.Setup(cache => cache.StringSet(key, serializedValue, TimeSpan.FromMinutes(5), false, When.Always, CommandFlags.None)).Returns(false);
            _redis.Setup(db => db.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(database.Object);

            // Act
            bool result = _cache.SetInCache(key, serializedValue, options);

            // Assert
            Assert.IsFalse(result);
            _memCache.Verify(x => x.CheckBackplane(), Times.Once);
            _memCache.Verify(x => x.AddOrUpdate(key, serializedValue, TimeSpan.FromMinutes(_settings.Value.InMemoryAbsoluteExpiration)), Times.Once);
            database.Verify(x => x.StringSet(key, serializedValue, TimeSpan.FromMinutes(5), It.IsAny<bool>(), It.IsAny<When>(), It.IsAny<CommandFlags>()), Times.Once);
        }

        #endregion
        #region REFRESH

        [DataTestMethod]
        [DataRow("testKey")]
        [DataRow("payload123")]
        [DataRow("levelupsoftware")]
        [TestMethod]
        public void RefreshInCache_RefreshesValuesInCaches_AndReturnsTrue(string key)
        {
            // Arrange
            TimeSpan ttl = TimeSpan.FromMinutes(60);

            Mock<IDatabase> database = new();
            database.Setup(cache => cache.KeyExpire(key, ttl, ExpireWhen.Always, CommandFlags.None)).Returns(true);
            _redis.Setup(db => db.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(database.Object);

            // Act
            bool result = _cache.RefreshInCache(key, ttl);

            // Assert
            Assert.IsTrue(result);
            _memCache.Verify(x => x.CheckBackplane(), Times.Once);
            _memCache.Verify(x => x.Refresh(key, TimeSpan.FromMinutes(_settings.Value.InMemoryAbsoluteExpiration)), Times.Once);
            database.Verify(x => x.KeyExpire(key, ttl, ExpireWhen.Always, It.IsAny<CommandFlags>()), Times.Once);
        }

        [DataTestMethod]
        [DataRow("testKey")]
        [DataRow("payload123")]
        [DataRow("levelupsoftware")]
        [TestMethod]
        public void RefreshInCache_RemoteCacheOperationFails_AndReturnsFalse(string key)
        {
            // Arrange
            TimeSpan ttl = TimeSpan.FromMinutes(60);

            Mock<IDatabase> database = new();
            database.Setup(cache => cache.KeyExpire(key, ttl, ExpireWhen.Always, CommandFlags.None)).Returns(false);
            _redis.Setup(db => db.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(database.Object);

            // Act
            bool result = _cache.RefreshInCache(key, ttl);

            // Assert
            Assert.IsFalse(result);
            _memCache.Verify(x => x.CheckBackplane(), Times.Once);
            _memCache.Verify(x => x.Refresh(key, TimeSpan.FromMinutes(_settings.Value.InMemoryAbsoluteExpiration)), Times.Once);
            database.Verify(x => x.KeyExpire(key, ttl, ExpireWhen.Always, It.IsAny<CommandFlags>()), Times.Once);
        }

        #endregion
        #region REMOVE

        [DataTestMethod]
        [DataRow("testKey")]
        [DataRow("payload123")]
        [DataRow("levelupsoftware")]
        [TestMethod]
        public void RemoveInCache_RemovesValuesInCaches_AndReturnsTrue(string key)
        {
            // Arrange
            Mock<IDatabase> database = new();
            database.Setup(cache => cache.KeyDelete(key, CommandFlags.None)).Returns(true);
            _redis.Setup(db => db.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(database.Object);

            // Act
            bool result = _cache.RemoveFromCache(key);

            // Assert
            Assert.IsTrue(result);
            _memCache.Verify(x => x.CheckBackplane(), Times.Once);
            _memCache.Verify(x => x.Remove(key), Times.Once);
            database.Verify(x => x.KeyDelete(key, It.IsAny<CommandFlags>()), Times.Once);
        }

        [DataTestMethod]
        [DataRow("testKey")]
        [DataRow("payload123")]
        [DataRow("levelupsoftware")]
        [TestMethod]
        public void RemoveInCache_RemoteCacheOperationFails_AndReturnsFalse(string key)
        {
            // Arrange
            Mock<IDatabase> database = new();
            database.Setup(cache => cache.KeyDelete(key, CommandFlags.None)).Returns(false);
            _redis.Setup(db => db.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(database.Object);

            // Act
            bool result = _cache.RemoveFromCache(key);

            // Assert
            Assert.IsFalse(result);
            _memCache.Verify(x => x.CheckBackplane(), Times.Once);
            _memCache.Verify(x => x.Remove(key), Times.Once);
            database.Verify(x => x.KeyDelete(key, It.IsAny<CommandFlags>()), Times.Once);
        }

        #endregion
        #endregion

        #region ASYNCHRONOUS OPERATIONS
        #region GET ASYNC

        [DataTestMethod]
        [DataRow("testKey")]
        [DataRow("payload123")]
        [DataRow("levelupsoftware")]
        [TestMethod]
        public async Task GetFromCacheAsync_KeyInMemCache_ReturnsValue(string key)
        {
            // Arrange
            Payload expectedValue = TestUtils.CreatePayloadWithInput(key);
            byte[] serializedByte = JsonSerializer.SerializeToUtf8Bytes(expectedValue);

            _memCache.Setup(cache => cache.TryGet(key, out serializedByte)).Returns(true);
            var destination = new ArrayBufferWriter<byte>();

            // Act
            await _cache.GetFromCacheAsync(key, destination);

            // Assert
            _memCache.Verify(cache => cache.TryGet(key, out serializedByte), Times.Once);
            CollectionAssert.AreEqual(serializedByte, destination.WrittenMemory.ToArray());
        }

        [DataTestMethod]
        [DataRow("testKey")]
        [DataRow("payload123")]
        [DataRow("levelupsoftware")]
        [TestMethod]
        public async Task GetFromCacheAsync_KeyNotInMemCache_ButInRedis_ReturnsValue(string key)
        {
            // Arrange
            Payload expectedValue = TestUtils.CreatePayloadWithInput(key);
            byte[] serializedByte = JsonSerializer.SerializeToUtf8Bytes(expectedValue);

            RedisValue redisSerializedValue = (RedisValue)serializedByte;
            byte[]? nullValue = default;
            _memCache.Setup(cache => cache.TryGet(key, out nullValue)).Returns(false);
            Mock<IDatabase> database = new();
            database.Setup(cache => cache.StringGetAsync(key, CommandFlags.PreferReplica)).ReturnsAsync(redisSerializedValue);
            _redis.Setup(db => db.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(database.Object);
            var destination = new ArrayBufferWriter<byte>();

            // Act: Call the method under test
            await _cache.GetFromCacheAsync(key, destination);

            // Assert: Verify the result
            _memCache.Verify(cache => cache.TryGet(key, out nullValue), Times.Once);
            database.Verify(cache => cache.StringGetAsync(key, CommandFlags.PreferReplica), Times.Once);
            CollectionAssert.AreEqual(serializedByte, destination.WrittenMemory.ToArray());
        }

        [DataTestMethod]
        [DataRow("testKey")]
        [DataRow("payload123")]
        [DataRow("levelupsoftware")]
        [TestMethod]
        public async Task GetFromCacheAsync_KeyNotFound_ReturnsNull(string key)
        {
            // Arrange
            Payload expectedValue = TestUtils.CreatePayloadWithInput(key);
            byte[] serializedByte = JsonSerializer.SerializeToUtf8Bytes(expectedValue);

            byte[]? nullValue = default;
            _memCache.Setup(cache => cache.TryGet(key, out nullValue)).Returns(false);
            Mock<IDatabase> database = new();
            database.Setup(cache => cache.StringGetAsync(key, CommandFlags.PreferReplica)).ReturnsAsync(RedisValue.Null);
            _redis.Setup(db => db.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(database.Object);
            var destination = new ArrayBufferWriter<byte>();

            // Act: Call the method under test
            await _cache.GetFromCacheAsync(key, destination);

            // Assert: Verify the result
            _memCache.Verify(cache => cache.TryGet(key, out nullValue), Times.Once);
            database.Verify(cache => cache.StringGetAsync(key, CommandFlags.PreferReplica), Times.Once);
            CollectionAssert.AreNotEqual(serializedByte, destination.WrittenMemory.ToArray());
            Assert.IsTrue(destination.FreeCapacity == destination.WrittenSpan.Length);
        }

        #endregion
        #region SET ASYNC

        [DataTestMethod]
        [DataRow("testKey")]
        [DataRow("payload123")]
        [DataRow("levelupsoftware")]
        [TestMethod]
        public async Task SetInCacheAsync_SetsValuesInCaches_AndReturnsTrue(string key)
        {
            // Arrange
            Payload expectedValue = TestUtils.CreatePayloadWithInput(key);
            byte[] serializedValue = JsonSerializer.SerializeToUtf8Bytes(expectedValue);

            var options = new DistributedCacheEntryOptions { SlidingExpiration = TimeSpan.FromMinutes(5) };
            Mock<IDatabase> database = new();
            database.Setup(cache => cache.StringSetAsync(key, serializedValue, TimeSpan.FromMinutes(5), false, When.Always, CommandFlags.None)).ReturnsAsync(true);
            _redis.Setup(db => db.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(database.Object);

            // Act
            bool result = await _cache.SetInCacheAsync(key, serializedValue, options);

            // Assert
            Assert.IsTrue(result);
            _memCache.Verify(x => x.CheckBackplane(), Times.Once);
            _memCache.Verify(x => x.AddOrUpdate(key, serializedValue, TimeSpan.FromMinutes(_settings.Value.InMemoryAbsoluteExpiration)), Times.Once);
            database.Verify(x => x.StringSetAsync(key, serializedValue, TimeSpan.FromMinutes(5), It.IsAny<bool>(), It.IsAny<When>(), It.IsAny<CommandFlags>()), Times.Once);
        }

        [DataTestMethod]
        [DataRow("testKey")]
        [DataRow("payload123")]
        [DataRow("levelupsoftware")]
        [TestMethod]
        public async Task SetInCacheAsync_RemoteCacheOperationFails_AndReturnsFalse(string key)
        {
            // Arrange
            Payload expectedValue = TestUtils.CreatePayloadWithInput(key);
            byte[] serializedValue = JsonSerializer.SerializeToUtf8Bytes(expectedValue);

            var options = new DistributedCacheEntryOptions { SlidingExpiration = TimeSpan.FromMinutes(5) };
            Mock<IDatabase> database = new();
            database.Setup(cache => cache.StringSetAsync(key, serializedValue, TimeSpan.FromMinutes(5), false, When.Always, CommandFlags.None)).ReturnsAsync(false);
            _redis.Setup(db => db.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(database.Object);

            // Act
            bool result = await _cache.SetInCacheAsync(key, serializedValue, options);

            // Assert
            Assert.IsFalse(result);
            _memCache.Verify(x => x.CheckBackplane(), Times.Once);
            _memCache.Verify(x => x.AddOrUpdate(key, serializedValue, TimeSpan.FromMinutes(_settings.Value.InMemoryAbsoluteExpiration)), Times.Once);
            database.Verify(x => x.StringSetAsync(key, serializedValue, TimeSpan.FromMinutes(5), It.IsAny<bool>(), It.IsAny<When>(), It.IsAny<CommandFlags>()), Times.Once);
        }

        #endregion
        #region REFRESH ASYNC

        [DataTestMethod]
        [DataRow("testKey")]
        [DataRow("payload123")]
        [DataRow("levelupsoftware")]
        [TestMethod]
        public async Task RefreshInCacheAsync_RefreshesValuesInCaches_AndReturnsTrue(string key)
        {
            // Arrange
            TimeSpan ttl = TimeSpan.FromMinutes(60);

            Mock<IDatabase> database = new();
            database.Setup(cache => cache.KeyExpireAsync(key, ttl, ExpireWhen.Always, CommandFlags.None)).ReturnsAsync(true);
            _redis.Setup(db => db.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(database.Object);

            // Act
            bool result = await _cache.RefreshInCacheAsync(key, ttl);

            // Assert
            Assert.IsTrue(result);
            _memCache.Verify(x => x.CheckBackplane(), Times.Once);
            _memCache.Verify(x => x.Refresh(key, TimeSpan.FromMinutes(_settings.Value.InMemoryAbsoluteExpiration)), Times.Once);
            database.Verify(x => x.KeyExpireAsync(key, ttl, ExpireWhen.Always, It.IsAny<CommandFlags>()), Times.Once);
        }

        [DataTestMethod]
        [DataRow("testKey")]
        [DataRow("payload123")]
        [DataRow("levelupsoftware")]
        [TestMethod]
        public async Task RefreshInCacheAsync_RemoteCacheOperationFails_AndReturnsFalse(string key)
        {
            // Arrange
            TimeSpan ttl = TimeSpan.FromMinutes(60);

            Mock<IDatabase> database = new();
            database.Setup(cache => cache.KeyExpireAsync(key, ttl, ExpireWhen.Always, CommandFlags.None)).ReturnsAsync(false);
            _redis.Setup(db => db.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(database.Object);

            // Act
            bool result = await _cache.RefreshInCacheAsync(key, ttl);

            // Assert
            Assert.IsFalse(result);
            _memCache.Verify(x => x.CheckBackplane(), Times.Once);
            _memCache.Verify(x => x.Refresh(key, TimeSpan.FromMinutes(_settings.Value.InMemoryAbsoluteExpiration)), Times.Once);
            database.Verify(x => x.KeyExpireAsync(key, ttl, ExpireWhen.Always, It.IsAny<CommandFlags>()), Times.Once);
        }

        #endregion
        #region REMOVE ASYNC

        [DataTestMethod]
        [DataRow("testKey")]
        [DataRow("payload123")]
        [DataRow("levelupsoftware")]
        [TestMethod]
        public async Task RemoveInCacheAsync_RemovesValuesInCaches_AndReturnsTrue(string key)
        {
            // Arrange
            Mock<IDatabase> database = new();
            database.Setup(cache => cache.KeyDeleteAsync(key, CommandFlags.None)).ReturnsAsync(true);
            _redis.Setup(db => db.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(database.Object);

            // Act
            bool result = await _cache.RemoveFromCacheAsync(key);

            // Assert
            Assert.IsTrue(result);
            _memCache.Verify(x => x.CheckBackplane(), Times.Once);
            _memCache.Verify(x => x.Remove(key), Times.Once);
            database.Verify(x => x.KeyDeleteAsync(key, It.IsAny<CommandFlags>()), Times.Once);
        }

        [DataTestMethod]
        [DataRow("testKey")]
        [DataRow("payload123")]
        [DataRow("levelupsoftware")]
        [TestMethod]
        public async Task RemoveInCacheAsync_RemoteCacheOperationFails_AndReturnsFalse(string key)
        {
            // Arrange
            Mock<IDatabase> database = new();
            database.Setup(cache => cache.KeyDeleteAsync(key, CommandFlags.None)).ReturnsAsync(false);
            _redis.Setup(db => db.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(database.Object);

            // Act
            bool result = await _cache.RemoveFromCacheAsync(key);

            // Assert
            Assert.IsFalse(result);
            _memCache.Verify(x => x.CheckBackplane(), Times.Once);
            _memCache.Verify(x => x.Remove(key), Times.Once);
            database.Verify(x => x.KeyDeleteAsync(key, It.IsAny<CommandFlags>()), Times.Once);
        }

        #endregion
        #endregion

        #region ASYNCHRONOUS BATCH OPERATIONS
        #region GET BATCH ASYNC

        #endregion
        #endregion
    }
}
