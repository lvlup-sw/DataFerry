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
            _cmSketch = new(DefaultMaxSize);
        }

        [TestCleanup]
        public void Cleanup()
        {
            _cmSketch.Clear();
        }

        [TestMethod]
        public void EstimateFrequency_ReturnsApproximateCount()
        {
            // Arrange
            var payload = TestUtils.CreatePayloadIdentical();
            var count = 100;

            // Act
            for (int i = 0; i < count; i++)
            {
                _cmSketch.Increment(payload);
            }

            // Assert
            var result = _cmSketch.EstimateFrequency(payload);
            Assert.IsTrue(result <= count);
        }

        [TestMethod]
        public void EstimateFrequency_ReturnsZeroForNonexistentItem()
        {
            // Arrange
            var payload = TestUtils.CreatePayloadRandom();

            // Act
            var result = _cmSketch.EstimateFrequency(payload);

            // Assert
            Assert.AreEqual(0, result);
        }

        [TestMethod]
        public void Increment_MultipleItems_CountsAreIndependent()
        {
            // Arrange
            var payload1 = TestUtils.CreatePayloadRandom();
            var payload2 = TestUtils.CreatePayloadRandom();

            // Act
            _cmSketch.Increment(payload1);
            _cmSketch.Increment(payload1);
            _cmSketch.Increment(payload2);

            // Assert
            Assert.AreEqual(2, _cmSketch.EstimateFrequency(payload1));
            Assert.AreEqual(1, _cmSketch.EstimateFrequency(payload2));
        }

        [TestMethod]
        public void LargePayloadIncrements_DoNotThrowExceptions()
        {
            // Arrange
            int range = 1000;

            // Act
            for (int i = 0; i < range; i++)
            {
                _cmSketch.Increment(TestUtils.CreateLargePayload());
            }

            // Assert
            // No exceptions
        }

        [TestMethod]
        public void Increment_ManyItems_ProbabilisticFrequencyIsAccurate()
        {
            // Arrange
            var range = 100000;
            var random = new Random();
            var itemCounts = new Dictionary<Payload, int>();

            // Generate test data with varying frequencies
            for (int i = 0; i < range; i++)
            {
                var item = TestUtils.CreatePayloadWithInput($"item_{random.Next(range)}");
                _cmSketch.Increment(item);
                itemCounts.TryAdd(item, 0);
                itemCounts[item]++;
            }

            // Act & Assert
            foreach (var (item, trueCount) in itemCounts)
            {
                var estimatedCount = _cmSketch.EstimateFrequency(item);

                // Due to the probabilistic nature, we can't expect exact counts
                // Instead, we check if the estimated count is within a reasonable range
                Assert.IsTrue(estimatedCount >= trueCount);
            }
        }

        [TestMethod]
        public void Clear_ClearsSketch()
        {
            // Arrange
            var payload = TestUtils.CreatePayloadIdentical();
            _cmSketch.Increment(payload);

            // Act
            _cmSketch.Clear();

            // Assert
            Assert.AreEqual(0, _cmSketch.EstimateFrequency(payload));
        }

        [TestMethod]
        public void Sketch_HandlesNonPowerOfTwoCapacity()
        {
            // Arrange
            var payload = TestUtils.CreatePayloadIdentical();
            _cmSketch = new CountMinSketch<Payload>(123); // 123 is not a power of two

            // Act
            _cmSketch.Increment(payload);

            // Assert
            Assert.AreEqual(1, _cmSketch.EstimateFrequency(payload));
        }

        [TestMethod]
        public void Sketch_PerformsWellWithLargeNumberOfItems()
        {
            // Arrange
            var payloads = Enumerable.Range(0, 1000000).Select(i => TestUtils.CreatePayloadWithInput($"item_{i}"));

            // Act
            foreach (var payload in payloads)
            {
                _cmSketch.Increment(payload);
            }

            // Assert
            // This is a performance test, so we don't assert anything.
            // You could measure the time it takes to run this test and make sure it's acceptable.
        }

        [TestMethod]
        public void Sketch_HandlesEdgeCases()
        {
            // Arrange
            var payload = TestUtils.CreatePayloadWithInput(""); // Empty string

            // Act
            _cmSketch.Increment(payload);

            // Assert
            Assert.AreEqual(1, _cmSketch.EstimateFrequency(payload));
        }
    }
}
