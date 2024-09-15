using DataFerry.Properties;
using DataFerry.Caches;
using StackExchange.Redis;
using PowerUp.Caching.Interfaces;
using Moq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace DataFerry.Tests
{
    [TestClass]
    public class DistributedCacheTests
    {
        private DistributedCache<Payload> _cache;
        private Mock<IConnectionMultiplexer> _redis;
        private Mock<IFastMemCache<string, string>> _memCache;
        private IOptions<CacheSettings> _settings;
        private Mock<ILogger> _logger;

        [TestInitialize]
        public void Setup()
        {
            // Mocking dependencies
            _redis = new Mock<IConnectionMultiplexer>();
            _memCache = new Mock<IFastMemCache<string, string>>();
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
            _logger = new Mock<ILogger>();

            // Creating the DistributedCache instance
            _cache = new DistributedCache<Payload>(
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
        // Test 1: Key exists in memCache
        [DataTestMethod]
        [DataRow("testKey")]
        [DataRow("payload123")]
        [DataRow("levelupsoftware")]
        [TestMethod]
        public async Task GetFromCacheAsync_KeyInMemCache_ReturnsValue(string key)
        {
            // Arrange
            Payload expectedValue = TestUtils.CreatePayloadWithInput(key);
            string serializedValue = JsonSerializer.Serialize(expectedValue);
            _memCache.Setup(cache => cache.TryGet(key, out serializedValue)).Returns(true);

            // Act
            var result = await _cache.GetFromCacheAsync(key);

            // Assert
            Assert.IsNotNull(result);
            Assert.IsTrue(TestUtils.Compares(expectedValue, result));
            _memCache.Verify(cache => cache.TryGet(key, out serializedValue), Times.Once);
        }


        // Test 2: Key not in memCache, but exists in Redis
        [DataTestMethod]
        [DataRow("testKey")]
        [DataRow("payload123")]
        [DataRow("levelupsoftware")]
        [TestMethod]
        public async Task GetFromCacheAsync_KeyNotInMemCache_ButInRedis_ReturnsValue(string key)
        {
            // Arrange
            Payload expectedValue = TestUtils.CreatePayloadWithInput(key);
            string serializedValue = JsonSerializer.Serialize(expectedValue);
            RedisValue redisSerializedValue = new(serializedValue);
            string? memValue = default;
            _memCache.Setup(cache => cache.TryGet(key, out memValue)).Returns(false);
            Mock<IDatabase> database = new();
            database.Setup(cache => cache.StringGetAsync(key, CommandFlags.PreferReplica)).ReturnsAsync(redisSerializedValue);
            _redis.Setup(db => db.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(database.Object);

            // Act: Call the method under test
            var result = await _cache.GetFromCacheAsync(key);

            // Assert: Verify the result
            Assert.IsNotNull(result);
            Assert.IsTrue(TestUtils.Compares(expectedValue, result));
            database.Verify(cache => cache.StringGetAsync(key, CommandFlags.PreferReplica), Times.Once);
        }

        // Test 3: Key not found in either cache, returns null
        [DataTestMethod]
        [DataRow("testKey")]
        [DataRow("payload123")]
        [DataRow("levelupsoftware")]
        [TestMethod]
        public async Task GetFromCacheAsync_KeyNotFound_ReturnsNull(string key)
        {
            // Arrange
            string? nullValue = default;
            _memCache.Setup(cache => cache.TryGet(key, out nullValue)).Returns(false);
            Mock<IDatabase> database = new();
            database.Setup(cache => cache.StringGetAsync(key, CommandFlags.PreferReplica)).ReturnsAsync(nullValue);
            _redis.Setup(db => db.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(database.Object);

            // Act: Call the method under test
            var result = await _cache.GetFromCacheAsync(key);

            // Assert: Verify the result
            Assert.IsNull(result);
            database.Verify(cache => cache.StringGetAsync(key, CommandFlags.PreferReplica), Times.Once);
        }

        #endregion
        #region SET

        [DataTestMethod]
        [DataRow("testKey")]
        [DataRow("payload123")]
        [DataRow("levelupsoftware")]
        [TestMethod]
        public async Task SetInCacheAsync_ReturnsTrue_ForBothCaches(string key)
        {
            // Arrange
            Payload expectedValue = TestUtils.CreatePayloadWithInput(key);
            string serializedValue = JsonSerializer.Serialize(expectedValue);
            RedisValue redisSerializedValue = new(serializedValue);
            _memCache.Setup(cache => cache.AddOrUpdate(key, serializedValue, TimeSpan.FromMinutes(_settings.Value.InMemoryAbsoluteExpiration)));
            Mock<IDatabase> database = new();
            database.Setup(cache => cache.StringSetAsync(key, serializedValue, It.IsAny<TimeSpan>(), It.IsAny<bool>(), It.IsAny<When>(), It.IsAny<CommandFlags>())).ReturnsAsync(true);
            _redis.Setup(db => db.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(database.Object);

            // Act
            bool result = await _cache.SetInCacheAsync(key, expectedValue, TimeSpan.FromHours(_settings.Value.AbsoluteExpiration));

            // Assert
            Assert.IsTrue(result);
            _memCache.Verify(cache => cache.AddOrUpdate(key, serializedValue, TimeSpan.FromMinutes(_settings.Value.InMemoryAbsoluteExpiration)), Times.Once);
            database.Verify(cache => cache.StringSetAsync(key, serializedValue, It.IsAny<TimeSpan>(), It.IsAny<bool>(), It.IsAny<When>(), It.IsAny<CommandFlags>()), Times.Once);
        }

        [DataTestMethod]
        [DataRow("testKey")]
        [DataRow("payload123")]
        [DataRow("levelupsoftware")]
        [TestMethod]
        public async Task SetInCacheAsync_ReturnsFalse_ForRedisCache(string key)
        {
            // Arrange
            Payload expectedValue = TestUtils.CreatePayloadWithInput(key);
            string serializedValue = JsonSerializer.Serialize(expectedValue);
            RedisValue redisSerializedValue = new(serializedValue);
            _memCache.Setup(cache => cache.AddOrUpdate(key, serializedValue, TimeSpan.FromMinutes(_settings.Value.InMemoryAbsoluteExpiration)));
            Mock<IDatabase> database = new();
            database.Setup(cache => cache.StringSetAsync(key, serializedValue, It.IsAny<TimeSpan>(), It.IsAny<bool>(), It.IsAny<When>(), It.IsAny<CommandFlags>())).ReturnsAsync(false);
            _redis.Setup(db => db.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(database.Object);

            // Act
            bool result = await _cache.SetInCacheAsync(key, expectedValue, TimeSpan.FromHours(_settings.Value.AbsoluteExpiration));

            // Assert
            Assert.IsFalse(result);
            _memCache.Verify(cache => cache.AddOrUpdate(key, serializedValue, TimeSpan.FromMinutes(_settings.Value.InMemoryAbsoluteExpiration)), Times.Once);
            database.Verify(cache => cache.StringSetAsync(key, serializedValue, It.IsAny<TimeSpan>(), It.IsAny<bool>(), It.IsAny<When>(), It.IsAny<CommandFlags>()), Times.Once);
        }

        #endregion
        #region DEL

        [DataTestMethod]
        [DataRow("testKey")]
        [DataRow("payload123")]
        [DataRow("levelupsoftware")]
        [TestMethod]
        public async Task RemoveFromCacheAsync_ReturnsTrue_ForBothCaches(string key)
        {
            // Arrange
            _memCache.Setup(cache => cache.Remove(key));
            Mock<IDatabase> database = new();
            database.Setup(cache => cache.KeyDeleteAsync(key, It.IsAny<CommandFlags>())).ReturnsAsync(true);
            _redis.Setup(db => db.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(database.Object);

            // Act
            bool result = await _cache.RemoveFromCacheAsync(key);

            // Assert
            Assert.IsTrue(result);
            _memCache.Verify(cache => cache.Remove(key), Times.Once);
            database.Verify(cache => cache.KeyDeleteAsync(key, It.IsAny<CommandFlags>()), Times.Once);
        }

        [DataTestMethod]
        [DataRow("testKey")]
        [DataRow("payload123")]
        [DataRow("levelupsoftware")]
        [TestMethod]
        public async Task RemoveFromCacheAsync_ReturnsFalse_ForRedisCache(string key)
        {
            // Arrange
            _memCache.Setup(cache => cache.Remove(key));
            Mock<IDatabase> database = new();
            database.Setup(cache => cache.KeyDeleteAsync(key, It.IsAny<CommandFlags>())).ReturnsAsync(false);
            _redis.Setup(db => db.GetDatabase(It.IsAny<int>(), It.IsAny<object>())).Returns(database.Object);

            // Act
            bool result = await _cache.RemoveFromCacheAsync(key);

            // Assert
            Assert.IsFalse(result);
            _memCache.Verify(cache => cache.Remove(key), Times.Once);
            database.Verify(cache => cache.KeyDeleteAsync(key, It.IsAny<CommandFlags>()), Times.Once);
        }

        #endregion
    }
}