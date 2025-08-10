using System.Buffers;
using System.Collections.Concurrent;
using System.Reflection;
using lvlup.DataFerry.Concurrency;
using lvlup.DataFerry.Concurrency.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics.Metrics;

using Moq;

namespace lvlup.DataFerry.Tests.Unit;

[TestClass]
public class ConcurrentPriorityQueueTests
{
    // ReSharper disable AccessToModifiedClosure
    #pragma warning disable CS8618
    private Mock<ITaskOrchestrator> _mockTaskOrchestrator;
    private Mock<ILoggerFactory> _mockLoggerFactory;
    private Mock<IMeterFactory> _mockMeterFactory;
    private IOptions<ConcurrentPriorityQueueOptions> _options;
    private IComparer<int> _intComparer;
    #pragma warning restore CS8618

    [TestInitialize]
    public void TestInitialize()
    {
        _mockTaskOrchestrator = new Mock<ITaskOrchestrator>();
        _mockTaskOrchestrator
            .Setup(o => o.Run(It.IsAny<Func<Task>>()))
            .Callback<Func<Task>>(action => action());

        _mockLoggerFactory = new Mock<ILoggerFactory>();
        _mockLoggerFactory
            .Setup(x => x.CreateLogger(It.IsAny<string>()))
            .Returns(Mock.Of<ILogger>());

        _mockMeterFactory = new Mock<IMeterFactory>();
        var mockMeter = new Mock<Meter>("test", "1.0");
        _mockMeterFactory
            .Setup(x => x.Create(It.IsAny<MeterOptions>()))
            .Returns(mockMeter.Object);

        _options = Options.Create(new ConcurrentPriorityQueueOptions());
        _intComparer = Comparer<int>.Default;
    }

    #region Test Helpers

    private ConcurrentPriorityQueue<int, string> CreateDefaultQueue(int maxSize = ConcurrentPriorityQueue<int, string>.DefaultMaxSize)
    {
        var options = Options.Create(new ConcurrentPriorityQueueOptions { MaxSize = maxSize });
        return new ConcurrentPriorityQueue<int, string>(
            _mockTaskOrchestrator.Object,
            _intComparer,
            _mockLoggerFactory.Object,
            _mockMeterFactory.Object,
            options);
    }

    private ConcurrentPriorityQueue<string, string> CreateDefaultQueueWithRefs(int maxSize = ConcurrentPriorityQueue<int, string>.DefaultMaxSize)
    {
        var options = Options.Create(new ConcurrentPriorityQueueOptions { MaxSize = maxSize });
        return new ConcurrentPriorityQueue<string, string>(
            _mockTaskOrchestrator.Object,
            Comparer<string>.Default,
            _mockLoggerFactory.Object,
            _mockMeterFactory.Object,
            options);
    }

    private SkipListNode<int, string> CreateNode(int priority, string element, int height, bool isInserted = true, bool isDeleted = false)
    {
        var node = new SkipListNode<int, string>(priority, element, height, _intComparer)
        {
            IsInserted = isInserted,
            IsDeleted = isDeleted
        };
        return node;
    }

    private static SkipListNode<int, string> CreateHeadNode(int height)
    {
        return new SkipListNode<int, string>(SkipListNode<int, string>.NodeType.Head, height);
    }

    private static SkipListNode<int, string> CreateTailNode(int height)
    {
        return new SkipListNode<int, string>(SkipListNode<int, string>.NodeType.Tail, height);
    }

    private static T GetInstanceField<T>(object instance, string fieldName)
    {
        FieldInfo? field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(field, $"Could not find instance field {fieldName}");
        object? value = field.GetValue(instance);
        Assert.IsNotNull(value, $"Field {fieldName} value is null");
        return (T)value;
    }

    private static long GetCurrentSequenceGeneratorValue()
    {
        FieldInfo? field = typeof(SkipListNode<int, string>)
            .GetField("s_sequenceGenerator", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.IsNotNull(field, "Could not find static field s_sequenceGenerator");
        return (long)(field.GetValue(null) ?? -1L);
    }

    #endregion
    #region Constructor Tests

    [TestMethod]
    public void Constructor_WithValidParameters_InitializesCorrectly()
    {
        var options = Options.Create(new ConcurrentPriorityQueueOptions
        {
            MaxSize = 100,
            SprayOffsetK = 2,
            SprayOffsetM = 2,
            PromotionProbability = 0.25
        });
        var cpq = new ConcurrentPriorityQueue<int, string>(
            _mockTaskOrchestrator.Object,
            _intComparer,
            _mockLoggerFactory.Object,
            _mockMeterFactory.Object,
            options);
        Assert.IsNotNull(cpq);
        Assert.AreEqual(0, cpq.GetCount());
    }

    [TestMethod]
    public void Constructor_NullTaskOrchestrator_ThrowsArgumentNullException()
    {
        Assert.ThrowsException<ArgumentNullException>(() =>
        {
            _ = new ConcurrentPriorityQueue<int, string>(
                null!,
                _intComparer,
                _mockLoggerFactory.Object,
                _mockMeterFactory.Object,
                _options);
        });
    }

    [TestMethod]
    public void Constructor_NullComparer_ThrowsArgumentNullException()
    {
        Assert.ThrowsException<ArgumentNullException>(() =>
        {
            _ = new ConcurrentPriorityQueue<int, string>(
                _mockTaskOrchestrator.Object,
                null!,
                _mockLoggerFactory.Object,
                _mockMeterFactory.Object,
                _options);
        });
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    public void Constructor_InvalidMaxSize_ThrowsArgumentOutOfRangeException(int maxSize)
    {
        // Note: Options validation would typically happen during binding
        // For test purposes, we'll skip validation and let the constructor handle it
        var options = new Mock<IOptions<ConcurrentPriorityQueueOptions>>();
        options.Setup(x => x.Value).Returns(new ConcurrentPriorityQueueOptions { MaxSize = maxSize });

        // The constructor should validate the options
        Assert.ThrowsException<ArgumentException>(() =>
        {
            _ = new ConcurrentPriorityQueue<int, string>(
                _mockTaskOrchestrator.Object,
                _intComparer,
                _mockLoggerFactory.Object,
                _mockMeterFactory.Object,
                options.Object);
        });
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    public void Constructor_InvalidOffsetK_ThrowsArgumentOutOfRangeException(int offsetK)
    {
        var options = new Mock<IOptions<ConcurrentPriorityQueueOptions>>();
        options.Setup(x => x.Value).Returns(new ConcurrentPriorityQueueOptions { SprayOffsetK = offsetK });

        Assert.ThrowsException<ArgumentException>(() =>
        {
            _ = new ConcurrentPriorityQueue<int, string>(
                _mockTaskOrchestrator.Object,
                _intComparer,
                _mockLoggerFactory.Object,
                _mockMeterFactory.Object,
                options.Object);
        });
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    public void Constructor_InvalidOffsetM_ThrowsArgumentOutOfRangeException(int offsetM)
    {
        var options = new Mock<IOptions<ConcurrentPriorityQueueOptions>>();
        options.Setup(x => x.Value).Returns(new ConcurrentPriorityQueueOptions { SprayOffsetM = offsetM });

        Assert.ThrowsException<ArgumentException>(() =>
        {
            _ = new ConcurrentPriorityQueue<int, string>(
                _mockTaskOrchestrator.Object,
                _intComparer,
                _mockLoggerFactory.Object,
                _mockMeterFactory.Object,
                options.Object);
        });
    }

    [TestMethod]
    [DataRow(0.0)]
    [DataRow(-0.1)]
    [DataRow(1.0)]
    [DataRow(1.1)]
    public void Constructor_InvalidPromotionProbability_ThrowsArgumentOutOfRangeException(double probability)
    {
        var options = new Mock<IOptions<ConcurrentPriorityQueueOptions>>();
        options.Setup(x => x.Value).Returns(new ConcurrentPriorityQueueOptions { PromotionProbability = probability });

        Assert.ThrowsException<ArgumentException>(() =>
        {
            _ = new ConcurrentPriorityQueue<int, string>(
                _mockTaskOrchestrator.Object,
                _intComparer,
                _mockLoggerFactory.Object,
                _mockMeterFactory.Object,
                options.Object);
        });
    }

    [TestMethod]
    public void Constructor_WithIntMaxValueSize_InitializesCorrectly()
    {
         var options = Options.Create(new ConcurrentPriorityQueueOptions { MaxSize = int.MaxValue });
         var queue = new ConcurrentPriorityQueue<int, string>(
             _mockTaskOrchestrator.Object,
             _intComparer,
             _mockLoggerFactory.Object,
             _mockMeterFactory.Object,
             options);
         Assert.IsNotNull(queue);
         Assert.AreEqual(0, queue.GetCount());
         // Todo: verify internal state via reflection
    }

    #endregion
    #region TryPeekMin Tests

    [TestMethod]
    public void TryPeekMin_EmptyQueue_ReturnsFalse()
    {
        // Arrange
        var queue = CreateDefaultQueueWithRefs(); // Use refs to check for null element

        // Act
        bool result = queue.TryPeekMin(out string priority, out string element);

        // Assert
        Assert.IsFalse(result, "TryPeekMin on an empty queue should return false.");
        Assert.IsNull(priority, "Priority should be default (null) for an empty queue.");
        Assert.IsNull(element, "Element should be default (null) for an empty queue.");
    }

    [TestMethod]
    public void TryPeekMin_SingleElement_ReturnsElement()
    {
        // Arrange
        var queue = CreateDefaultQueue();
        const int expectedPriority = 10;
        const string expectedElement = "Element10";
        queue.TryAdd(expectedPriority, expectedElement);

        // Act
        bool result = queue.TryPeekMin(out int actualPriority, out string actualElement);

        // Assert
        Assert.IsTrue(result, "TryPeekMin should return true for a non-empty queue.");
        Assert.AreEqual(expectedPriority, actualPriority, "The peeked priority should match the added priority.");
        Assert.AreEqual(expectedElement, actualElement, "The peeked element should match the added element.");
        Assert.AreEqual(1, queue.GetCount(), "The queue count should not change after peeking.");
    }

    [TestMethod]
    public void TryPeekMin_MultipleElements_ReturnsMinimum()
    {
        // Arrange
        var queue = CreateDefaultQueue();
        const int minPriority = 5;
        const string minElement = "Element5";
        queue.TryAdd(20, "Element20");
        queue.TryAdd(minPriority, minElement);
        queue.TryAdd(10, "Element10");

        // Act
        bool result = queue.TryPeekMin(out int actualPriority, out string actualElement);

        // Assert
        Assert.IsTrue(result, "TryPeekMin should return true for a non-empty queue.");
        Assert.AreEqual(minPriority, actualPriority, "TryPeekMin should return the minimum priority.");
        Assert.AreEqual(minElement, actualElement, "TryPeekMin should return the element associated with the minimum priority.");
        Assert.AreEqual(3, queue.GetCount(), "The queue count should not change after peeking.");
    }

    [TestMethod]
    public void TryPeekMin_AfterDelete_ReturnsNextMinimum()
    {
        // Arrange
        var queue = CreateDefaultQueue();
        const int firstMinPriority = 5;
        const int secondMinPriority = 10;
        const string secondMinElement = "Element10";

        queue.TryAdd(20, "Element20");
        queue.TryAdd(firstMinPriority, "Element5");
        queue.TryAdd(secondMinPriority, secondMinElement);

        // Act
        // Delete the first minimum
        bool deleteResult = queue.TryDeleteAbsoluteMin(out _);
        Assert.IsTrue(deleteResult, "Pre-condition: Deleting the first min element should succeed.");

        // Peek for the new minimum
        bool peekResult = queue.TryPeekMin(out int actualPriority, out string actualElement);

        // Assert
        Assert.IsTrue(peekResult, "TryPeekMin should succeed after a deletion.");
        Assert.AreEqual(secondMinPriority, actualPriority, "TryPeekMin should return the new minimum priority.");
        Assert.AreEqual(secondMinElement, actualElement, "TryPeekMin should return the new minimum element.");
        Assert.AreEqual(2, queue.GetCount(), "The queue count should be 2 after one deletion.");
    }

    [TestMethod]
    public void TryPeekMin_WithLogicallyDeletedNodes_SkipsDeleted()
    {
        // Arrange
        var queue = CreateDefaultQueue();
        const int expectedPriority = 15;
        const string expectedElement = "Element15";

        queue.TryAdd(5, "Element5");
        queue.TryAdd(10, "Element10");
        queue.TryAdd(expectedPriority, expectedElement);
        queue.TryAdd(20, "Element20");

        // Logically delete the first two minimum nodes
        Assert.IsTrue(queue.TryRemove(5, out _), "Should successfully remove priority 5.");
        Assert.IsTrue(queue.TryRemove(10, out _), "Should successfully remove priority 10.");
        Assert.AreEqual(2, queue.GetCount(), "Queue count should be 2 after deletions.");

        // Act
        bool result = queue.TryPeekMin(out int actualPriority, out string actualElement);

        // Assert
        Assert.IsTrue(result, "TryPeekMin should find an element after skipping deleted ones.");
        Assert.AreEqual(expectedPriority, actualPriority, "TryPeekMin should return the first valid minimum priority.");
        Assert.AreEqual(expectedElement, actualElement, "TryPeekMin should return the element of the first valid minimum.");
    }

    [TestMethod]
    public void TryPeekMin_ConcurrentReads_ThreadSafe()
    {
        // Arrange
        var queue = CreateDefaultQueue();
        const int expectedPriority = 5;
        const string expectedElement = "Element5";
        queue.TryAdd(10, "Element10");
        queue.TryAdd(expectedPriority, expectedElement);
        const int numThreads = 10;
        var tasks = new List<Task>();
        var exceptions = new ConcurrentBag<Exception>();

        // Act
        for (int i = 0; i < numThreads; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                try
                {
                    bool result = queue.TryPeekMin(out int priority, out string element);
                    Assert.IsTrue(result, "Concurrent peek should succeed.");
                    Assert.AreEqual(expectedPriority, priority, "Concurrent peek should return the correct priority.");
                    Assert.AreEqual(expectedElement, element, "Concurrent peek should return the correct element.");
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            }));
        }

        Task.WaitAll(tasks.ToArray());

        // Assert
        Assert.AreEqual(0, exceptions.Count, "No exceptions should be thrown during concurrent peeks.");
        Assert.AreEqual(2, queue.GetCount(), "Queue count should not change after concurrent peeks.");
    }

    [TestMethod]
    public void TryPeekMin_DuplicatePriorities_ReturnsFirstBySequence()
    {
        // Arrange
        var queue = CreateDefaultQueue();
        const int priority = 5;
        const string firstElement = "FirstElement";
        const string secondElement = "SecondElement";

        queue.TryAdd(10, "Element10");
        queue.TryAdd(priority, firstElement); // This one has the lower sequence number
        queue.TryAdd(priority, secondElement);

        // Act
        bool result = queue.TryPeekMin(out int actualPriority, out string actualElement);

        // Assert
        Assert.IsTrue(result, "TryPeekMin should succeed.");
        Assert.AreEqual(priority, actualPriority, "The correct priority should be returned.");
        Assert.AreEqual(firstElement, actualElement, "The element with the lowest sequence number should be returned.");
    }

    [TestMethod]
    public void TryPeekMin_DoesNotModifyQueue()
    {
        // Arrange
        var queue = CreateDefaultQueue();
        queue.TryAdd(10, "Element10");
        queue.TryAdd(5, "Element5");
        int initialCount = queue.GetCount();

        // Act
        bool result1 = queue.TryPeekMin(out int priority1, out string element1);
        int countAfterFirstPeek = queue.GetCount();
        bool result2 = queue.TryPeekMin(out int priority2, out string element2);
        int countAfterSecondPeek = queue.GetCount();

        // Assert
        Assert.IsTrue(result1, "First peek should succeed.");
        Assert.IsTrue(result2, "Second peek should succeed.");
        Assert.AreEqual(initialCount, countAfterFirstPeek, "Count should not change after first peek.");
        Assert.AreEqual(initialCount, countAfterSecondPeek, "Count should not change after second peek.");
        Assert.AreEqual(priority1, priority2, "Priorities from consecutive peeks should be the same.");
        Assert.AreEqual(element1, element2, "Elements from consecutive peeks should be the same.");
    }

    #endregion
    #region TryRemove Tests

    [TestMethod]
    public void TryRemove_EmptyQueue_ReturnsFalse()
    {
        // Arrange
        var queue = CreateDefaultQueueWithRefs();

        // Act
        bool result = queue.TryRemove("non-existent", out string element);

        // Assert
        Assert.IsFalse(result, "TryRemove on an empty queue should return false.");
        Assert.IsNull(element, "Element should be default (null) for an empty queue.");
    }

    [TestMethod]
    public void TryRemove_ExistingPriority_RemovesAndReturnsElement()
    {
        // Arrange
        var queue = CreateDefaultQueue();
        const int priorityToRemove = 10;
        const string elementToRemove = "Element10";
        queue.TryAdd(5, "Element5");
        queue.TryAdd(priorityToRemove, elementToRemove);
        int initialCount = queue.GetCount();

        // Act
        bool result = queue.TryRemove(priorityToRemove, out string removedElement);

        // Assert
        Assert.IsTrue(result, "TryRemove should return true for an existing priority.");
        Assert.AreEqual(elementToRemove, removedElement, "The correct element should be returned.");
        Assert.AreEqual(initialCount - 1, queue.GetCount(), "The queue count should decrease by one.");
        Assert.IsFalse(queue.ContainsPriority(priorityToRemove), "The priority should no longer be in the queue.");
        _mockTaskOrchestrator.Verify(o => o.Run(It.IsAny<Func<Task>>()), Times.Once, "A background removal task should have been scheduled.");
    }

    [TestMethod]
    public void TryRemove_NonExistentPriority_ReturnsFalse()
    {
        // Arrange
        var queue = CreateDefaultQueue();
        queue.TryAdd(1, "Element1");
        const int nonExistentPriority = 99;

        // Act
        bool result = queue.TryRemove(nonExistentPriority, out string element);

        // Assert
        Assert.IsFalse(result, "TryRemove for a non-existent priority should return false.");
        Assert.IsNull(element, "Element should be default (null) when removal fails.");
        Assert.AreEqual(1, queue.GetCount(), "Queue count should not change.");
        _mockTaskOrchestrator.Verify(o => o.Run(It.IsAny<Func<Task>>()), Times.Never, "No background task should be scheduled for a failed removal.");
    }

    [TestMethod]
    public void TryRemove_DuplicatePriorities_RemovesFirst()
    {
        // Arrange
        var queue = CreateDefaultQueue();
        const int priority = 10;
        const string firstElement = "Element10a";
        const string secondElement = "Element10b";
        queue.TryAdd(priority, firstElement); // Lower sequence number
        queue.TryAdd(priority, secondElement);
        queue.TryAdd(1, "Element1");
        int initialCount = queue.GetCount();

        // Act
        bool result = queue.TryRemove(priority, out string removedElement);

        // Assert
        Assert.IsTrue(result, "TryRemove should succeed for a duplicate priority.");
        Assert.AreEqual(firstElement, removedElement, "The element with the lower sequence number should be removed first.");
        Assert.AreEqual(initialCount - 1, queue.GetCount(), "Queue count should decrease by one.");
        Assert.IsTrue(queue.ContainsPriority(priority), "The priority should still exist in the queue.");
        _mockTaskOrchestrator.Verify(o => o.Run(It.IsAny<Func<Task>>()), Times.Once, "A background removal task should have been scheduled.");
    }

    [TestMethod]
    public void TryRemove_ConcurrentRemoves_OnlyOneSucceeds()
    {
        // Arrange
        var queue = CreateDefaultQueue();
        const int priorityToRemove = 10;
        queue.TryAdd(priorityToRemove, "Element10");
        const int numThreads = 5;
        var tasks = new List<Task<bool>>();
        var exceptions = new ConcurrentBag<Exception>();
        int successCount = 0;

        // Act
        for (int i = 0; i < numThreads; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                try
                {
                    return queue.TryRemove(priorityToRemove, out _);
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                    return false;
                }
            }));
        }

        var results = Task.WhenAll(tasks).Result;
        foreach(bool res in results)
        {
            if (res)
            {
                successCount++;
            }
        }

        // Assert
        Assert.AreEqual(0, exceptions.Count, "No exceptions should be thrown during concurrent removes.");
        Assert.AreEqual(1, successCount, "Exactly one thread should succeed in removing the element.");
        Assert.AreEqual(0, queue.GetCount(), "The queue should be empty after the element is removed.");
        _mockTaskOrchestrator.Verify(o => o.Run(It.IsAny<Func<Task>>()), Times.Once, "Exactly one background removal task should have been scheduled.");
    }

    [TestMethod]
    public void TryRemove_UpdatesCountCorrectly()
    {
        // Arrange
        var queue = CreateDefaultQueue();
        queue.TryAdd(1, "E1");
        queue.TryAdd(2, "E2");
        queue.TryAdd(3, "E3");
        Assert.AreEqual(3, queue.GetCount(), "Pre-condition: Count should be 3.");

        // Act & Assert
        queue.TryRemove(2, out _);
        Assert.AreEqual(2, queue.GetCount(), "Count should be 2 after removing priority 2.");

        queue.TryRemove(1, out _);
        Assert.AreEqual(1, queue.GetCount(), "Count should be 1 after removing priority 1.");

        queue.TryRemove(3, out _);
        Assert.AreEqual(0, queue.GetCount(), "Count should be 0 after removing priority 3.");

        queue.TryRemove(99, out _); // Non-existent
        Assert.AreEqual(0, queue.GetCount(), "Count should remain 0 after attempting to remove a non-existent priority.");
    }

    [TestMethod]
    public void TryRemove_SchedulesPhysicalRemoval()
    {
        // Arrange
        var queue = CreateDefaultQueue();
        queue.TryAdd(1, "Element1");

        // Act
        bool result = queue.TryRemove(1, out _);

        // Assert
        Assert.IsTrue(result, "Removal should be successful.");
        _mockTaskOrchestrator.Verify(o => o.Run(It.IsAny<Func<Task>>()), Times.Once, "Run should be called exactly once to schedule physical removal.");
    }

    [TestMethod]
    public void TryRemove_HandlesNullPriority_ThrowsException()
    {
        // Arrange
        var queue = CreateDefaultQueueWithRefs();
        string nullPriority = null!;

        // Act & Assert
        Assert.ThrowsException<ArgumentNullException>(() => queue.TryRemove(nullPriority, out _));
    }

    #endregion
    #region TryPeek Tests

    [TestMethod]
    public void TryPeek_EmptyQueue_ReturnsFalse()
    {
        // Arrange
        var queue = CreateDefaultQueueWithRefs();

        // Act
        bool result = queue.TryPeek("non-existent", out var element);

        // Assert
        Assert.IsFalse(result, "TryPeek on an empty queue should return false.");
        Assert.IsNull(element, "Element should be default (null) for an empty queue.");
    }

    [TestMethod]
    public void TryPeek_ExistingPriority_ReturnsElement()
    {
        // Arrange
        var queue = CreateDefaultQueue();
        const int priorityToPeek = 10;
        const string expectedElement = "Element10";
        queue.TryAdd(5, "Element5");
        queue.TryAdd(priorityToPeek, expectedElement);
        int initialCount = queue.GetCount();

        // Act
        bool result = queue.TryPeek(priorityToPeek, out string actualElement);

        // Assert
        Assert.IsTrue(result, "TryPeek should return true for an existing priority.");
        Assert.AreEqual(expectedElement, actualElement, "The correct element should be returned.");
        Assert.AreEqual(initialCount, queue.GetCount(), "Queue count should not change after a peek.");
        Assert.IsTrue(queue.ContainsPriority(priorityToPeek), "The peeked priority should still exist.");
    }

    [TestMethod]
    public void TryPeek_NonExistentPriority_ReturnsFalse()
    {
        // Arrange
        var queue = CreateDefaultQueue();
        queue.TryAdd(1, "Element1");
        const int nonExistentPriority = 99;

        // Act
        bool result = queue.TryPeek(nonExistentPriority, out string element);

        // Assert
        Assert.IsFalse(result, "TryPeek for a non-existent priority should return false.");
        Assert.IsNull(element, "Element should be default (null) when peek fails.");
        Assert.AreEqual(1, queue.GetCount(), "Queue count should not change.");
    }

    [TestMethod]
    public void TryPeek_DuplicatePriorities_ReturnsFirst()
    {
        // Arrange
        var queue = CreateDefaultQueue();
        const int priority = 10;
        const string firstElement = "Element10a";
        const string secondElement = "Element10b";
        queue.TryAdd(priority, firstElement); // Lower sequence number
        queue.TryAdd(priority, secondElement);
        queue.TryAdd(1, "Element1");

        // Act
        bool result = queue.TryPeek(priority, out string peekedElement);

        // Assert
        Assert.IsTrue(result, "TryPeek should succeed for a duplicate priority.");
        Assert.AreEqual(firstElement, peekedElement, "The element with the lower sequence number should be returned first.");
        Assert.AreEqual(3, queue.GetCount(), "Queue count should not change.");
    }

    [TestMethod]
    public void TryPeek_DoesNotModifyQueue()
    {
        // Arrange
        var queue = CreateDefaultQueue();
        const int priority = 10;
        const string element = "Element10";
        queue.TryAdd(priority, element);
        queue.TryAdd(5, "Element5");
        int initialCount = queue.GetCount();

        // Act
        queue.TryPeek(priority, out _);
        int countAfterFirstPeek = queue.GetCount();
        queue.TryPeek(priority, out _);
        int countAfterSecondPeek = queue.GetCount();

        // Assert
        Assert.AreEqual(initialCount, countAfterFirstPeek, "Count should not change after first peek.");
        Assert.AreEqual(initialCount, countAfterSecondPeek, "Count should not change after second peek.");
        Assert.IsTrue(queue.ContainsPriority(priority), "Peeked element should still be in the queue.");
    }

    [TestMethod]
    public void TryPeek_ConcurrentReads_ThreadSafe()
    {
        // Arrange
        var queue = CreateDefaultQueue();
        const int priorityToPeek = 10;
        const string expectedElement = "Element10";
        queue.TryAdd(priorityToPeek, expectedElement);
        const int numThreads = 10;
        var tasks = new List<Task>();
        var exceptions = new ConcurrentBag<Exception>();

        // Act
        for (int i = 0; i < numThreads; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                try
                {
                    bool result = queue.TryPeek(priorityToPeek, out string element);
                    Assert.IsTrue(result, "Concurrent peek should succeed.");
                    Assert.AreEqual(expectedElement, element, "Concurrent peek should return the correct element.");
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            }));
        }

        Task.WaitAll(tasks.ToArray());

        // Assert
        Assert.AreEqual(0, exceptions.Count, "No exceptions should be thrown during concurrent peeks.");
        Assert.AreEqual(1, queue.GetCount(), "Queue count should not change after concurrent peeks.");
    }

    [TestMethod]
    public void TryPeek_WithLogicallyDeletedNode_ReturnsNextValid()
    {
        // Arrange
        var queue = CreateDefaultQueue();
        const int priority = 10;
        const string firstElement = "Element10a";
        const string secondElement = "Element10b";
        queue.TryAdd(priority, firstElement);
        queue.TryAdd(priority, secondElement);
        Assert.IsTrue(queue.TryRemove(priority, out string removedElement), "Pre-condition: First element should be removed.");
        Assert.AreEqual(firstElement, removedElement, "Pre-condition: The first element should be the one removed.");

        // Act
        bool result = queue.TryPeek(priority, out string peekedElement);

        // Assert
        Assert.IsTrue(result, "TryPeek should find the second valid element.");
        Assert.AreEqual(secondElement, peekedElement, "TryPeek should return the second element after the first was deleted.");
    }

    [TestMethod]
    public void TryPeek_HandlesNullPriority_ThrowsException()
    {
        // Arrange
        var queue = CreateDefaultQueueWithRefs();
        string nullPriority = null!;

        // Act & Assert
        Assert.ThrowsException<ArgumentNullException>(() => queue.TryPeek(nullPriority, out _));
    }

    #endregion
    #region TryAdd Tests

    [TestMethod]
    public void TryAdd_SingleElement_AddsSuccessfully()
    {
        // Arrange
        var queue = CreateDefaultQueue();
        const int priority = 1;
        const string element = "Element1";

        // Act
        bool added = queue.TryAdd(priority, element);

        // Assert
        Assert.IsTrue(added, "TryAdd should return true for a successful addition.");
        Assert.AreEqual(1, queue.GetCount(), "Queue count should be 1 after adding one element.");
        Assert.IsTrue(queue.ContainsPriority(priority), "Queue should contain the added priority.");
    }

    [TestMethod]
    public void TryAdd_MultipleElements_AddsSuccessfullyInOrder()
    {
        // Arrange
        var queue = CreateDefaultQueue();

        // Act
        queue.TryAdd(5, "Element5");
        queue.TryAdd(1, "Element1");
        queue.TryAdd(3, "Element3");

        // Assert
        Assert.AreEqual(3, queue.GetCount(), "Queue count should be 3 after adding three elements.");
        Assert.IsTrue(queue.ContainsPriority(1), "Queue should contain priority 1.");
        Assert.IsTrue(queue.ContainsPriority(3), "Queue should contain priority 3.");
        Assert.IsTrue(queue.ContainsPriority(5), "Queue should contain priority 5.");
    }

    [TestMethod]
    public void TryAdd_DuplicatePriority_AddsSuccessfully()
    {
        // Arrange
        var queue = CreateDefaultQueue();
        const int priority = 1;
        queue.TryAdd(priority, "Element1a");

        // Act
        bool added = queue.TryAdd(priority, "Element1b");

        // Assert
        Assert.IsTrue(added, "TryAdd should return true when adding a duplicate priority.");
        Assert.AreEqual(2, queue.GetCount(), "Queue count should be 2 after adding a duplicate priority.");
        Assert.IsTrue(queue.ContainsPriority(priority), "Queue should still contain the priority after adding a duplicate.");
    }

    [TestMethod]
    public void TryAdd_ExceedMaxSize_RemovesMinElement()
    {
        // Arrange
        const int maxSize = 2;
        var queue = CreateDefaultQueue(maxSize: maxSize);
        queue.TryAdd(5, "Element5");
        queue.TryAdd(1, "Element1");
        const int priorityToAdd = 3;
        const string elementToAdd = "Element3";

        // Act
        bool added = queue.TryAdd(priorityToAdd, elementToAdd);

        // Assert
        Assert.IsTrue(added, "TryAdd should return true even when exceeding max size (if removal succeeds).");
        Assert.AreEqual(maxSize, queue.GetCount(), $"Queue count should remain at max size {maxSize}.");
        Assert.IsFalse(queue.ContainsPriority(1), "The element with the minimum priority (1) should have been removed.");
        Assert.IsTrue(queue.ContainsPriority(priorityToAdd), "The newly added element (priority 3) should be in the queue.");
        Assert.IsTrue(queue.ContainsPriority(5), "The other existing element (priority 5) should still be in the queue.");
        _mockTaskOrchestrator.Verify(o => o.Run(It.IsAny<Func<Task>>()), Times.AtLeastOnce, "A background removal task should have been scheduled.");
    }

    [TestMethod]
    public void TryAdd_ExceedMaxSizeUnbounded_DoesNotRemoveMinElement()
    {
        // Arrange
        var queue = CreateDefaultQueue(maxSize: int.MaxValue);
        queue.TryAdd(5, "Element5");
        queue.TryAdd(1, "Element1");
        const int priorityToAdd = 3;
        const string elementToAdd = "Element3";

        // Act
        bool added = queue.TryAdd(priorityToAdd, elementToAdd);

        // Assert
        Assert.IsTrue(added, "TryAdd should return true for unbounded queue.");
        Assert.AreEqual(3, queue.GetCount(), "Queue count should increase to 3.");
        Assert.IsTrue(queue.ContainsPriority(1), "Priority 1 should still exist.");
        Assert.IsTrue(queue.ContainsPriority(priorityToAdd), "Priority 3 should exist.");
        Assert.IsTrue(queue.ContainsPriority(5), "Priority 5 should still exist.");
        _mockTaskOrchestrator.Verify(o => o.Run(It.IsAny<Func<Task>>()), Times.Never, "No background removal task should have been scheduled for an unbounded queue.");
    }

    [TestMethod]
    public void TryAdd_NullPriority_ThrowsArgumentNullException()
    {
        // Arrange
        var queue = CreateDefaultQueueWithRefs();
        string nullPriority = null!;

        // Act & Assert
        Assert.ThrowsException<ArgumentNullException>(() => queue.TryAdd(nullPriority, "Element"));
    }


    [TestMethod]
    public void TryAdd_NullElement_ThrowsArgumentNullException()
    {
        // Arrange
        var queue = CreateDefaultQueue();
        const int priority = 1;
        string nullElement = null!;

        // Act & Assert
        Assert.ThrowsException<ArgumentNullException>(() => queue.TryAdd(priority, nullElement));
    }

    #endregion
    #region TryDelete Tests (Using TryRemove)

    [TestMethod]
    public void TryDelete_ExistingElement_DeletesSuccessfully()
    {
        // Arrange
        var queue = CreateDefaultQueue();
        const int priorityToDelete = 1;
        const int remainingPriority = 2;
        queue.TryAdd(priorityToDelete, "Element1");
        queue.TryAdd(remainingPriority, "Element2");
        int initialCount = queue.GetCount();

        // Act
        bool deleted = queue.TryRemove(priorityToDelete, out _);

        // Assert
        Assert.IsTrue(deleted, "TryDelete should return true for an existing element.");
        Assert.AreEqual(initialCount - 1, queue.GetCount(), "Queue count should decrease by 1.");
        Assert.IsFalse(queue.ContainsPriority(priorityToDelete), "Deleted priority should no longer be in the queue.");
        Assert.IsTrue(queue.ContainsPriority(remainingPriority), "Other priorities should remain in the queue.");
        _mockTaskOrchestrator.Verify(o => o.Run(It.IsAny<Func<Task>>()), Times.Once, "A background removal task should have been scheduled.");
    }

    [TestMethod]
    public void TryDelete_NonExistingElement_ReturnsFalse()
    {
        // Arrange
        var queue = CreateDefaultQueue();
        const int existingPriority = 1;
        queue.TryAdd(existingPriority, "Element1");
        const int priorityToDelete = 99;
        int initialCount = queue.GetCount();

        // Act
        bool deleted = queue.TryRemove(priorityToDelete, out _);

        // Assert
        Assert.IsFalse(deleted, "TryDelete should return false for a non-existing element.");
        Assert.AreEqual(initialCount, queue.GetCount(), "Queue count should remain unchanged.");
        Assert.IsTrue(queue.ContainsPriority(existingPriority), "Existing priorities should remain in the queue.");
        _mockTaskOrchestrator.Verify(o => o.Run(It.IsAny<Func<Task>>()), Times.Never, "No background removal task should have been scheduled.");
    }

    [TestMethod]
    public void TryDelete_DuplicatePriority_DeletesOneInstance()
    {
        // Arrange
        var queue = CreateDefaultQueue();
        const int priorityToDelete = 1;
        const int otherPriority = 2;
        queue.TryAdd(priorityToDelete, "Element1a");
        queue.TryAdd(priorityToDelete, "Element1b");
        queue.TryAdd(otherPriority, "Element2");
        int initialCount = queue.GetCount();

        // Act
        bool deleted = queue.TryRemove(priorityToDelete, out _);

        // Assert
        Assert.IsTrue(deleted, "TryDelete should return true when deleting one instance of a duplicate priority.");
        Assert.AreEqual(initialCount - 1, queue.GetCount(), "Queue count should decrease by 1.");
        Assert.IsTrue(queue.ContainsPriority(priorityToDelete), "Queue should still contain the priority due to the remaining duplicate.");
        Assert.IsTrue(queue.ContainsPriority(otherPriority), "Other priorities should remain in the queue.");
        _mockTaskOrchestrator.Verify(o => o.Run(It.IsAny<Func<Task>>()), Times.Once, "A background removal task should have been scheduled.");
    }

    [TestMethod]
    public void TryDelete_EmptyQueue_ReturnsFalse()
    {
        // Arrange
        var queue = CreateDefaultQueue();
        const int priorityToDelete = 1;

        // Act
        bool deleted = queue.TryRemove(priorityToDelete, out _);

        // Assert
        Assert.IsFalse(deleted, "TryDelete should return false for an empty queue.");
        Assert.AreEqual(0, queue.GetCount(), "Queue count should remain 0.");
        _mockTaskOrchestrator.Verify(o => o.Run(It.IsAny<Func<Task>>()), Times.Never, "No background removal task should have been scheduled.");
    }

    [TestMethod]
    public void TryDelete_NullPriority_ThrowsArgumentNullException()
    {
        // Arrange
        var queue = CreateDefaultQueueWithRefs();
        string nullPriority = null!;

        // Act & Assert
        Assert.ThrowsException<ArgumentNullException>(() => queue.TryRemove(nullPriority, out _));
    }

    #endregion
    #region TryDeleteAbsoluteMin Tests

    [TestMethod]
    public void TryDeleteAbsoluteMin_NonEmptyQueue_DeletesMinElement()
    {
        // Arrange
        var queue = CreateDefaultQueue();
        const int minPriority = 1;
        const string minElement = "Element1";
        queue.TryAdd(5, "Element5");
        queue.TryAdd(minPriority, minElement);
        queue.TryAdd(3, "Element3");
        int initialCount = queue.GetCount();

        // Act
        bool deleted = queue.TryDeleteAbsoluteMin(out string actualElement);

        // Assert
        Assert.IsTrue(deleted, "TryDeleteAbsoluteMin should return true for a non-empty queue.");
        Assert.AreEqual(minElement, actualElement, "The out parameter should contain the element with the minimum priority.");
        Assert.AreEqual(initialCount - 1, queue.GetCount(), "Queue count should decrease by 1.");
        Assert.IsFalse(queue.ContainsPriority(minPriority), "The minimum priority should no longer be in the queue.");
        Assert.IsTrue(queue.ContainsPriority(3), "Priority 3 should remain.");
        Assert.IsTrue(queue.ContainsPriority(5), "Priority 5 should remain.");
        _mockTaskOrchestrator.Verify(o => o.Run(It.IsAny<Func<Task>>()), Times.Once, "A background removal task should have been scheduled.");
    }

    [TestMethod]
    public void TryDeleteAbsoluteMin_QueueWithDuplicatesAtMin_DeletesOneMinElement()
    {
        // Arrange
        var queue = CreateDefaultQueue();
        const int minPriority = 1;
        const string minElementA = "Element1a";
        const string minElementB = "Element1b";
        const int otherPriority = 5;
        queue.TryAdd(minPriority, minElementA);
        queue.TryAdd(otherPriority, "Element5");
        queue.TryAdd(minPriority, minElementB);
        int initialCount = queue.GetCount();

        // Act
        bool deleted = queue.TryDeleteAbsoluteMin(out string actualElement);

        // Assert
        Assert.IsTrue(deleted, "TryDeleteAbsoluteMin should return true when duplicates exist at min priority.");
        Assert.IsTrue(actualElement is minElementA or minElementB, $"Deleted element '{actualElement}' was not one of the expected minimum elements ('{minElementA}' or '{minElementB}').");
        Assert.AreEqual(initialCount - 1, queue.GetCount(), "Queue count should decrease by 1.");
        Assert.IsTrue(queue.ContainsPriority(minPriority), "The minimum priority should still exist due to the remaining duplicate.");
        Assert.IsTrue(queue.ContainsPriority(otherPriority), "The other priority (5) should remain.");
        _mockTaskOrchestrator.Verify(o => o.Run(It.IsAny<Func<Task>>()), Times.Once, "A background removal task should have been scheduled.");
    }

    [TestMethod]
    public void TryDeleteAbsoluteMin_EmptyQueue_ReturnsFalse()
    {
        // Arrange
        var queue = CreateDefaultQueueWithRefs();

        // Act
        bool deleted = queue.TryDeleteAbsoluteMin(out string result);

        // Assert
        Assert.IsFalse(deleted, "TryDeleteAbsoluteMin should return false for an empty queue.");
        Assert.IsNull(result, "The out parameter should be the default value (null for string) for an empty queue.");
        Assert.AreEqual(0, queue.GetCount(), "Queue count should remain 0.");
        _mockTaskOrchestrator.Verify(o => o.Run(It.IsAny<Func<Task>>()), Times.Never, "No background removal task should have been scheduled.");
    }

    [TestMethod]
    public void TryDeleteAbsoluteMin_QueueWithOneElement_DeletesElement()
    {
        // Arrange
        var queue = CreateDefaultQueue();
        const int priority = 10;
        const string expectedElement = "SingleElement";
        queue.TryAdd(priority, expectedElement);

        // Act
        bool deleted = queue.TryDeleteAbsoluteMin(out string actualElement);

        // Assert
        Assert.IsTrue(deleted, "TryDeleteAbsoluteMin should return true for a queue with one element.");
        Assert.AreEqual(expectedElement, actualElement, "The out parameter should be the single element in the queue.");
        Assert.AreEqual(0, queue.GetCount(), "Queue count should become 0.");
        _mockTaskOrchestrator.Verify(o => o.Run(It.IsAny<Func<Task>>()), Times.Once, "A background removal task should have been scheduled.");
    }

    #endregion
    #region TryDeleteMin (SprayList) Tests

    [TestMethod]
    public void TryDeleteMin_QueueWithMultipleElements_DeletesAnElement()
    {
        // Arrange
        var queue = CreateDefaultQueue(maxSize: 100);
        const int numberOfElements = 20;
        for (int i = 10; i >= 0; i--)
        {
            queue.TryAdd(i, $"Element{i}");
        }
        for (int i = 11; i < numberOfElements; i++)
        {
            queue.TryAdd(i, $"Element{i}");
        }
        int initialCount = queue.GetCount();
        Assert.AreEqual(numberOfElements, initialCount, "Pre-condition: Queue should have the expected number of elements.");

        // Act
        bool deleted = queue.TryDeleteMin(out string element);

        // Assert
        Assert.IsTrue(deleted, "TryDeleteMin should return true for a sufficiently populated queue.");
        Assert.IsNotNull(element, "The out parameter 'element' should not be null as an element should have been deleted.");
        Assert.AreEqual(initialCount - 1, queue.GetCount(), "Queue count should decrease by 1.");
        _mockTaskOrchestrator.Verify(o => o.Run(It.IsAny<Func<Task>>()), Times.Once, "A background removal task should have been scheduled.");
    }

    [TestMethod]
    public void TryDeleteMin_QueueWithVeryFewElements_FallsBackToDeleteAbsoluteMin()
    {
        // Arrange
        var queue = CreateDefaultQueue(maxSize: 5);
        const int priority = 1;
        const string expectedElement = "Element1";
        queue.TryAdd(priority, expectedElement);
        int initialCount = queue.GetCount();
        Assert.AreEqual(1, initialCount, "Pre-condition: Queue should have exactly one element.");

        // Act
        bool deleted = queue.TryDeleteMin(out string actualElement);

        // Assert
        Assert.IsTrue(deleted, "TryDeleteMin should return true when falling back to TryDeleteAbsoluteMin for a single-element queue.");
        Assert.AreEqual(expectedElement, actualElement, "The out parameter should be the single element present, indicating fallback logic worked.");
        Assert.AreEqual(0, queue.GetCount(), "Queue count should become 0.");
        _mockTaskOrchestrator.Verify(o => o.Run(It.IsAny<Func<Task>>()), Times.Once, "A background removal task should have been scheduled via the fallback.");
    }

    [TestMethod]
    public void TryDeleteMin_EmptyQueue_ReturnsFalse()
    {
         // Arrange
        var queue = CreateDefaultQueue();
        Assert.AreEqual(0, queue.GetCount(), "Pre-condition: Queue should be empty.");

        // Act
        bool deleted = queue.TryDeleteMin(out string actualElement);

        // Assert
        Assert.IsFalse(deleted, "TryDeleteMin should return false for an empty queue.");
        Assert.IsNull(actualElement, "The out parameter should be the default value (null for string) for an empty queue.");
        Assert.AreEqual(0, queue.GetCount(), "Queue count should remain 0.");
        _mockTaskOrchestrator.Verify(o => o.Run(It.IsAny<Func<Task>>()), Times.Never, "No background removal task should have been scheduled.");
    }

    #endregion
    #region SampleNearMin Tests

    [TestMethod]
    public void SampleNearMin_SufficientElements_ReturnsSamples()
    {
        // Arrange
        var queue = CreateDefaultQueue(maxSize: 100);
        const int elementCount = 50;
        for (int i = 0; i < elementCount; i++)
        {
            queue.TryAdd(i, $"Element{i}");
        }
        const int sampleSize = 5;
        const int maxAttemptsMultiplier = 5;
        Assert.IsTrue(elementCount > sampleSize, "Pre-condition: Element count must be greater than sample size.");

        // Act
        var samples = queue.SampleNearMin(sampleSize, maxAttemptsMultiplier).ToList();

        // Assert
        Assert.IsNotNull(samples, "The result of SampleNearMin should not be null.");
        Assert.IsTrue(samples.Count > 0, "Expected at least one sample to be returned.");
        Assert.IsTrue(samples.Count <= sampleSize, $"The number of samples ({samples.Count}) should not exceed the requested sample size ({sampleSize}).");
        Console.WriteLine($"SampleNearMin returned {samples.Count} samples (requested {sampleSize}).");

        foreach (var sample in samples)
        {
            Assert.IsTrue(queue.ContainsPriority(sample.priority), $"Sampled priority {sample.priority} should ideally still exist in the queue.");
        }
    }

    [TestMethod]
    public void SampleNearMin_InsufficientElements_ReturnsEmptyList()
    {
        // Arrange
        var queue = CreateDefaultQueue(maxSize: 100);
        queue.TryAdd(1, "Element1");
        queue.TryAdd(2, "Element2");
        const int sampleSize = 5;
        Assert.IsTrue(queue.GetCount() <= sampleSize, "Pre-condition: Element count must be less than or equal to sample size.");

        // Act
        var samples = queue.SampleNearMin(sampleSize).ToList();

        // Assert
        Assert.IsNotNull(samples, "The result of SampleNearMin should not be null.");
        Assert.AreEqual(0, samples.Count, "The sample list should be empty when fewer elements exist than the sample size.");
    }

    [TestMethod]
    public void SampleNearMin_EmptyQueue_ReturnsEmptyList()
    {
        // Arrange
        var queue = CreateDefaultQueue();
        const int sampleSize = 5;
        Assert.AreEqual(0, queue.GetCount(), "Pre-condition: Queue must be empty.");

        // Act
        var samples = queue.SampleNearMin(sampleSize).ToList();

        // Assert
        Assert.IsNotNull(samples, "The result of SampleNearMin should not be null.");
        Assert.AreEqual(0, samples.Count, "The sample list should be empty for an empty queue.");
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    public void SampleNearMin_InvalidSampleSize_ThrowsArgumentOutOfRangeException(int sampleSize)
    {
        // Arrange
        var queue = CreateDefaultQueue();

        // Act & Assert
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => queue.SampleNearMin(sampleSize));
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    public void SampleNearMin_InvalidMaxAttemptsMultiplier_ThrowsArgumentOutOfRangeException(int multiplier)
    {
        // Arrange
        var queue = CreateDefaultQueue();
        const int sampleSize = 1;
        queue.TryAdd(1, "E1");
        queue.TryAdd(2, "E2");
        Assert.IsTrue(queue.GetCount() > sampleSize, "Pre-condition: Element count must be greater than sample size.");

        // Act & Assert
        Assert.ThrowsException<ArgumentOutOfRangeException>(() => queue.SampleNearMin(sampleSize, multiplier));
    }

    #endregion
    #region Update Tests

    [TestMethod]
    public void Update_ExistingElement_UpdatesSuccessfully()
    {
        // Arrange
        var queue = CreateDefaultQueue();
        const int priority = 1;
        const string originalElement = "OriginalElement";
        const string updatedElement = "UpdatedElement";
        queue.TryAdd(priority, originalElement);

        // Act
        bool updated = queue.Update(priority, updatedElement);

        // Assert
        Assert.IsTrue(updated, "Update should return true for an existing element.");
        Assert.IsTrue(queue.TryDeleteAbsoluteMin(out string elementAfterUpdate), "Should be able to delete the element after update.");
        Assert.AreEqual(updatedElement, elementAfterUpdate, "The retrieved element should match the updated value.");
    }

    [TestMethod]
    public void Update_NonExistingElement_ReturnsFalse()
    {
        // Arrange
        var queue = CreateDefaultQueue();
        queue.TryAdd(1, "Element1");
        const int nonExistingPriority = 99;
        const string newElement = "NewElement";
        int initialCount = queue.GetCount();

        // Act
        bool updated = queue.Update(nonExistingPriority, newElement);

        // Assert
        Assert.IsFalse(updated, "Update should return false for a non-existing priority.");
        Assert.AreEqual(initialCount, queue.GetCount(), "Queue count should not change when updating a non-existing element.");
    }

    [TestMethod]
    public void Update_WithUpdateFunction_UpdatesSuccessfully()
    {
        // Arrange
        var queue = CreateDefaultQueue();
        const int priority = 1;
        const string originalElement = "Value:10";
        queue.TryAdd(priority, originalElement);
        Func<int, string, string> updateFunction = (_, e) => $"{e}_Updated";
        string expectedElementAfterUpdate = updateFunction(priority, originalElement);

        // Act
        bool updated = queue.Update(priority, updateFunction);

        // Assert
        Assert.IsTrue(updated, "Update with function should return true for an existing element.");
        Assert.IsTrue(queue.TryDeleteAbsoluteMin(out string elementAfterUpdate), "Should be able to delete the element after update.");
        Assert.AreEqual(expectedElementAfterUpdate, elementAfterUpdate, "The retrieved element should match the value returned by the update function.");
    }

    [TestMethod]
    public void Update_WithUpdateFunction_NonExistingElement_ReturnsFalse()
    {
        // Arrange
        var queue = CreateDefaultQueue();
        const int nonExistingPriority = 99;
        Func<int, string, string> updateFunction = (_, e) => $"{e}_Updated";
        int initialCount = queue.GetCount();

        // Act
        bool updated = queue.Update(nonExistingPriority, updateFunction);

        // Assert
        Assert.IsFalse(updated, "Update with function should return false for a non-existing priority.");
        Assert.AreEqual(initialCount, queue.GetCount(), "Queue count should not change.");
    }

    [TestMethod]
    public void Update_NullPriority_ThrowsArgumentNullException()
    {
        // Arrange
        var queue = CreateDefaultQueueWithRefs();
        string nullPriority = null!;
        const string element = "Element";

        // Act & Assert
        Assert.ThrowsException<ArgumentNullException>(() => queue.Update(nullPriority, element));
    }

    [TestMethod]
    public void Update_NullElement_ThrowsArgumentNullException()
    {
        // Arrange
        var queue = CreateDefaultQueue();
        const int priority = 1;
        const string originalElement = "Original";
        queue.TryAdd(priority, originalElement);
        string nullElement = null!;

        // Act & Assert
        Assert.ThrowsException<ArgumentNullException>(() => queue.Update(priority, nullElement));
    }

    [TestMethod]
    public void Update_NullPriorityFunc_ThrowsArgumentNullException()
    {
        // Arrange
        var queue = CreateDefaultQueueWithRefs();
        string nullPriority = null!;
        Func<string, string, string> updateFunction = (_, e) => e;

        // Act & Assert
        Assert.ThrowsException<ArgumentNullException>(() => queue.Update(nullPriority, updateFunction));
    }

    [TestMethod]
    public void Update_NullUpdateFunction_ThrowsArgumentNullException()
    {
        // Arrange
        var queue = CreateDefaultQueue();
        const int priority = 1;
        const string originalElement = "Original";
        queue.TryAdd(priority, originalElement);
        Func<int, string, string> nullUpdateFunction = null!;

        // Act & Assert
        Assert.ThrowsException<ArgumentNullException>(() => queue.Update(priority, nullUpdateFunction));
    }

    [TestMethod]
    public void Update_ElementLogicallyDeletedDuringUpdateAttempt_ReturnsFalse()
    {
        // Arrange
        var queue = CreateDefaultQueue();
        const int priority = 1;
        queue.TryAdd(priority, "Element1");
        bool preDeleted = queue.TryRemove(priority, out _);
        Assert.IsTrue(preDeleted, "Pre-condition: Element should be successfully deleted before update attempt.");
        const string updatedElementValue = "UpdatedElement";

        // Act
        bool updated = queue.Update(priority, updatedElementValue);

        // Assert
        Assert.IsFalse(updated, "Update should return false when the target element was logically deleted before the update occurred.");
    }

    #endregion
    #region ContainsPriority Tests

    [TestMethod]
    public void ContainsPriority_ExistingPriority_ReturnsTrue()
    {
        // Arrange
        var queue = CreateDefaultQueue();
        const int priority1 = 1;
        const int priority5 = 5;
        queue.TryAdd(priority1, "Element1");
        queue.TryAdd(priority5, "Element5");

        // Act
        bool contains1 = queue.ContainsPriority(priority1);
        bool contains5 = queue.ContainsPriority(priority5);

        // Assert
        Assert.IsTrue(contains1, $"ContainsPriority should return true for existing priority {priority1}.");
        Assert.IsTrue(contains5, $"ContainsPriority should return true for existing priority {priority5}.");
    }

    [TestMethod]
    public void ContainsPriority_NonExistingPriority_ReturnsFalse()
    {
        // Arrange
        var queue = CreateDefaultQueue();
        queue.TryAdd(1, "Element1");
        const int nonExistingPriority = 99;

        // Act
        bool contains99 = queue.ContainsPriority(nonExistingPriority);

        // Assert
        Assert.IsFalse(contains99, $"ContainsPriority should return false for non-existing priority {nonExistingPriority}.");
    }

    [TestMethod]
    public void ContainsPriority_EmptyQueue_ReturnsFalse()
    {
        // Arrange
        var queue = CreateDefaultQueue();
        const int priorityToCheck = 1;
        Assert.AreEqual(0, queue.GetCount(), "Pre-condition: Queue must be empty.");

        // Act
        bool contains = queue.ContainsPriority(priorityToCheck);

        // Assert
        Assert.IsFalse(contains, "ContainsPriority should return false for an empty queue.");
    }

    [TestMethod]
    public void ContainsPriority_AfterDelete_ReturnsFalse()
    {
        // Arrange
        var queue = CreateDefaultQueue();
        const int priority = 1;
        queue.TryAdd(priority, "Element1");
        bool preDeleted = queue.TryRemove(priority, out _);
        Assert.IsTrue(preDeleted, "Pre-condition: Element should be successfully deleted.");
        Assert.AreEqual(0, queue.GetCount(), "Pre-condition: Queue count should be 0 after delete.");

        // Act
        bool contains = queue.ContainsPriority(priority);

        // Assert
        Assert.IsFalse(contains, "ContainsPriority should return false for a priority that has been deleted.");
    }

    [TestMethod]
    public void ContainsPriority_NullPriority_ThrowsArgumentNullException()
    {
        // Arrange
        var queue = CreateDefaultQueueWithRefs();
        string nullPriority = null!;

        // Act & Assert
        Assert.ThrowsException<ArgumentNullException>(() => queue.ContainsPriority(nullPriority));
    }

    #endregion
    #region GetCount Tests

    [TestMethod]
    public void GetCount_EmptyQueue_ReturnsZero()
    {
        // Arrange
        var queue = CreateDefaultQueue();

        // Act
        int count = queue.GetCount();

        // Assert
        Assert.AreEqual(0, count, "GetCount should return 0 for an empty queue.");
    }

    [TestMethod]
    public void GetCount_AfterAdds_ReturnsCorrectCount()
    {
        // Arrange
        var queue = CreateDefaultQueue();

        // Act & Assert Step 1 (Add first)
        queue.TryAdd(1, "E1");
        int countAfterFirstAdd = queue.GetCount();
        Assert.AreEqual(1, countAfterFirstAdd, "Count should be 1 after the first add.");

        // Act & Assert Step 2 (Add second)
        queue.TryAdd(2, "E2");
        int countAfterSecondAdd = queue.GetCount();
        Assert.AreEqual(2, countAfterSecondAdd, "Count should be 2 after the second add.");

        // Act & Assert Step 3 (Add duplicate)
        queue.TryAdd(1, "E1Dup");
        int countAfterDuplicateAdd = queue.GetCount();
        Assert.AreEqual(3, countAfterDuplicateAdd, "Count should be 3 after adding an element with a duplicate priority.");
    }

    [TestMethod]
    public void GetCount_AfterDeletes_ReturnsCorrectCount()
    {
        // Arrange
        var queue = CreateDefaultQueue();
        queue.TryAdd(1, "E1");
        queue.TryAdd(2, "E2");
        queue.TryAdd(3, "E3");
        Assert.AreEqual(3, queue.GetCount(), "Pre-condition: Count should be 3 after initial adds.");

        // Act & Assert Step 1 (TryDelete)
        queue.TryRemove(2, out _);
        int countAfterFirstDelete = queue.GetCount();
        Assert.AreEqual(2, countAfterFirstDelete, "Count should be 2 after deleting priority 2.");

        // Act & Assert Step 2 (TryDeleteAbsoluteMin)
        queue.TryDeleteAbsoluteMin(out _);
        int countAfterSecondDelete = queue.GetCount();
        Assert.AreEqual(1, countAfterSecondDelete, "Count should be 1 after deleting the absolute minimum.");

        // Act & Assert Step 3 (TryDeleteMin)
        queue.TryDeleteMin(out _);
        int countAfterThirdDelete = queue.GetCount();
        Assert.AreEqual(0, countAfterThirdDelete, "Count should be 0 after deleting the final element.");
    }

    #endregion
    #region GetEnumerator Tests

    [TestMethod]
    public void GetEnumerator_NonEmptyQueue_EnumeratesPrioritiesInOrder()
    {
        // Arrange
        var queue = CreateDefaultQueue();
        queue.TryAdd(5, "E5");
        queue.TryAdd(1, "E1a");
        queue.TryAdd(3, "E3");
        queue.TryAdd(1, "E1b");
        var expectedPriorities = new List<int> { 1, 1, 3, 5 };
        var actualPriorities = new List<int>();

        // Act
        IEnumerator<int> enumerator = queue.GetEnumerator();
        while (enumerator.MoveNext())
        {
            actualPriorities.Add(enumerator.Current);
        }
        enumerator.Dispose();

        // Assert
        CollectionAssert.AreEqual(expectedPriorities, actualPriorities, "The enumerated priorities should be in the correct order.");
    }

    [TestMethod]
    public void GetEnumerator_EmptyQueue_ReturnsEmptyEnumerator()
    {
        // Arrange
        var queue = CreateDefaultQueue();
        var expectedPriorities = new List<int>();
        var actualPriorities = new List<int>();
        Assert.AreEqual(0, queue.GetCount(), "Pre-condition: Queue must be empty.");
        IEnumerator<int> enumerator = null!;

        // Act
        try
        {
            enumerator = queue.GetEnumerator();
            while (enumerator.MoveNext())
            {
                actualPriorities.Add(enumerator.Current);
            }
        }
        finally
        {
            enumerator?.Dispose();
        }

        // Assert
        Assert.AreEqual(0, actualPriorities.Count, "Manually enumerating an empty queue should result in an empty list.");
        CollectionAssert.AreEqual(expectedPriorities, actualPriorities, "The enumerated list from an empty queue should be empty.");
    }

    [TestMethod]
    public void GetEnumerator_DuringConcurrentModifications_MayReflectSomeChanges()
    {
        // Arrange
        var queue = CreateDefaultQueue();
        queue.TryAdd(1, "E1");
        queue.TryAdd(3, "E3");
        queue.TryAdd(5, "E5");
        IEnumerator<int> enumerator = null!;
        bool movedFirst;
        int firstElement = -1;
        var remainingElements = new List<int>();

        try
        {
            // Act - Step 1: Start Enumeration
            enumerator = queue.GetEnumerator();
            movedFirst = enumerator.MoveNext();
            if (movedFirst)
            {
                firstElement = enumerator.Current;
            }

            // Act - Step 2: Concurrent Modifications (simulated)
            queue.TryAdd(0, "E0");
            queue.TryRemove(3, out _);

            // Act - Step 3: Continue Enumeration (already manual)
            while (enumerator.MoveNext())
            {
                remainingElements.Add(enumerator.Current);
            }
        }
        finally
        {
             enumerator?.Dispose();
        }

        // Assert
        Assert.IsTrue(movedFirst, "Enumerator should successfully move to the first element.");
        Assert.AreEqual(1, firstElement, "The first element enumerated should be 1.");
        Assert.IsFalse(remainingElements.Contains(3), "The deleted element (3) should not be present in the remaining enumerated items.");
        Assert.IsTrue(remainingElements.Contains(5), "The element (5), which was after the deleted one and not deleted itself, should be present.");
        Console.WriteLine($"Remaining elements after concurrent modification: {string.Join(", ", remainingElements)}");
    }

    #endregion
    #region Concurrency Tests

    [TestMethod]
    public void ConcurrentAdd_MultipleThreads_AddsAllElements()
    {
        // Arrange
        const int numThreads = 10;
        const int itemsPerThread = 100;
        const int totalExpectedCount = numThreads * itemsPerThread;
        var queue = CreateDefaultQueue(maxSize: totalExpectedCount + 10);
        var tasks = new List<Task>();

        // Prepare the tasks (still part of Arrange)
        for (int i = 0; i < numThreads; i++)
        {
            int threadId = i;
            tasks.Add(Task.Run(() => AddAction(threadId)));
        }

        // Act
        Task.WaitAll(tasks.ToArray());
        int finalCount = queue.GetCount();

        // Assert
        Assert.AreEqual(totalExpectedCount, finalCount, $"The final count should match the total number of elements added ({totalExpectedCount}).");
        return;

        // Define the action each thread will perform (part of Arrange)
        void AddAction(int threadId)
        {
            for (int j = 0; j < itemsPerThread; j++)
            {
                int priority = threadId * itemsPerThread + j;
                queue.TryAdd(priority, $"Element{priority}");
            }
        }
    }

    [TestMethod]
    public void ConcurrentAddDelete_MultipleThreads_MaintainsReasonableState()
    {
        // Arrange
        const int numAddThreads = 5;
        const int numDeleteThreads = 3;
        const int itemsPerAddThread = 100;
        const int itemsPerDeleteThread = 50;
        const int prePopulateCount = 200;
        const int maxQueueSize = (numAddThreads * itemsPerAddThread) + prePopulateCount + 10;
        var queue = CreateDefaultQueue(maxSize: maxQueueSize);
        var tasks = new List<Task>();

        for (int i = 0; i < prePopulateCount; i++)
        {
            // Use priorities that likely won't overlap heavily with concurrent adds initially
            queue.TryAdd(-(i + 1), $"PrePop{-i}");
        }
        int initialPopulatedCount = queue.GetCount();
        Assert.AreEqual(prePopulateCount, initialPopulatedCount, "Pre-condition: Queue should be pre-populated.");

        Action deleteAction = () =>
        {
            int successfulDeletes = 0;
            for (int j = 0; j < itemsPerDeleteThread; j++)
            {
                // Try deleting - even if it fails, it simulates load
                if (queue.TryDeleteAbsoluteMin(out _))
                {
                    successfulDeletes++;
                }
            }
            Console.WriteLine($"Delete thread completed with {successfulDeletes} successful deletes.");
        };

        for (int i = 0; i < numAddThreads; i++)
        {
            int threadId = i;
            tasks.Add(Task.Run(() => AddAction(threadId)));
        }

        for (int i = 0; i < numDeleteThreads; i++)
        {
             tasks.Add(Task.Run(deleteAction));
        }

        // Theoretical count range:
        // Min: prePopulateCount + adds - deletes
        // Max: prePopulateCount + adds
        const int totalAdds = numAddThreads * itemsPerAddThread;
        const int totalDeleteAttempts = numDeleteThreads * itemsPerDeleteThread;
        int expectedMinCount = Math.Max(0, initialPopulatedCount + totalAdds - totalDeleteAttempts);
        int expectedMaxCount = initialPopulatedCount + totalAdds;
        int minExpectedDeletionTasks = Math.Max(1, totalDeleteAttempts / 10);

        // Act
        Task.WaitAll(tasks.ToArray());
        int finalCount = queue.GetCount();

        // Assert
        Assert.IsTrue(finalCount >= 0, "Final count should not be negative.");
        Console.WriteLine($"Concurrent Add/Delete: Final Count = {finalCount}, Theoretical Range = [{expectedMinCount} - {expectedMaxCount}]");
        Assert.IsTrue(finalCount >= expectedMinCount, $"Final count {finalCount} is less than theoretical minimum {expectedMinCount}");
        Assert.IsTrue(finalCount <= expectedMaxCount, $"Final count {finalCount} is more than theoretical maximum {expectedMaxCount}");
        _mockTaskOrchestrator.Verify(o => o.Run(It.IsAny<Func<Task>>()), Times.AtLeast(minExpectedDeletionTasks),
            $"At least {minExpectedDeletionTasks} background removal tasks should have been scheduled (actual verification may vary based on execution).");
        return;

        // Define Add action
        void AddAction(int threadId)
        {
            for (int j = 0; j < itemsPerAddThread; j++)
            {
                int priority = threadId * itemsPerAddThread + j;
                queue.TryAdd(priority, $"Element{priority}");
            }
        }
    }

    [TestMethod]
    [Timeout(15000)]
    public void TryDeleteMin_HighContention_RemovesCorrectNumberOfElements()
    {
        // Arrange
        const int maxSize = 500;
        const int initialElements = 400;
        const int numThreads = 10;
        const int deletesPerThread = 30;
        var queue = CreateDefaultQueue(maxSize: maxSize);
        var deletedElements = new ConcurrentBag<string>();
        var exceptions = new ConcurrentBag<Exception>();

        for (int i = 0; i < initialElements; i++)
        {
            queue.TryAdd(i, $"Element{i}");
        }
        Assert.AreEqual(initialElements, queue.GetCount(), "Pre-condition: Initial count mismatch.");
        var tasks = new List<Task>();

        // Act
        for (int i = 0; i < numThreads; i++)
        {
            tasks.Add(Task.Run(() => {
                try
                {
                    for (int j = 0; j < deletesPerThread; j++)
                    {
                        if (queue.TryDeleteMin(out string element))
                        {
                            deletedElements.Add(element);
                        }
                        // Small delay to allow other threads to run, increase contention slightly
                        // Remove or adjust this depending on desired contention level
                        // Task.Delay(1).Wait();
                    }
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            }));
        }

        Task.WaitAll(tasks.ToArray());

        // Assert
        Assert.AreEqual(0, exceptions.Count, $"Exceptions occurred during concurrent deletes: {string.Join("; ", exceptions.Select(e => e.Message))}");
        int expectedFinalCount = initialElements - deletedElements.Count;
        Assert.AreEqual(expectedFinalCount, queue.GetCount(), "Final count should reflect successful deletes.");
        Assert.AreEqual(numThreads * deletesPerThread, deletedElements.Count + queue.GetCount() - (initialElements - numThreads*deletesPerThread) ,"Total elements accounted for mismatch");
        // Verify background removals were scheduled
        // We expect one background task per successful delete
        _mockTaskOrchestrator.Verify(o => o.Run(It.IsAny<Func<Task>>()), Times.Exactly(deletedElements.Count),
            "Incorrect number of background removal tasks scheduled.");
        Console.WriteLine($"Deleted {deletedElements.Count} elements under high contention.");
        foreach (var element in deletedElements.OrderBy(e => int.Parse(e.Replace("Element",""))).Take(10)) { Console.WriteLine($"Sample deleted: {element}"); }
    }

    [TestMethod]
    [Timeout(15000)]
    public void TryAdd_AtMaxSize_ConcurrentlyTriggersDeleteMin()
    {
        // Arrange
        const int maxSize = 50;
        const int numThreads = 20;
        var queue = CreateDefaultQueue(maxSize: maxSize);
        var addedElements = new ConcurrentDictionary<string, bool>();
        var exceptions = new ConcurrentBag<Exception>();
        int initialMinPriority = -1;

        for (int i = 0; i < maxSize; i++)
        {
            int priority = i + 1;
            queue.TryAdd(priority, $"Initial{priority}");
            if (i == 0) initialMinPriority = priority;
        }
        Assert.AreEqual(maxSize, queue.GetCount(), "Pre-condition: Queue not filled to max size.");
        Assert.AreEqual(1, initialMinPriority, "Pre-condition: Initial min priority incorrect.");
        var tasks = new List<Task>();

        // Act
        for (int i = 0; i < numThreads; i++)
        {
            int itemIndex = i;
            tasks.Add(Task.Run(() => {
                try
                {
                    int priority = maxSize + 10 + itemIndex;
                    string element = $"Added{priority}";
                    if (queue.TryAdd(priority, element))
                    {
                        addedElements.TryAdd(element, true);
                    }
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            }));
        }

        Task.WaitAll(tasks.ToArray());

        // Assert
        Assert.AreEqual(0, exceptions.Count, $"Exceptions occurred during concurrent adds at max size: {string.Join("; ", exceptions.Select(e => e.Message))}");
        Assert.AreEqual(maxSize, queue.GetCount(), "Queue count should remain at max size.");
        _mockTaskOrchestrator.Verify(o => o.Run(It.IsAny<Func<Task>>()), Times.AtMost(addedElements.Count),
            "Incorrect number of background removal tasks scheduled for adds at max size.");
        int addedCount = addedElements.Count;
        Console.WriteLine($"Successfully added {addedCount} elements concurrently at max size, triggering deletes.");
        bool originalMinStillPresent = queue.ContainsPriority(initialMinPriority);
        Console.WriteLine($"Original min priority {initialMinPriority} still present? {originalMinStillPresent}");
    }

    [TestMethod]
    [Timeout(20000)]
    public void TryAdd_TryDeleteMin_ConcurrentNearMaxSize()
    {
        // Arrange
        const int maxSize = 100;
        const int initialElements = 80;
        const int numAddThreads = 10;
        const int addsPerThread = 5;
        const int numDeleteThreads = 8;
        const int deletesPerThread = 5;
        var queue = CreateDefaultQueue(maxSize: maxSize);
        var exceptions = new ConcurrentBag<Exception>();
        long itemsAddedSuccessfully = 0;
        long itemsDeletedSuccessfully = 0;

        for (int i = 0; i < initialElements; i++)
        {
            queue.TryAdd(i, $"Initial{i}");
        }
        Assert.AreEqual(initialElements, queue.GetCount(), "Pre-condition: Initial count mismatch.");
        var tasks = new List<Task>();

        // Add tasks
        for (int i = 0; i < numAddThreads; i++)
        {
            int threadId = i;
            tasks.Add(Task.Run(() => {
                try
                {
                    for (int j = 0; j < addsPerThread; j++)
                    {
                        int priority = initialElements + (threadId * addsPerThread) + j;
                        if (queue.TryAdd(priority, $"Added{priority}"))
                        {
                            Interlocked.Increment(ref itemsAddedSuccessfully);
                        }
                    }
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            }));
        }

        for (int i = 0; i < numDeleteThreads; i++)
        {
            tasks.Add(Task.Run(() => {
                try
                {
                    for (int j = 0; j < deletesPerThread; j++)
                    {
                        if (queue.TryDeleteMin(out _))
                        {
                             Interlocked.Increment(ref itemsDeletedSuccessfully);
                        }
                    }
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
            }));
        }

        // Act
        Task.WaitAll(tasks.ToArray());
        int finalCount = queue.GetCount();

        // Assert
        Assert.AreEqual(0, exceptions.Count, $"Exceptions occurred during concurrent add/delete near max size: {string.Join("; ", exceptions.Select(e => e.Message))}");
        Assert.IsTrue(finalCount <= maxSize, $"Final count {finalCount} exceeded max size {maxSize}.");
        Assert.IsTrue(finalCount >= 0, $"Final count {finalCount} is negative.");
        Console.WriteLine($"Concurrent Add/Delete near Max: Added={itemsAddedSuccessfully}, Deleted={itemsDeletedSuccessfully}, FinalCount={finalCount}");
        Assert.IsTrue(finalCount <= initialElements + Interlocked.Read(ref itemsAddedSuccessfully), "Final count seems too high relative to adds.");
        // Verify background tasks were scheduled. We can only be certain about the ones
        // triggered by EXPLICIT successful deletes. Deletes triggered by Add are non-deterministic
        int successfulExplicitDeletes = (int)Interlocked.Read(ref itemsDeletedSuccessfully);
        _mockTaskOrchestrator.Verify(o => o.Run(It.IsAny<Func<Task>>()), Times.AtLeast(successfulExplicitDeletes),
            $"Expected at least {successfulExplicitDeletes} background removal tasks (from explicit deletes). Actual calls depend on concurrent interactions.");
    }

    #endregion
    #region Internal SkipListNode Tests

    [TestMethod]
    public void SkipListNode_Constructor_HeadNode_InitializesCorrectly()
    {
        // Arrange
        const int height = 5;

        // Act
        var head = CreateHeadNode(height);

        // Assert
        Assert.AreEqual(SkipListNode<int, string>.NodeType.Head, head.Type, "Type should be Head.");
        Assert.AreEqual(long.MinValue, head.SequenceNumber, "SequenceNumber should be MinValue for Head.");
        Assert.AreEqual(height, head.TopLevel, "TopLevel should match constructor height.");
        Assert.AreEqual(0, head.Priority, "Priority should be default for Head.");
        Assert.IsNull(head.Element, "Element should be default for Head.");
    }

    [TestMethod]
    public void SkipListNode_Constructor_TailNode_InitializesCorrectly()
    {
        // Arrange
        const int height = 5;

        // Act
        var tail = CreateTailNode(height);

        // Assert
        Assert.AreEqual(SkipListNode<int, string>.NodeType.Tail, tail.Type, "Type should be Tail.");
        Assert.AreEqual(long.MaxValue, tail.SequenceNumber, "SequenceNumber should be MaxValue for Tail.");
        Assert.AreEqual(height, tail.TopLevel, "TopLevel should match constructor height.");
    }

    [TestMethod]
    public void SkipListNode_Constructor_DataNode_InitializesCorrectly()
    {
        // Arrange
        const int priority = 10;
        const string element = "Data";
        const int height = 3;
        long seqBefore = GetCurrentSequenceGeneratorValue();

        // Act
        var dataNode = CreateNode(priority, element, height);
        long seqAfter = GetCurrentSequenceGeneratorValue();

        // Assert
        Assert.AreEqual(SkipListNode<int, string>.NodeType.Data, dataNode.Type, "Type should be Data.");
        Assert.AreEqual(priority, dataNode.Priority, "Priority should match constructor.");
        Assert.AreEqual(element, dataNode.Element, "Element should match constructor.");
        Assert.AreEqual(height, dataNode.TopLevel, "TopLevel should match constructor.");
        Assert.AreEqual(seqBefore + 1, dataNode.SequenceNumber, "Sequence number should increment by 1.");
        Assert.AreEqual(seqAfter, dataNode.SequenceNumber, "Sequence number should match static generator after increment.");
        Assert.IsTrue(dataNode.IsInserted, "IsInserted should be true by default from helper.");
        Assert.IsFalse(dataNode.IsDeleted, "IsDeleted should be false by default from helper.");
    }

    [TestMethod]
    public void SkipListNode_Properties_SetAndGet()
    {
        // Arrange
        var node = CreateNode(1, "Test", 2, isInserted: false, isDeleted: false);

        // Act & Assert - IsInserted
        node.IsInserted = true;
        Assert.IsTrue(node.IsInserted, "IsInserted getter should return true after setting to true.");
        node.IsInserted = false;
        Assert.IsFalse(node.IsInserted, "IsInserted getter should return false after setting to false.");

        // Act & Assert - IsDeleted
        node.IsDeleted = true;
        Assert.IsTrue(node.IsDeleted, "IsDeleted getter should return true after setting to true.");
        node.IsDeleted = false;
        Assert.IsFalse(node.IsDeleted, "IsDeleted getter should return false after setting to false.");
    }

    [TestMethod]
    public void SkipListNode_GetSetNextNode_WorksCorrectly()
    {
        // Arrange
        var node1 = CreateNode(1, "N1", 2);
        var node2 = CreateNode(2, "N2", 2);
        const int level = 1;

        // Act
        node1.SetNextNode(level, node2);
        var next = node1.GetNextNode(level);

        // Assert
        Assert.AreSame(node2, next, "GetNextNode should return the node set by SetNextNode.");
    }

    [TestMethod]
    [DataRow(1, "N1", 0, 2, "N2", 0, -1)]
    [DataRow(2, "N1", 0, 1, "N2", 0, 1)]
    public void SkipListNode_CompareTo_PriorityComparison(int p1, string e1, int h1, int p2, string e2, int h2, int expected)
    {
        // Arrange
        var node1 = CreateNode(p1, e1, h1);
        var node2 = CreateNode(p2, e2, h2);

        // Act
        int result1 = node1.CompareTo(node2);
        int result2 = node2.CompareTo(node1);

        // Assert
        Assert.AreEqual(expected, result1, "Comparison Node1 vs Node2 failed.");
        Assert.AreEqual(-expected, result2, "Reverse comparison Node2 vs Node1 failed.");
    }

    [TestMethod]
    public void SkipListNode_CompareTo_SequenceComparison()
    {
        // Arrange
        var node1 = CreateNode(5, "N1", 2);
        var node2 = CreateNode(5, "N2", 2);
        Assert.IsTrue(node1.SequenceNumber < node2.SequenceNumber, "Pre-condition: Node1 sequence < Node2 sequence.");

        // Act
        int result1 = node1.CompareTo(node2);
        int result2 = node2.CompareTo(node1);

        // Assert
        Assert.AreEqual(-1, result1, "Node1 should be less than Node2 (same priority, lower sequence).");
        Assert.AreEqual(1, result2, "Node2 should be greater than Node1 (same priority, higher sequence).");
    }

    [TestMethod]
    public void SkipListNode_CompareTo_HeadTailComparison()
    {
        // Arrange
        var head = CreateHeadNode(5);
        var tail = CreateTailNode(5);
        var data = CreateNode(10, "Data", 5);

        // Act & Assert
        Assert.AreEqual(-1, head.CompareTo(data), "Head vs Data");
        Assert.AreEqual(1, data.CompareTo(head), "Data vs Head");
        Assert.AreEqual(-1, head.CompareTo(tail), "Head vs Tail");
        Assert.AreEqual(1, tail.CompareTo(head), "Tail vs Head");
        Assert.AreEqual(-1, data.CompareTo(tail), "Data vs Tail");
        Assert.AreEqual(1, tail.CompareTo(data), "Tail vs Data");
        Assert.AreEqual(0, head.CompareTo(head), "Head vs Head");
        Assert.AreEqual(0, tail.CompareTo(tail), "Tail vs Tail");
        Assert.AreEqual(0, data.CompareTo(data), "Data vs Data");
    }

    [TestMethod]
    [DataRow(1, 5, -1)]
    [DataRow(5, 5, 0)]
    [DataRow(9, 5, 1)]
    public void SkipListNode_CompareToPriority_DataNode(int nodePriority, int comparePriority, int expected)
    {
        // Arrange
        var node = CreateNode(nodePriority, "Data", 2);

        // Act
        int result = node.CompareToPriority(comparePriority);

        // Assert
        Assert.AreEqual(expected, result);
    }

    [TestMethod]
    public void SkipListNode_CompareToPriority_HeadTailNodes()
    {
        // Arrange
        var head = CreateHeadNode(5);
        var tail = CreateTailNode(5);
        const int comparePriority = 10;

        // Act
        int headResult = head.CompareToPriority(comparePriority);
        int tailResult = tail.CompareToPriority(comparePriority);

        // Assert
        Assert.AreEqual(-1, headResult, "Head should always compare as less.");
        Assert.AreEqual(1, tailResult, "Tail should always compare as greater.");
    }

    #endregion
    #region Internal SearchResult Tests

    [TestMethod]
    public void SearchResult_IsFound_CorrectBasedOnLevelFound()
    {
        // Arrange
        var nodes = new SkipListNode<int, string>[1];
        var dummyNode = CreateNode(0,"Dummy",0);
        var arrayPool = ArrayPool<SkipListNode<int, string>>.Shared;
        var resultFound = new SearchResult<int, string>(0, nodes, nodes, dummyNode, arrayPool, 1);
        var resultNotFoundLevel = new SearchResult<int, string>(-1, nodes, nodes, null, arrayPool, 1);
        var resultNotFoundNode = new SearchResult<int, string>(0, nodes, nodes, null, arrayPool, 1); // Level found but node is null

        // Act
        bool isFound1 = resultFound.IsFound;
        bool isFound2 = resultNotFoundLevel.IsFound;
        bool isFound3 = resultNotFoundNode.IsFound;

        // Assert
        Assert.IsTrue(isFound1, "IsFound should be true when LevelFound >= 0 AND NodeFound is not null.");
        Assert.IsFalse(isFound2, "IsFound should be false when LevelFound == -1.");
        Assert.IsFalse(isFound3, "IsFound should be false when NodeFound is null, even if LevelFound >= 0.");
    }

    [TestMethod]
    public void SearchResult_GetPredecessorSuccessor_ReturnsCorrectNode()
    {
        // Arrange
        var pred1 = CreateNode(1, "P1", 0);
        var succ1 = CreateNode(3, "S1", 0);
        var nodeThatWasFound = pred1.GetNextNode(0);
        var preds = new[] { pred1 };
        var succs = new[] { succ1 };
        var arrayPool = ArrayPool<SkipListNode<int, string>>.Shared;
        var result = new SearchResult<int, string>(0, preds, succs, nodeThatWasFound, arrayPool, 1);
        const int level = 0;

        // Act
        var retrievedPred = result.GetPredecessor(level);
        var retrievedSucc = result.GetSuccessor(level);

        // Assert
        Assert.AreSame(pred1, retrievedPred, "GetPredecessor should return the correct node.");
        Assert.AreSame(succ1, retrievedSucc, "GetSuccessor should return the correct node.");
    }

    [TestMethod]
    public void SearchResult_GetNodeFound_Success()
    {
        // Arrange
        var nodeFound = CreateNode(5, "Found", 1);
        var preds = new[] { CreateNode(1, "P0", 1), CreateNode(3, "P1", 1) };
        var succs = new[] { CreateNode(7, "S0", 1), nodeFound.GetNextNode(1) };
        const int levelFound = 1;
        var arrayPool = ArrayPool<SkipListNode<int, string>>.Shared;
        var result = new SearchResult<int, string>(levelFound, preds, succs, nodeFound, arrayPool, 2);
        Assert.IsTrue(result.IsFound, "Pre-condition: IsFound should be true.");

        // Act
        var retrievedNode = result.GetNodeFound();

        // Assert
        Assert.AreSame(nodeFound, retrievedNode, "GetNodeFound should return the explicitly passed NodeFound instance.");
    }

    [TestMethod]
    public void SearchResult_GetNodeFound_ThrowsWhenNotFound()
    {
        // Arrange
        var preds = new SkipListNode<int, string>[1];
        var succs = new SkipListNode<int, string>[1];
        var arrayPool = ArrayPool<SkipListNode<int, string>>.Shared;
        var result = new SearchResult<int, string>(-1, preds, succs, null, arrayPool, 1);
        Assert.IsFalse(result.IsFound, "Pre-condition: IsFound should be false.");

        // Act & Assert
        Assert.ThrowsException<InvalidOperationException>(() => result.GetNodeFound(), "Should throw when IsFound is false.");
    }

    #endregion
    #region Internal SprayParameters Tests

    [TestMethod]
    [DataRow(100, 10, 1, 1, 5, 97, 1)]
    [DataRow(1000, 15, 2, 3, 8, 987, 1)]
    [DataRow(2, 5, 1, 1, 1, 1, 1)]
    [DataRow(1000000, 30, 1, 1, 14, 2636, 2)]
    public void SprayParameters_CalculateParameters_ReturnsCorrectValues(int count, int topLevel, int k, int m, int expectedH, int expectedY, int expectedD)
    {
        // Act
        var parameters = SprayParameters.CalculateParameters(count, topLevel, k, m);

        // Assert
        Console.WriteLine($"Input: N={count}, L={topLevel}, K={k}, M={m}");
        Console.WriteLine($"Calculated: H={parameters.StartHeight}, Y={parameters.MaxJumpLength}, D={parameters.DescentLength}");
        Console.WriteLine($"Expected:   H={expectedH}, Y={expectedY}, D={expectedD}");
        Assert.AreEqual(expectedD, parameters.DescentLength, "DescentLength (d) is incorrect.");
        Assert.AreEqual(expectedH, parameters.StartHeight, "StartHeight (h) is incorrect.");
        Assert.AreEqual(expectedY, parameters.MaxJumpLength, "MaxJumpLength (y) is incorrect.");
    }

    [TestMethod]
    public void SprayParameters_CalculateParameters_HandlesLogEdgeCases()
    {
        // Arrange
        const int topLevelForN2 = 5;
        const int k = 1;
        const int m=1;

        // Act
        var paramsFor1 = SprayParameters.CalculateParameters(1, topLevelForN2, k, m);
        var paramsFor2 = SprayParameters.CalculateParameters(2, topLevelForN2, k, m);

        // Assert
        Assert.AreEqual(paramsFor2.DescentLength, paramsFor1.DescentLength, "Descent length should be same for count 1 and 2");
        Assert.AreEqual(paramsFor2.StartHeight, paramsFor1.StartHeight, "Start height should be same for count 1 and 2");
        Assert.AreEqual(paramsFor2.MaxJumpLength, paramsFor1.MaxJumpLength, "Max jump length should be same for count 1 and 2");
    }

    #endregion
    #region Internal Helper Method Tests

    [TestMethod]
    public void NodeIsInvalidOrDeleted_NullNode_ReturnsTrue()
    {
        // Arrange
        SkipListNode<int, string>? node = null;

        // Act
        bool result = ConcurrentPriorityQueue<int, string>.NodeIsInvalidOrDeleted(node);

        // Assert
        Assert.IsTrue(result);
    }

    [TestMethod]
    public void NodeIsInvalidOrDeleted_HeadTailNodes_ReturnsTrue()
    {
        // Arrange
        var head = CreateHeadNode(5);
        var tail = CreateTailNode(5);

        // Act
        bool headResult = ConcurrentPriorityQueue<int, string>.NodeIsInvalidOrDeleted(head);
        bool tailResult = ConcurrentPriorityQueue<int, string>.NodeIsInvalidOrDeleted(tail);

        // Assert
        Assert.IsTrue(headResult, "Head node should be considered invalid.");
        Assert.IsTrue(tailResult, "Tail node should be considered invalid.");
    }

    [TestMethod]
    public void NodeIsInvalidOrDeleted_NotInsertedNode_ReturnsTrue()
    {
        // Arrange
        var node = CreateNode(1, "Test", 2, isInserted: false);

        // Act
        bool result = ConcurrentPriorityQueue<int, string>.NodeIsInvalidOrDeleted(node);

        // Assert
        Assert.IsTrue(result);
    }

    [TestMethod]
    public void NodeIsInvalidOrDeleted_DeletedNode_ReturnsTrue()
    {
        // Arrange
        var node = CreateNode(1, "Test", 2, isDeleted: true);

        // Act
        bool result = ConcurrentPriorityQueue<int, string>.NodeIsInvalidOrDeleted(node);

        // Assert
        Assert.IsTrue(result);
    }

    [TestMethod]
    public void NodeIsInvalidOrDeleted_ValidDataNode_ReturnsFalse()
    {
        // Arrange
        var node = CreateNode(1, "Test", 2, isInserted: true, isDeleted: false);

        // Act
        bool result = ConcurrentPriorityQueue<int, string>.NodeIsInvalidOrDeleted(node);

        // Assert
        Assert.IsFalse(result);
    }

    [TestMethod]
    public void LogicallyDeleteNode_HeadTailNodes_ReturnsFalse()
    {
        // Arrange
        var head = CreateHeadNode(5);
        var tail = CreateTailNode(5);

        // Act
        bool headResult = ConcurrentPriorityQueue<int, string>.LogicallyDeleteNode(head);
        bool tailResult = ConcurrentPriorityQueue<int, string>.LogicallyDeleteNode(tail);

        // Assert
        Assert.IsFalse(headResult, "Should return false for Head node.");
        Assert.IsFalse(tailResult, "Should return false for Tail node.");
        Assert.IsFalse(head.IsDeleted, "Head node IsDeleted state should not change.");
        Assert.IsFalse(tail.IsDeleted, "Tail node IsDeleted state should not change.");
    }

    [TestMethod]
    public void LogicallyDeleteNode_AlreadyDeletedNode_ReturnsFalseAndUnlocks()
    {
        // Arrange
        var node = CreateNode(1, "Test", 2, isDeleted: true); // Node starts deleted

        // Act
        bool result = ConcurrentPriorityQueue<int, string>.LogicallyDeleteNode(node);

        // Assert
        Assert.IsFalse(result, "Should return false if already deleted.");
        Assert.IsTrue(node.IsDeleted, "IsDeleted state should remain true.");

        bool canRelock = node.TryEnter();
        if (canRelock)
        {
            node.Unlock();
        }
        Assert.IsTrue(canRelock, "Node lock should have been released by LogicallyDeleteNode when returning false after acquiring lock.");
    }

    [TestMethod]
    public void LogicallyDeleteNode_ValidDataNode_ReturnsTrueAndSetsFlag()
    {
        // Arrange
        var node = CreateNode(1, "Test", 2, isInserted: true, isDeleted: false);

        // Act
        bool result = ConcurrentPriorityQueue<int, string>.LogicallyDeleteNode(node);

        // Assert
        Assert.IsTrue(result, "Should return true for successful logical delete.");
        Assert.IsTrue(node.IsDeleted, "IsDeleted flag should be set to true.");
        node.Unlock();
    }

    [TestMethod]
    [Timeout(5000)]
    public void LogicallyDeleteNode_Contention_ReturnsFalse()
    {
        // Arrange
        var node = CreateNode(1, "Test", 2, isInserted: true, isDeleted: false);
        var lockWasAcquiredEvent = new ManualResetEventSlim(false);
        var releaseLockEvent = new ManualResetEventSlim(false);
        Exception? taskException = null;

        // Start a background task to acquire and hold the lock
        var lockTask = Task.Run(() => {
            try
            {
                node.Lock();
                lockWasAcquiredEvent.Set();
                releaseLockEvent.Wait();
            }
            catch(Exception ex)
            {
                taskException = ex;
                lockWasAcquiredEvent.Set();
            }
            finally
            {
                node.Unlock();
            }
        });

        try
        {
            // Wait for the background task to acquire the lock
            if (!lockWasAcquiredEvent.Wait(TimeSpan.FromSeconds(2)))
            {
                Assert.Fail("Background task failed to acquire lock in time.");
            }
            Assert.IsNull(taskException, $"Background task threw an exception: {taskException}");

            bool result = ConcurrentPriorityQueue<int, string>.LogicallyDeleteNode(node);

            // Assert
            Assert.IsFalse(result, "Should return false when lock cannot be acquired immediately due to contention from another thread.");
            Assert.IsFalse(node.IsDeleted, "IsDeleted flag should not be changed if lock wasn't acquired.");
        }
        finally
        {
            releaseLockEvent.Set();
            if (!lockTask.Wait(TimeSpan.FromSeconds(2)))
            {
                Console.WriteLine("Warning: Background lock task did not complete cleanly.");
            }
        }
    }

    [TestMethod]
    public void GenerateLevel_StaysWithinBounds()
    {
        // Arrange
        const int maxSize = 1000;
        const double probability = 0.5;
        var options = Options.Create(new ConcurrentPriorityQueueOptions { MaxSize = maxSize, PromotionProbability = probability });
        var queue = new ConcurrentPriorityQueue<int, string>(
            _mockTaskOrchestrator.Object,
            _intComparer,
            _mockLoggerFactory.Object,
            _mockMeterFactory.Object,
            options);
        int expectedTopLevel = GetInstanceField<int>(queue, "_topLevel");
        const int iterations = 5000;
        bool levelOutOfLowerBound = false;
        bool levelOutOfUpperBound = false;

        // Act
        for (int i = 0; i < iterations; i++)
        {
            int level = queue.GenerateLevel();
            if (level < 0) levelOutOfLowerBound = true;
            if (level > expectedTopLevel) levelOutOfUpperBound = true;
            if (levelOutOfLowerBound || levelOutOfUpperBound) break;
        }

        // Assert
        Assert.IsFalse(levelOutOfLowerBound, "Generated level should never be less than 0.");
        Assert.IsFalse(levelOutOfUpperBound, $"Generated level should never be greater than calculated topLevel {expectedTopLevel}.");
    }

    [TestMethod]
    public void ValidateInsertion_ValidPath_ReturnsTrue()
    {
        // Arrange
        const int level = 0;
        const int insertLevel = 0;
        var pred = CreateNode(1, "P", 1);
        var succ = CreateNode(3, "S", 1);
        pred.SetNextNode(level, succ);
        var preds = new[] { pred };
        var succs = new[] { succ };
        var arrayPool = ArrayPool<SkipListNode<int, string>>.Shared;
        var searchResult = new SearchResult<int, string>(-1, preds, succs, null, arrayPool, 1);
        int highestLocked = -1;

        // Act
        bool isValid = ConcurrentPriorityQueue<int, string>.ValidateInsertion(searchResult, insertLevel, ref highestLocked);

        // Assert
        Assert.IsTrue(isValid);
        Assert.AreEqual(insertLevel, highestLocked);
        if (highestLocked >= 0) searchResult.GetPredecessor(highestLocked).Unlock();
    }

    [TestMethod]
    public void ValidateInsertion_DeletedPredecessor_ReturnsFalse()
    {
        // Arrange
        const int level = 0;
        const int insertLevel = 0;
        var pred = CreateNode(1, "P", 1, isDeleted: true);
        var succ = CreateNode(3, "S", 1);
        pred.SetNextNode(level, succ);
        var preds = new[] { pred };
        var succs = new[] { succ };
        var arrayPool = ArrayPool<SkipListNode<int, string>>.Shared;
        var searchResult = new SearchResult<int, string>(-1, preds, succs, null, arrayPool, 1);
        int highestLocked = -1;

        // Act
        bool isValid = ConcurrentPriorityQueue<int, string>.ValidateInsertion(searchResult, insertLevel, ref highestLocked);

        // Assert
        Assert.IsFalse(isValid);
        Assert.AreEqual(level, highestLocked);
        if (highestLocked >= 0) searchResult.GetPredecessor(highestLocked)?.Unlock();
    }

    [TestMethod]
    public void ValidateInsertion_DeletedSuccessor_ReturnsFalse()
    {
        // Arrange
        const int level = 0;
        const int insertLevel = 0;
        var pred = CreateNode(1, "P", 1);
        var succ = CreateNode(3, "S", 1, isDeleted: true);
        pred.SetNextNode(level, succ);
        var preds = new[] { pred };
        var succs = new[] { succ };
        var arrayPool = ArrayPool<SkipListNode<int, string>>.Shared;
        var searchResult = new SearchResult<int, string>(-1, preds, succs, null, arrayPool, 1);
        int highestLocked = -1;

        // Act
        bool isValid = ConcurrentPriorityQueue<int, string>.ValidateInsertion(searchResult, insertLevel, ref highestLocked);

        // Assert
        Assert.IsFalse(isValid);
        Assert.AreEqual(level, highestLocked);
        if (highestLocked >= 0) searchResult.GetPredecessor(highestLocked)?.Unlock();
    }

    [TestMethod]
    public void ValidateInsertion_BrokenLink_ReturnsFalse()
    {
        // Arrange
        const int level = 0;
        const int insertLevel = 0;
        var pred = CreateNode(1, "P", 1);
        var succ = CreateNode(3, "S", 1);
        var another = CreateNode(2, "A", 1);
        pred.SetNextNode(level, another);
        var preds = new[] { pred };
        var succs = new[] { succ };
        var arrayPool = ArrayPool<SkipListNode<int, string>>.Shared;
        var searchResult = new SearchResult<int, string>(-1, preds, succs, null, arrayPool, 1);
        int highestLocked = -1;

        // Act
        bool isValid = ConcurrentPriorityQueue<int, string>.ValidateInsertion(searchResult, insertLevel, ref highestLocked);

        // Assert
        Assert.IsFalse(isValid);
        Assert.AreEqual(level, highestLocked);
        if (highestLocked >= 0) searchResult.GetPredecessor(highestLocked)?.Unlock();
    }

    [TestMethod]
    public void InsertNode_LinksCorrectly()
    {
        // Arrange
        const int level = 0;
        const int insertLevel = 0;
        var pred = CreateNode(1, "P", 1);
        var succ = CreateNode(3, "S", 1);
        var newNode = CreateNode(2, "New", 0, isInserted: false);
        pred.SetNextNode(level, succ);
        var preds = new[] { pred };
        var succs = new[] { succ };
        var arrayPool = ArrayPool<SkipListNode<int, string>>.Shared;
        var searchResult = new SearchResult<int, string>(-1, preds, succs, null, arrayPool, 1);

        // Act
        pred.Lock();
        try
        {
             ConcurrentPriorityQueue<int, string>.InsertNode(newNode, searchResult, insertLevel);
        }
        finally
        {
            pred.Unlock();
        }

        // Assert
        Assert.AreSame(succ, newNode.GetNextNode(level), "New node's next pointer should point to successor.");
        Assert.AreSame(newNode, pred.GetNextNode(level), "Predecessor's next pointer should point to new node.");
    }

    [TestMethod]
    public void SchedulePhysicalNodeRemoval_NodeNotDeleted_DoesNothing()
    {
         // Arrange
         var queue = CreateDefaultQueue();
         var node = CreateNode(1, "Test", 0, isDeleted: false);

         // Act
         queue.SchedulePhysicalNodeRemoval(node);

         // Assert
         _mockTaskOrchestrator.Verify(o => o.Run(It.IsAny<Func<Task>>()), Times.Never);
    }

    [TestMethod]
    public void SchedulePhysicalNodeRemoval_NodeDeleted_RunsTask()
    {
         // Arrange
         var queue = CreateDefaultQueue();
         var node = CreateNode(1, "Test", 0, isDeleted: true);

         // Act
         queue.SchedulePhysicalNodeRemoval(node);

         // Assert
         _mockTaskOrchestrator.Verify(o => o.Run(It.IsAny<Func<Task>>()), Times.Once);
    }

    [TestMethod]
    public void SchedulePhysicalNodeRemoval_TaskLogic_UnlinksNode()
    {
        // Arrange
        var queue = CreateDefaultQueue();
        var head = GetInstanceField<SkipListNode<int, string>>(queue, "_head");
        var tail = GetInstanceField<SkipListNode<int, string>>(queue, "_tail");
        var nodeToRemove = CreateNode(5, "RemoveMe", 0, isDeleted: true);

        head.SetNextNode(0, nodeToRemove);
        nodeToRemove.SetNextNode(0, tail);
        Assert.AreSame(nodeToRemove, head.GetNextNode(0), "Pre-condition: Head should point to nodeToRemove");

        // Act
        queue.SchedulePhysicalNodeRemoval(nodeToRemove, 0);

        // Assert
        Assert.AreSame(tail, head.GetNextNode(0), "Head should now point to Tail after removal task execution.");
    }

    [TestMethod]
    public void SchedulePhysicalNodeRemoval_HandlesOrchestratorQueueFull()
    {
         // Arrange
         var queue = CreateDefaultQueue();
         var node = CreateNode(1, "Test", 0, isDeleted: true);
         _mockTaskOrchestrator.Setup(o => o.Run(It.IsAny<Func<Task>>()))
                              .Throws(new InvalidOperationException("Simulated queue full"));

        // Act
        Exception? caughtException = null;
        try
        {
             queue.SchedulePhysicalNodeRemoval(node);
        }
        catch (Exception ex)
        {
            caughtException = ex;
        }

        // Assert
        Assert.IsNull(caughtException, "SchedulePhysicalNodeRemoval should have caught the InvalidOperationException, but it propagated.");
        _mockTaskOrchestrator.Verify(o => o.Run(It.IsAny<Func<Task>>()), Times.Once); // Verify Run was still called
    }

    [TestMethod]
    public void InlineSearch_FindsExistingValidNode()
    {
        // Arrange
        var queue = CreateDefaultQueue();
        queue.TryAdd(5, "E5");
        queue.TryAdd(1, "E1");
        queue.TryAdd(3, "E3");

        // Act
        var node1 = queue.InlineSearch(1);
        var node3 = queue.InlineSearch(3);
        var node5 = queue.InlineSearch(5);

        // Assert
        Assert.IsNotNull(node1, "Node 1 should be found.");
        Assert.AreEqual(1, node1.Priority, "Node 1 priority mismatch.");
        Assert.IsNotNull(node3, "Node 3 should be found.");
        Assert.AreEqual(3, node3.Priority, "Node 3 priority mismatch.");
        Assert.IsNotNull(node5, "Node 5 should be found.");
        Assert.AreEqual(5, node5.Priority, "Node 5 priority mismatch.");
    }

    [TestMethod]
    public void InlineSearch_ReturnsNullForNonExistingNode()
    {
        // Arrange
        var queue = CreateDefaultQueue();
        queue.TryAdd(5, "E5");
        queue.TryAdd(1, "E1");

        // Act
        var node0 = queue.InlineSearch(0);
        var node4 = queue.InlineSearch(4);
        var node9 = queue.InlineSearch(9);

        // Assert
        Assert.IsNull(node0, "Should not find node with priority 0.");
        Assert.IsNull(node4, "Should not find node with priority 4.");
        Assert.IsNull(node9, "Should not find node with priority 9.");
    }

    [TestMethod]
    public void InlineSearch_ReturnsNullForDeletedNode()
    {
        // Arrange
        var queue = CreateDefaultQueue();
        queue.TryAdd(1, "E1");
        bool deleted = queue.TryRemove(1, out _);
        Assert.IsTrue(deleted, "Pre-condition: Delete should succeed.");
        Assert.AreEqual(0, queue.GetCount(), "Pre-condition: Queue should be empty after delete.");

        // Act
        var node1 = queue.InlineSearch(1);

        // Assert
        Assert.IsNull(node1, "InlineSearch should return null for a deleted node.");
    }

    [TestMethod]
    public void StructuralSearch_FindsNodeAndReturnsCorrectStructure()
    {
        // Arrange
        var queue = CreateDefaultQueue(maxSize: 10);
        queue.TryAdd(5, "E5");
        queue.TryAdd(1, "E1");
        queue.TryAdd(3, "E3");
        var nodeToFind = queue.InlineSearch(3);
        Assert.IsNotNull(nodeToFind, "Pre-condition: Node with priority 3 must exist.");
        _ = GetInstanceField<SkipListNode<int, string>>(queue, "_head");
        var node1 = queue.InlineSearch(1);
        var node5 = queue.InlineSearch(5);
        Assert.IsNotNull(node1, "Pre-condition: Node 1 must exist.");
        Assert.IsNotNull(node5, "Pre-condition: Node 5 must exist.");

        // Act
        var result = queue.StructuralSearch(nodeToFind);

        // Assert
        Assert.IsTrue(result.IsFound, "Search should find the existing node.");
        Assert.IsTrue(result.LevelFound >= 0, "LevelFound should be >= 0 when node is found.");
        Assert.AreSame(nodeToFind, result.GetNodeFound(), "GetNodeFound should return the searched node.");
        Assert.AreSame(node1, result.GetPredecessor(0), "Predecessor at level 0 should be node 1.");
        Assert.AreSame(node5, result.GetSuccessor(0), "Successor at level 0 should be node 5.");
    }

    #endregion
}