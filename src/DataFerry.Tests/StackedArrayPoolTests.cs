using lvlup.DataFerry.Collections;

namespace lvlup.DataFerry.Tests
{
    [TestClass]
    public class StackedArrayPoolTests
    {
        private StackArrayPool<int> _pool = default!;

        [TestInitialize]
        public void Setup()
        {
            _pool = new();
        }

        [TestMethod]
        public void Rent_MinimumLengthWithinRange_ReturnsArrayWithSufficientLength()
        {
            // Arrange
            int minimumLength = 32;

            // Act
            int[] rentedArray = _pool.Rent(minimumLength);

            // Assert
            Assert.IsNotNull(rentedArray);
            Assert.IsTrue(rentedArray.Length >= minimumLength);
        }

        [TestMethod]
        public void Rent_MinimumLengthTooSmall_ReturnsNewArray()
        {
            // Arrange
            int minimumLength = 1;

            // Act
            int[] rentedArray = _pool.Rent(minimumLength);

            // Assert
            Assert.IsNotNull(rentedArray);
            Assert.AreEqual(minimumLength, rentedArray.Length);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentOutOfRangeException))]
        public void Rent_MinimumLengthTooLarge_ReturnsNewArray()
        {
            // Arrange
            int minimumLength = int.MaxValue;

            // Act
            _ = _pool.Rent(minimumLength);

            // Assert
            // Handled by annotation
        }

        [TestMethod]
        public void Return_ValidArray_CanBeRentedAgain()
        {
            // Arrange
            int minimumLength = 32;
            int[] rentedArray = _pool.Rent(minimumLength);

            // Act
            _pool.Return(rentedArray);
            int[] rentedArrayAgain = _pool.Rent(minimumLength);

            // Assert
            Assert.IsNotNull(rentedArrayAgain);
            Assert.IsTrue(rentedArrayAgain.Length >= minimumLength);
        }

        [TestMethod]
        public void Return_ClearArrayTrue_ArrayIsCleared()
        {
            // Arrange
            int minimumLength = 32;
            int[] rentedArray = _pool.Rent(minimumLength);
            rentedArray[0] = 123;

            // Act
            _pool.Return(rentedArray, clearArray: true);
            int[] rentedArrayAgain = _pool.Rent(minimumLength);

            // Assert
            Assert.IsNotNull(rentedArrayAgain);
            Assert.AreEqual(0, rentedArrayAgain[0]);
        }

        [TestMethod]
        public void Rent_ExhaustedBucket_AllocatesNewArray()
        {
            // Arrange
            int minimumLength = 32;

            // Act
            var rentedArrays = Enumerable.Range(0, 2)
                .Select(_ => _pool.Rent(minimumLength))
                .ToList();

            // Assert
            Assert.AreEqual(2, rentedArrays.Count);
            Assert.IsTrue(rentedArrays.All(array => array.Length >= minimumLength));
        }

        [TestMethod]
        public void Return_ArrayToDifferentBucket_HandlesCorrectly()
        {
            // Arrange
            int rentLength = 64;
            int returnLength = 32;

            // Act
            int[] rentedArray = _pool.Rent(rentLength);
            Array.Resize(ref rentedArray, returnLength);
            _pool.Return(rentedArray);

            int[] rentedArrayAgain = _pool.Rent(returnLength);

            // Assert
            Assert.IsNotNull(rentedArrayAgain);
            Assert.IsTrue(rentedArrayAgain.Length >= returnLength);
        }

        [TestMethod]
        public void GetBucketIndex_NonPowerOfTwoLength_ReturnsNegativeOne()
        {
            // Arrange
            int nonPowerOfTwoLength = 17;

            // Act
            int bucketIndex = StackArrayPool<int>.GetBucketIndex(nonPowerOfTwoLength);

            // Assert
            Assert.AreEqual(-1, bucketIndex);
        }

        [TestMethod]
        public void GetBucketIndex_LengthOutOfRange_ReturnsNegativeOne()
        {
            // Arrange
            int lengthOutOfRange = int.MaxValue;

            // Act
            int bucketIndex = StackArrayPool<int>.GetBucketIndex(lengthOutOfRange);

            // Assert
            Assert.AreEqual(-1, bucketIndex);
        }

        [TestMethod]
        public void Return_ShouldClearArrayIfClearArrayIsTrue()
        {
            // Arrange
            var array = new int[] { 1, 2, 3 };

            // Act
            _pool.Return(array, clearArray: true);

            // Assert
            CollectionAssert.AreEqual(new int[] { 0, 0, 0 }, array);
        }

        [TestMethod]
        public void Return_ShouldNotClearArrayIfClearArrayIsFalse()
        {
            // Arrange
            var array = new int[] { 1, 2, 3 };

            // Act
            _pool.Return(array, clearArray: false);

            // Assert
            CollectionAssert.AreEqual(new int[] { 1, 2, 3 }, array);
        }
    }
}
