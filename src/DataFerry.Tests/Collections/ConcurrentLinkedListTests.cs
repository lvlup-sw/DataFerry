using lvlup.DataFerry.Collections;
using lvlup.DataFerry.Tests.TestModels;

namespace lvlup.DataFerry.Tests.Collections
{
    [TestClass]
    public class ConcurrentLinkedListTests
    {
        private ConcurrentLinkedList<Payload> _list = default!;

        [TestInitialize]
        public void Setup()
        {
            _list = new(Comparer<Payload>.Create((x, y) => y.CompareTo(x)));
        }

        [TestCleanup]
        public void Cleanup()
        {
            _list = default!;
        }

        [TestMethod]
        public void TryInsert_ShouldInsertNewNode()
        {
            // Arrange
            var payload = TestUtils.CreatePayloadRandom();

            // Act
            bool result = _list.TryInsert(payload);

            // Assert
            Assert.IsTrue(result);
            Assert.IsTrue(_list.Contains(payload));
        }

        [TestMethod]
        public void TryInsert_ShouldNotInsertDuplicateKey_WhenKeyExists()
        {
            // Arrange
            var payload = TestUtils.CreatePayloadIdentical();
            _list.TryInsert(payload);

            // Act
            bool result = _list.TryInsert(payload);

            // Assert
            Assert.IsFalse(result);
        }

        [TestMethod]
        public void TryInsert_ShouldInsertManyNodes()
        {
            // Arrange
            List<Payload> payloads = [];
            for (int i = 0; i < 50; i++)
            {
                payloads.Add(TestUtils.CreatePayloadRandom());
                bool res = _list.TryInsert(payloads[i]);
                Assert.IsTrue(res);
            }

            // Act
            bool result = _list.Count == 50;

            // Assert
            Assert.IsTrue(result);
        }

        [TestMethod]
        public void TryInsert_ShouldHandleConcurrentInserts_WithSameKey()
        {
            // Arrange
            var key = TestUtils.CreatePayloadIdentical();
            var tasks = new List<Task<bool>>();

            // Act
            for (int i = 0; i < 10; i++)
            {
                tasks.Add(Task.Run(() => _list.TryInsert(key)));
            }
            Task.WaitAll([.. tasks]);

            // Assert
            // Only one insertion should be successful
            Assert.AreEqual(1, tasks.Count(t => t.Result));
            Assert.IsTrue(_list.Contains(key));
        }

        [TestMethod]
        public void TryRemove_ShouldRemoveExistingNode_WhenKeyExists()
        {
            // Arrange
            var key = TestUtils.CreatePayloadIdentical();
            _list.TryInsert(key);

            // Act
            bool result = _list.TryRemove(key);

            // Assert
            Assert.IsTrue(result);
            Assert.IsFalse(_list.Contains(key));
        }

        [TestMethod]
        public void TryRemove_ShouldNotRemoveNode_WhenKeyDoesNotExist()
        {
            // Arrange
            var key = TestUtils.CreatePayloadRandom();

            // Act
            bool result = _list.TryRemove(key);

            // Assert
            Assert.IsFalse(result);
        }

        [TestMethod]
        public void TryRemove_ShouldHandleConcurrentRemoves_WithSameKey()
        {
            // Arrange
            var key = TestUtils.CreatePayloadIdentical();
            _list.TryInsert(key);
            var tasks = new List<Task<bool>>();
            var initialCount = _list.Count;

            // Act
            for (int i = 0; i < 10; i++)
            {
                tasks.Add(Task.Run(() => _list.TryRemove(key)));
            }
            Task.WaitAll([.. tasks]);

            // Assert
            Assert.AreEqual(initialCount - 1, _list.Count);
            Assert.IsFalse(_list.Contains(key));
        }

        [TestMethod]
        public void Contains_ShouldReturnTrue_WhenKeyExistsAndStateIsValid()
        {
            // Arrange
            var key = TestUtils.CreatePayloadIdentical();
            _list.TryInsert(key);

            // Act
            bool result = _list.Contains(key);

            // Assert
            Assert.IsTrue(result);
        }

        [TestMethod]
        public void Contains_ShouldReturnFalse_WhenKeyDoesNotExist()
        {
            // Arrange
            var key = TestUtils.CreatePayloadIdentical();

            // Act
            bool result = _list.Contains(key);

            // Assert
            Assert.IsFalse(result);
        }

        [TestMethod]
        public void Contains_ShouldReturnFalse_WhenKeyExistsButStateIsInvalid()
        {
            // Arrange
            var key = TestUtils.CreatePayloadIdentical();
            _list.TryInsert(key);
            _list.TryRemove(key); // This should set the state to INV

            // Act
            bool result = _list.Contains(key);

            // Assert
            Assert.IsFalse(result);
        }

        [TestMethod]
        public void Find_ShouldReturnNode_WhenValueExistsAndStateIsValid()
        {
            // Arrange
            var key = TestUtils.CreatePayloadIdentical();
            _list.TryInsert(key);

            // Act
            Node<Payload>? node = _list.Find(key);

            // Assert
            Assert.IsNotNull(node);
            Assert.AreEqual(key, node.Key);
        }

        [TestMethod]
        public void Find_ShouldReturnNull_WhenValueDoesNotExist()
        {
            // Arrange
            var key = TestUtils.CreatePayloadIdentical();

            // Act
            Node<Payload>? node = _list.Find(key);

            // Assert
            Assert.IsNull(node);
        }

        [TestMethod]
        public void Find_ShouldReturnNull_WhenValueExistsButStateIsInvalid()
        {
            // Arrange
            var key = TestUtils.CreatePayloadIdentical();
            _list.TryInsert(key);
            _list.TryRemove(key);

            // Act
            Node<Payload>? node = _list.Find(key);

            // Assert
            Assert.IsNull(node);
        }

        [TestMethod]
        public void CopyTo_ShouldCopyElementsToArray_WhenListIsNotEmpty()
        {
            // Arrange
            var key1 = TestUtils.CreatePayloadRandom();
            var key2 = TestUtils.CreatePayloadRandom();
            _list.TryInsert(key1);
            _list.TryInsert(key2);
            var array = new Payload[_list.Count];

            // Act
            _list.CopyTo(array, 0);

            // Assert
            Assert.AreEqual(key2, array[0]);
            Assert.AreEqual(key1, array[1]);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentOutOfRangeException))]
        public void CopyTo_ShouldNotCopyElements_WhenListIsEmpty()
        {
            // Arrange
            var array = new Payload[_list.Count];

            // Act
            _list.CopyTo(array, 0);
        }

        [TestMethod]
        public void CopyTo_ShouldCopyElementsToArray_FromGivenIndex()
        {
            // Arrange
            var key1 = TestUtils.CreatePayloadIdentical();
            var key2 = TestUtils.CreatePayloadRandom();
            _list.TryInsert(key1);
            _list.TryInsert(key2);
            var array = new Payload[_list.Count + 1];
            array[0] = TestUtils.CreatePayloadIdentical();

            // Act
            _list.CopyTo(array, 1);

            // Assert
            Assert.AreEqual(key1, array[2]);
            Assert.AreEqual(key2, array[1]);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void CopyTo_ShouldThrowArgumentNullException_WhenArrayIsNull()
        {
            // Act
            _list.CopyTo(null!, 0);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentOutOfRangeException))]
        public void CopyTo_ShouldThrowArgumentOutOfRangeException_WhenIndexIsNegative()
        {
            // Act
            _list.CopyTo(new Payload[_list.Count], -1);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentOutOfRangeException))]
        public void CopyTo_ShouldThrowArgumentOutOfRangeException_WhenIndexIsGreaterThanOrEqualToArrayLength()
        {
            // Act
            _list.CopyTo(new Payload[_list.Count], 2);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentOutOfRangeException))]
        public void CopyTo_ShouldThrowArgumentOutOfRangeException_WhenNotEnoughSpaceInArray()
        {
            // Arrange
            var key1 = TestUtils.CreatePayloadIdentical();
            var key2 = TestUtils.CreatePayloadIdentical();
            _list.TryInsert(key1);
            _list.TryInsert(key2);

            // Act
            _list.CopyTo(new Payload[_list.Count], 1);
        }
    }
}
