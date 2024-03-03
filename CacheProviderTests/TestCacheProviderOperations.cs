using Xunit;
using Moq;
using CacheProvider.Providers;
using CacheProvider.Caches;
using MockCachingOperation.Process;
using StackExchange.Redis;

namespace CacheProviderTests
{
    public class TestCacheProviderOperations
    {
        [Fact]
        public async Task CheckCacheAsync_ItemNotInCache_ReturnsItemFromRealProvider()
        {
            // Arrange
            var mockRealProvider = new Mock<IRealProvider<Payload>>();
            var mockConnection = new Mock<ConnectionMultiplexer>();
            var settings = new CacheSettings();
            var cacheProvider = new CacheProvider<Payload>(mockRealProvider.Object, CacheType.Distributed, settings, mockConnection.Object);
            var payload = new Payload { Identifier = "test", Data = ["testData"] };

            mockRealProvider.Setup(p => p.GetItemAsync(payload)).ReturnsAsync(payload);

            // Act
            var result = await cacheProvider.CheckCacheAsync(payload, payload.Identifier);

            // Assert
            Assert.Equal(payload, result);
            mockRealProvider.Verify(p => p.GetItemAsync(payload), Times.Once);
        }

        [Fact]
        public void CheckCache_ItemNotInCache_ReturnsItemFromRealProvider()
        {
            // Arrange
            var mockRealProvider = new Mock<IRealProvider<Payload>>();
            var settings = new CacheSettings();
            var cacheProvider = new CacheProvider<Payload>(mockRealProvider.Object, CacheType.Local, settings);
            var payload = new Payload { Identifier = "test", Data = ["testData"] };

            mockRealProvider.Setup(p => p.GetItem(payload)).Returns(payload);

            // Act
            var result = cacheProvider.CheckCache(payload, payload.Identifier);

            // Assert
            Assert.Equal(payload, result);
            mockRealProvider.Verify(p => p.GetItem(payload), Times.Once);
        }
    }
}