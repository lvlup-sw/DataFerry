using lvlup.DataFerry.Collections;
using lvlup.DataFerry.Orchestrators;
using lvlup.DataFerry.Orchestrators.Contracts;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace lvlup.DataFerry.Tests.Collections
{
    [TestClass]
    public class ConcurrentPriorityQueueTests
    {
#pragma warning disable CS8618
        private ConcurrentPriorityQueue<int, string> _queue;
        private ITaskOrchestrator _taskOrchestrator;
#pragma warning restore CS8618

        [TestInitialize]
        public void Setup()
        {
            // Using a synchronous orchestrator for predictable testing unless testing async removal
            _taskOrchestrator = new TaskOrchestrator(LoggerFactory.Create(builder => builder.Services.AddLogging()).CreateLogger<TaskOrchestrator>());

            // Default comparer (min-heap behavior for ints)
            _queue = new ConcurrentPriorityQueue<int, string>(
                _taskOrchestrator,
                Comparer<int>.Default);
        }

        [TestMethod]
        public void TryAdd_AddsElementsWithCorrectPriority()
        {
            // Arrange
            var initialCount = _queue.GetCount();

            // Act
            _queue.TryAdd(1, "Task A");
            _queue.TryAdd(3, "Task C");
            _queue.TryAdd(2, "Task B");

            // Assert
            Assert.AreEqual(initialCount + 3, _queue.GetCount());
            Assert.IsTrue(_queue.ContainsPriority(1));
            Assert.IsTrue(_queue.ContainsPriority(2));
            Assert.IsTrue(_queue.ContainsPriority(3));
        }

        [TestMethod]
        public void TryDeleteMin_RemovesElementsInPriorityOrder()
        {
            // Arrange
            _queue.TryAdd(1, "Task A");
            _queue.TryAdd(3, "Task C");
            _queue.TryAdd(2, "Task B");
            var initialCount = _queue.GetCount();
            
            // Act & Assert
            Assert.IsTrue(_queue.TryDeleteMin(out var elementA));
            Assert.AreEqual("Task A", elementA);
            Assert.AreEqual(initialCount - 1, _queue.GetCount());
            Assert.IsFalse(_queue.ContainsPriority(1));

            Assert.IsTrue(_queue.TryDeleteMin(out var elementB));
            Assert.AreEqual("Task B", elementB);
            Assert.AreEqual(initialCount - 2, _queue.GetCount());
            Assert.IsFalse(_queue.ContainsPriority(2));

            Assert.IsTrue(_queue.TryDeleteMin(out var elementC));
            Assert.AreEqual("Task C", elementC);
            Assert.AreEqual(initialCount - 3, _queue.GetCount());
            Assert.IsFalse(_queue.ContainsPriority(3));

            Assert.IsFalse(_queue.TryDeleteMin(out _));
        }

        [TestMethod]
        public void TryDelete_RemovesCorrectPriority()
        {
            // Arrange
            _queue.TryAdd(1, "Task A");
            _queue.TryAdd(3, "Task C");
            _queue.TryAdd(2, "Task B");
            var initialCount = _queue.GetCount();

            // Act & Assert
            Assert.IsTrue(_queue.TryDelete(3));
            Assert.IsFalse(_queue.ContainsPriority(3), "Priority 3 should be deleted");
            Assert.AreEqual(initialCount - 1, _queue.GetCount());

            Assert.IsTrue(_queue.TryDelete(1));
            Assert.IsFalse(_queue.ContainsPriority(1), "Priority 1 should be deleted");
            Assert.AreEqual(initialCount - 2, _queue.GetCount());

            Assert.IsTrue(_queue.TryDelete(2));
            Assert.IsFalse(_queue.ContainsPriority(2), "Priority 2 should be deleted");
            Assert.AreEqual(initialCount - 3, _queue.GetCount());

            Assert.IsFalse(_queue.TryDelete(1), "Deleting already deleted priority should fail");
            Assert.IsFalse(_queue.TryDelete(5), "Deleting non-existent priority should fail");
        }

        [TestMethod]
        public void TryAdd_DuplicatePriorities_AllowedAndOrdered()
        {
            // Arrange (assuming default allows duplicates)
            _queue.TryAdd(1, "Task A1");
            _queue.TryAdd(2, "Task B");
            _queue.TryAdd(1, "Task A2");
            _queue.TryAdd(1, "Task A3");

            // Assert
            Assert.AreEqual(4, _queue.GetCount());
            Assert.IsTrue(_queue.ContainsPriority(1));

            // Act & Assert
            Assert.IsTrue(_queue.TryDeleteMin(out var el1));
            Assert.AreEqual("Task A1", el1);
            Assert.IsTrue(_queue.TryDeleteMin(out var el2));
            Assert.AreEqual("Task A2", el2);
            Assert.IsTrue(_queue.TryDeleteMin(out var el3));
            Assert.AreEqual("Task A3", el3);
            Assert.IsTrue(_queue.ContainsPriority(2));
            Assert.IsTrue(_queue.TryDeleteMin(out var el4));
            Assert.AreEqual("Task B", el4);
            Assert.AreEqual(0, _queue.GetCount());
        }

        [TestMethod]
        public void ContainsPriority_ReturnsCorrectly()
        {
            // Arrange
            Assert.IsFalse(_queue.ContainsPriority(1), "Should not contain initially");
            _queue.TryAdd(1, "Task A");
            _queue.TryAdd(3, "Task C");

            // Act & Assert
            Assert.IsTrue(_queue.ContainsPriority(1));
            Assert.IsFalse(_queue.ContainsPriority(2));
            Assert.IsTrue(_queue.ContainsPriority(3));

            _queue.TryDelete(1);
            Assert.IsFalse(_queue.ContainsPriority(1), "Should not contain after delete");
        }

        [TestMethod]
        public void Update_UpdatesElementSuccessfully()
        {
            // Arrange
            _queue.TryAdd(1, "Task A Original");
            _queue.TryAdd(2, "Task B");

            // Act
            bool updateResult = _queue.Update(1, "Task A Updated");

            // Assert
            Assert.IsTrue(updateResult);
            Assert.IsTrue(_queue.TryDeleteMin(out var element));
            Assert.AreEqual("Task A Updated", element);
            Assert.IsTrue(_queue.ContainsPriority(2));
        }

        [TestMethod]
        public void Update_UsingFunction_UpdatesElementSuccessfully()
        {
            // Arrange
            _queue.TryAdd(1, "Task A");

            // Act
            bool updateResult = _queue.Update(1, (p, el) => $"{el} - Priority {p} - Updated");

            // Assert
            Assert.IsTrue(updateResult);
            Assert.IsTrue(_queue.TryDeleteMin(out var element));
            Assert.AreEqual("Task A - Priority 1 - Updated", element);
        }

        [TestMethod]
        public void Update_NonExistentPriority_ReturnsFalse()
        {
            // Arrange
            _queue.TryAdd(1, "Task A");

            // Act
            bool updateResult = _queue.Update(5, "Task Z");

            // Assert
            Assert.IsFalse(updateResult);
            Assert.AreEqual(1, _queue.GetCount());
        }

        [TestMethod]
        public void Update_DeletedPriority_ReturnsFalse()
        {
            // Arrange
            _queue.TryAdd(1, "Task A");
            Assert.IsTrue(_queue.TryDelete(1));

            // Act
            bool updateResult = _queue.Update(1, "Task A Updated");

            // Assert
            Assert.IsFalse(updateResult);
            Assert.AreEqual(0, _queue.GetCount());
        }


        [TestMethod]
        public void Operations_OnEmptyQueue_BehaveCorrectly()
        {
            // Arrange (Queue is empty)

            // Act & Assert
            Assert.AreEqual(0, _queue.GetCount());
            Assert.IsFalse(_queue.ContainsPriority(1));
            Assert.IsFalse(_queue.TryDeleteMin(out _));
            Assert.IsFalse(_queue.TryDelete(1));
            Assert.IsFalse(_queue.Update(1, "A"));
            Assert.IsFalse(_queue.TryDeleteMinProbabilistically(out _));
        }

        [TestMethod]
        public void MaxSize_EnforcedCorrectly()
        {
            // Arrange
            var boundedQueue = new ConcurrentPriorityQueue<int, string>(
                _taskOrchestrator,
                Comparer<int>.Default,
                maxSize: 3);

            // Act
            boundedQueue.TryAdd(5, "E");
            boundedQueue.TryAdd(2, "B");
            boundedQueue.TryAdd(4, "D");
            Assert.AreEqual(3, boundedQueue.GetCount(), "Count should be at max size before overflow");
            boundedQueue.TryAdd(1, "A");

            // Assert
            Assert.IsTrue(boundedQueue.GetCount() <= 3, $"Count {boundedQueue.GetCount()} should be <= 3 after adding past max size");
            Assert.IsTrue(boundedQueue.ContainsPriority(5), "Highest priority item 5 should likely remain");
            boundedQueue.TryAdd(6, "F");

            Assert.IsTrue(boundedQueue.GetCount() <= 3, $"Count {boundedQueue.GetCount()} should be <= 3 after second overflow add");
            Assert.IsTrue(boundedQueue.ContainsPriority(6), "New highest priority item 6 should be present");
            Assert.IsTrue(boundedQueue.ContainsPriority(5), "Original highest priority item 5 should likely remain");
        }

        [TestMethod]
        public void UnlimitedSize_AllowsExceedingDefaultMaxSize()
        {
            // Arrange
             var unlimitedQueue = new ConcurrentPriorityQueue<int, string>(
                _taskOrchestrator,
                Comparer<int>.Default,
                maxSize: int.MaxValue);

            // Act
            for (int i = 0; i < ConcurrentPriorityQueue<int,string>.DefaultMaxSize + 10; i++)
            {
                unlimitedQueue.TryAdd(i, $"Task {i}");
            }

            // Assert
            Assert.AreEqual(ConcurrentPriorityQueue<int, string>.DefaultMaxSize + 10, unlimitedQueue.GetCount(), "Count should exceed default max size");
        }

         [TestMethod]
         public void TryDeleteMinProbabilistically_RemovesAnElement()
         {
             // Arrange
             _queue.TryAdd(1, "Task A");
             _queue.TryAdd(2, "Task B");
             Assert.AreEqual(2, _queue.GetCount());

             // Act
             bool result = _queue.TryDeleteMinProbabilistically(out string? element);

             // Assert
             Assert.IsTrue(result);
             Assert.IsNotNull(element);
             Assert.AreEqual(1, _queue.GetCount());
         }
    }
}