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
            _queue = new(_taskOrchestrator, Comparer<int>.Create((x, y) => y.CompareTo(x)));
        }

        [TestMethod]
        public void TryAdd_AddsElementsWithCorrectPriority()
        {
            _queue.TryAdd(1, "Task A");
            _queue.TryAdd(3, "Task C");
            _queue.TryAdd(2, "Task B");

            Assert.IsTrue(_queue.ContainsPriority(1));
            Assert.IsTrue(_queue.ContainsPriority(2));
            Assert.IsTrue(_queue.ContainsPriority(3));
            //Assert.IsTrue(_queue.ContainsElement("Task A"));
            //Assert.IsTrue(_queue.ContainsElement("Task B"));
            //Assert.IsTrue(_queue.ContainsElement("Task C"));
            
            var enumerator = _queue.GetEnumerator();
            enumerator.MoveNext();
            var n1 = enumerator.Current;
            enumerator.MoveNext();
            var n2 = enumerator.Current;
            enumerator.MoveNext();
            var n3 = enumerator.Current;

            Assert.IsTrue(n1.Equals("Task A"));
            Assert.IsTrue(n2.Equals("Task B"));
            Assert.IsTrue(n3.Equals("Task C"));
        }

        [TestMethod]
        public void TryRemoveMin_RemovesElementsInPriorityOrder()
        {
            _queue.TryAdd(1, "Task A");
            _queue.TryAdd(3, "Task C");
            _queue.TryAdd(2, "Task B");

            Assert.IsTrue(_queue.TryRemoveMin(out var element1));
            Assert.AreEqual("Task A", element1);

            Thread.Sleep(100);

            Assert.IsTrue(_queue.TryRemoveMin(out var element2));
            Assert.AreEqual("Task B", element2);

            Assert.IsTrue(_queue.TryRemoveMin(out var element3));
            Assert.AreEqual("Task C", element3);

            Assert.IsFalse(_queue.TryRemoveMin(out _)); // Queue should be empty
        }

        [TestMethod]
        public void Update_UpdatesPriorityCorrectly()
        {
            _queue.TryAdd(3, "Task A");
            _queue.TryAdd(2, "Task B");

            _queue.Update(1, "Task A");

            Assert.IsTrue(_queue.TryRemoveMin(out var element1));
            Assert.AreEqual("Task A", element1);

            Assert.IsTrue(_queue.TryRemoveMin(out var element2));
            Assert.AreEqual("Task B", element2);
        }

        // Add more tests for other methods and edge cases as needed
    }
}
