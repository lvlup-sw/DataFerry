using lvlup.DataFerry.Algorithms;
using lvlup.DataFerry.Tests.TestModels;

namespace lvlup.DataFerry.Tests
{
    [TestClass]
    public class CountMinSketchTests
    {
        private CountMinSketch<Payload> _cmSketch = default!;
        private const int DefaultMaxSize = 1000;

        [TestInitialize]
        public void Initialize()
        {
            _cmSketch = new CountMinSketch<Payload>(DefaultMaxSize);
        }

        [TestCleanup]
        public void Cleanup()
        {
            _cmSketch = default!;
        }

        [TestMethod]
        public void Insert_IncreasesCount()
        {
            // Arrange
            var payload = TestUtils.CreatePayloadIdentical();

            // Act
           _cmSketch.Insert(payload);

            // Assert
            Assert.IsTrue(_cmSketch.Query(payload) > 0);
        }

        [TestMethod]
        public void Query_ReturnsApproximateCount()
        {
            // Arrange
            var payload = TestUtils.CreatePayloadIdentical();
            var count = 100;

            // Act
            for (int i = 0; i < count; i++)
            {
                _cmSketch.Insert(payload);
            }

            // Assert
            var result = _cmSketch.Query(payload);
            Assert.IsTrue(result >= count);
        }

        [TestMethod]
        public void Query_ReturnsZeroForNonexistentItem()
        {
            // Arrange
            var payload = TestUtils.CreatePayloadRandom();

            // Act
            var result = _cmSketch.Query(payload);

            // Assert
            Assert.AreEqual(0, result);
        }

        [TestMethod]
        public void CalculateOptimalDimensions_ReturnsCorrectValues()
        {
            // Arrange
            var maxSize = 1000;

            // Act
            var (width, numHashes) = _cmSketch.CalculateOptimalDimensions(maxSize);

            // Assert
            Assert.AreEqual(271829, width);
            Assert.AreEqual(5, numHashes);
        }

        [TestMethod]
        public void Insert_MultipleItems_CountsAreIndependent()
        {
            // Arrange
            var payload1 = TestUtils.CreatePayloadRandom();
            var payload2 = TestUtils.CreatePayloadRandom();

            // Act
            _cmSketch.Insert(payload1);
            _cmSketch.Insert(payload1);
            _cmSketch.Insert(payload2);

            // Assert
            Assert.AreEqual(2, _cmSketch.Query(payload1));
            Assert.AreEqual(1, _cmSketch.Query(payload2));
        }

        [TestMethod]
        public void LargePayloadInsertions_DoesNotThrowExceptions()
        {
            // Arrange
            int range = 1000;

            // Act
            for (int i = 0; i < range; i++)
            {
                _cmSketch.Insert(TestUtils.CreateLargePayload());
            }

            // Assert
            // No exceptions
        }

        [TestMethod]
        public void Insert_ManyItems_ProbabilisticFrequencyIsAccurate()
        {
            // Arrange
            var range = 100000;
            var random = new Random();
            var itemCounts = new Dictionary<Payload, int>();

            // Generate test data with varying frequencies
            for (int i = 0; i < range; i++)
            {
                var item = TestUtils.CreatePayloadWithInput($"item_{random.Next(range)}");
                _cmSketch.Insert(item);
                itemCounts.TryAdd(item, 0);
                itemCounts[item]++;
            }

            // Act & Assert
            foreach (var (item, trueCount) in itemCounts)
            {
                var estimatedCount = _cmSketch.Query(item);

                // Due to the probabilistic nature, we can't expect exact counts
                // Instead, we check if the estimated count is within a reasonable range
                Assert.IsTrue(estimatedCount >= trueCount);
            }
        }
    }
}
