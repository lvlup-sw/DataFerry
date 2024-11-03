using lvlup.DataFerry.Collections;
using lvlup.DataFerry.Orchestrators;
using lvlup.DataFerry.Orchestrators.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace lvlup.DataFerry.Tests
{
    [TestClass]
    public class ConcurrentPriorityQueueTests
    {
        private ConcurrentPriorityQueue<int, string> _queue = default!;
        private ITaskOrchestrator _taskOrchestrator = default!;

        [TestInitialize]
        public void Setup()
        {
            _taskOrchestrator = new TaskOrchestrator(
                LoggerFactory.Create(builder => builder.Services.AddLogging())
                             .CreateLogger<TaskOrchestrator>()
            );
            _queue = new(_taskOrchestrator, Comparer<int>.Create((x, y) => x.CompareTo(y)));
        }

        [TestMethod]
        public void TryAdd_AddsElementsWithCorrectPriority()
        {
            // Act & Arrange
            _queue.TryAdd(1, "Task A");
            _queue.TryAdd(3, "Task C");
            _queue.TryAdd(2, "Task B");
            _queue.TryGetElement(1, out var elementA);
            _queue.TryGetElement(2, out var elementB);
            _queue.TryGetElement(3, out var elementC);

            // Assert
            Assert.AreEqual(elementA, "Task A");
            Assert.AreEqual(elementB, "Task B");
            Assert.AreEqual(elementC, "Task C");
        }

        [TestMethod]
        public void TryRemoveMin_RemovesElementsInPriorityOrder()
        {
            // Arrange
            _queue.TryAdd(1, "Task A");
            _queue.TryAdd(3, "Task C");
            _queue.TryAdd(2, "Task B");

            // Act & Assert
            Assert.IsTrue(_queue.TryRemoveMin(out var elementA));
            Assert.AreEqual("Task A", elementA);

            Assert.IsTrue(_queue.TryRemoveMin(out var elementB));
            Assert.AreEqual("Task B", elementB);

            Assert.IsTrue(_queue.TryRemoveMin(out var elementC));
            Assert.AreEqual("Task C", elementC);
        }

        [TestMethod]
        public void TryRemovePriority_RemovesCorrectPriority()
        {
            // Arrange
            _queue.TryAdd(1, "Task A");
            _queue.TryAdd(3, "Task C");
            _queue.TryAdd(2, "Task B");

            // Act & Assert
            Assert.IsTrue(_queue.TryRemovePriority(3));
            Assert.IsFalse(_queue.TryGetElement(3, out var elementC));

            Assert.IsTrue(_queue.TryRemovePriority(2));
            Assert.IsFalse(_queue.TryGetElement(2, out var elementB));

            Assert.IsTrue(_queue.TryRemovePriority(1));
            Assert.IsFalse(_queue.TryGetElement(1, out var elementA));
        }

        [TestMethod]
        public void TryRemovePriority_RemovesAnyDuplicatePriority()
        {
            // Arrange
            _queue.TryAdd(3, "Task A");
            _queue.TryAdd(3, "Task C");
            _queue.TryAdd(3, "Task B");

            // Act & Assert
            Assert.IsTrue(_queue.TryRemovePriority(3));
            Assert.IsTrue(_queue.TryGetElement(3, out var elementC));
            Assert.AreEqual(elementC, "Task C");

            Assert.IsTrue(_queue.TryRemovePriority(2));
            Assert.IsTrue(_queue.TryGetElement(2, out var elementB));
            Assert.AreEqual(elementB, "Task B");

            Assert.IsTrue(_queue.TryRemovePriority(1));
            Assert.IsTrue(_queue.TryGetElement(1, out var elementA));
            Assert.AreEqual(elementA, "Task A");
        }

        [TestMethod]
        public void Update_UpdatesPriorityCorrectly()
        {
            // Arrange
            _queue.TryAdd(3, "Task A");
            _queue.TryAdd(2, "Task B");

            // Act
            _queue.Update(1, "Task A");
            Thread.Sleep(1000);

            // Assert
            Assert.IsTrue(_queue.TryRemoveMin(out var elementA));
            Assert.AreEqual(elementA, "Task A");
        }

        // Add more tests for other methods and edge cases as needed
    }
}
