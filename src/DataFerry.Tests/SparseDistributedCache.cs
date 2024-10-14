using lvlup.DataFerry.Caches.Abstractions;
using lvlup.DataFerry.Caches;
using lvlup.DataFerry.Collections;
using lvlup.DataFerry.Properties;
using lvlup.DataFerry.Serializers.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using StackExchange.Redis;
using System;
using System.Buffers;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace lvlup.DataFerry.Tests
{
    /*
    [TestClass]
    public class SparseDistributedCacheTests
    {
        private Mock<IConnectionMultiplexer> _cacheMock;
        private Mock<IFastMemCache<string, byte[]>> _memCacheMock;
        private Mock<StackArrayPool<byte>> _arrayPoolMock;
        private Mock<IDataFerrySerializer> _serializerMock;
        private CacheSettings _settings;
        private Mock<ILogger<SparseDistributedCache>> _loggerMock;

        [TestInitialize]
        public void Setup()
        {
            _cacheMock = new Mock<IConnectionMultiplexer>();
            _memCacheMock = new Mock<IFastMemCache<string, byte[]>>();
            _arrayPoolMock = new Mock<StackArrayPool<byte>>();
            _serializerMock = new Mock<IDataFerrySerializer>();
            _settings = new CacheSettings { UseMemoryCache = true }; // Enable memory cache for testing
            _loggerMock = new Mock<ILogger<SparseDistributedCache>>();
        }

        [TestMethod]
        public void GetFromCache_MemoryCacheHit_ReturnsTrue()
        {
            // Arrange
            var key = "testKey";
            var data = new byte[] { 1, 2, 3 };
            _memCacheMock.Setup(m => m.CheckBackplane()).Returns(true);
            _memCacheMock.Setup(m => m.TryGet(key, out data)).Returns(true);

            var destination = new ArrayBufferWriter<byte>();
            var cache = new SparseDistributedCache(
                _cacheMock.Object,
                _memCacheMock.Object,
                _arrayPoolMock.Object,
                _serializerMock.Object,
                Options.Create(_settings),
                _loggerMock.Object);

            // Act
            var result = cache.GetFromCache(key, destination);

            // Assert
            Assert.IsTrue(result);
            _serializerMock.Verify(s => s.Deserialize(It.IsAny<ReadOnlySequence<byte>>(), destination), Times.Once);
        }

        [TestMethod]
        public async Task GetFromCache_MemoryCacheMiss_DatabaseHit_ReturnsTrue()
        {
            // Arrange
            var key = "testKey";
            var db = new Mock<IDatabase>();
            var redisValue = new RedisValue(new byte[] { 1, 2, 3 });
            _cacheMock.Setup(c => c.GetDatabase()).Returns(db.Object);
            _memCacheMock.Setup(m => m.CheckBackplane()).Returns(true);
            _memCacheMock.Setup(m => m.TryGet(key, out It.Ref<byte[]?>.IsAny)).Returns(false);
            db.Setup(d => d.StringGetAsync(key, CommandFlags.PreferReplica)).ReturnsAsync(redisValue);

            var destination = new ArrayBufferWriter<byte>();
            var cache = new SparseDistributedCache(
                _cacheMock.Object,
                _memCacheMock.Object,
                _arrayPoolMock.Object,
                _serializerMock.Object,
                Options.Create(_settings),
                _loggerMock.Object);

            // Act
            var result = cache.GetFromCache(key, destination);

            // Assert
            Assert.IsTrue(result);
            _serializerMock.Verify(s => s.Deserialize(It.IsAny<ReadOnlySequence<byte>>(), destination), Times.Once);
        }

        // Add more tests for other scenarios like:
        // - Memory cache disabled
        // - Memory cache miss and database miss
        // - Exceptions during database access
    }
    */
}
