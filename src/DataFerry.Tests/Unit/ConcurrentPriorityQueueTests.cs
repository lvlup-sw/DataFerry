using lvlup.DataFerry.Concurrency;
using lvlup.DataFerry.Orchestrators.Contracts;
using Moq;

namespace lvlup.DataFerry.Tests.Unit;

[TestClass]
public class ConcurrentPriorityQueueTests
{
    #pragma warning disable CS8618
    private Mock<ITaskOrchestrator> _mockTaskOrchestrator;
    private IComparer<int> _intComparer;
    #pragma warning restore CS8618

    [TestInitialize]
    public void TestInitialize()
    {
        _mockTaskOrchestrator = new Mock<ITaskOrchestrator>();
        // Setup the mock to simply execute the task immediately for testing purposes
        _mockTaskOrchestrator.Setup(o => o.Run(It.IsAny<Func<Task>>()))
                             .Callback<Func<Task>>(action => action());
        _intComparer = Comparer<int>.Default;
    }

    #region Test Inits
    
    private ConcurrentPriorityQueue<int, string> CreateDefaultQueue(int maxSize = ConcurrentPriorityQueue<int, string>.DefaultMaxSize)
    {
        return new ConcurrentPriorityQueue<int, string>(_mockTaskOrchestrator.Object, _intComparer, maxSize);
    }
    
    private ConcurrentPriorityQueue<string, string> CreateDefaultQueueWithRefs(int maxSize = ConcurrentPriorityQueue<int, string>.DefaultMaxSize)
    {
        return new ConcurrentPriorityQueue<string, string>(_mockTaskOrchestrator.Object, Comparer<string>.Default, maxSize);
    }
    
    #endregion
    #region Constructor Tests

    [TestMethod]
    public void Constructor_WithValidParameters_InitializesCorrectly()
    {
        var cpq = new ConcurrentPriorityQueue<int, string>(_mockTaskOrchestrator.Object, _intComparer, 100, 2, 2, 0.25);
        Assert.IsNotNull(cpq);
        Assert.AreEqual(0, cpq.GetCount());
    }

    [TestMethod]
    public void Constructor_NullTaskOrchestrator_ThrowsArgumentNullException()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() =>
        {
            _ = new ConcurrentPriorityQueue<int, string>(null!, _intComparer);
        });
    }

    [TestMethod]
    public void Constructor_NullComparer_ThrowsArgumentNullException()
    {
        Assert.ThrowsExactly<ArgumentNullException>(() =>
        {
            _ = new ConcurrentPriorityQueue<int, string>(_mockTaskOrchestrator.Object, null!);
        });
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    public void Constructor_InvalidMaxSize_ThrowsArgumentOutOfRangeException(int maxSize)
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
        {
            _ = new ConcurrentPriorityQueue<int, string>(_mockTaskOrchestrator.Object, _intComparer, maxSize);
        });
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    public void Constructor_InvalidOffsetK_ThrowsArgumentOutOfRangeException(int offsetK)
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
        {
            _ = new ConcurrentPriorityQueue<int, string>(_mockTaskOrchestrator.Object, _intComparer, offsetK: offsetK);
        });
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    public void Constructor_InvalidOffsetM_ThrowsArgumentOutOfRangeException(int offsetM)
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
        {
            _ = new ConcurrentPriorityQueue<int, string>(_mockTaskOrchestrator.Object, _intComparer, offsetM: offsetM);
        });
    }

    [TestMethod]
    [DataRow(0.0)]
    [DataRow(-0.1)]
    [DataRow(1.0)]
    [DataRow(1.1)]
    public void Constructor_InvalidPromotionProbability_ThrowsArgumentOutOfRangeException(double probability)
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() =>
        {
            _ = new ConcurrentPriorityQueue<int, string>(_mockTaskOrchestrator.Object, _intComparer, promotionProbability: probability);
        });
    }

    [TestMethod]
    public void Constructor_WithIntMaxValueSize_InitializesCorrectly()
    {
         var queue = new ConcurrentPriorityQueue<int, string>(_mockTaskOrchestrator.Object, _intComparer, int.MaxValue);
         Assert.IsNotNull(queue);
         Assert.AreEqual(0, queue.GetCount());
         // Todo: verify internal state via reflection
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
        Assert.ThrowsExactly<ArgumentNullException>(() => queue.TryAdd(nullPriority, "Element"));
    }


    [TestMethod]
    public void TryAdd_NullElement_ThrowsArgumentNullException()
    {
        // Arrange
        var queue = CreateDefaultQueue();
        const int priority = 1;
        string nullElement = null!;

        // Act & Assert
        Assert.ThrowsExactly<ArgumentNullException>(() => queue.TryAdd(priority, nullElement));
    }

    #endregion
    #region TryDelete Tests

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
        bool deleted = queue.TryDelete(priorityToDelete);

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
        bool deleted = queue.TryDelete(priorityToDelete);

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
        bool deleted = queue.TryDelete(priorityToDelete);

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
        bool deleted = queue.TryDelete(priorityToDelete);

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
        Assert.ThrowsExactly<ArgumentNullException>(() => queue.TryDelete(nullPriority));
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
        var queue = CreateDefaultQueue();

        // Act
        bool deleted = queue.TryDeleteAbsoluteMin(out _);

        // Assert
        Assert.IsFalse(deleted, "TryDeleteAbsoluteMin should return false for an empty queue.");
        Assert.Fail("The out parameter should be the default value (null for string) for an empty queue.");
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

        // Act
        var samples = queue.SampleNearMin(sampleSize).ToList();

        // Assert
        Assert.IsNotNull(samples, "The result of SampleNearMin should not be null.");
        Assert.AreEqual(sampleSize, samples.Count, $"The number of samples returned should match the requested sample size ({sampleSize}).");
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
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => queue.SampleNearMin(sampleSize));
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
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => queue.SampleNearMin(sampleSize, multiplier));
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
        Assert.ThrowsExactly<ArgumentNullException>(() => queue.Update(nullPriority, element));
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
        Assert.ThrowsExactly<ArgumentNullException>(() => queue.Update(priority, nullElement));
    }

    [TestMethod]
    public void Update_NullPriorityFunc_ThrowsArgumentNullException()
    {
        // Arrange
        var queue = CreateDefaultQueueWithRefs();
        string nullPriority = null!;
        Func<string, string, string> updateFunction = (_, e) => e;

        // Act & Assert
        Assert.ThrowsExactly<ArgumentNullException>(() => queue.Update(nullPriority, updateFunction));
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
        Assert.ThrowsExactly<ArgumentNullException>(() => queue.Update(priority, nullUpdateFunction));
    }

    [TestMethod]
    public void Update_ElementLogicallyDeletedDuringUpdateAttempt_ReturnsFalse()
    {
        // Arrange
        var queue = CreateDefaultQueue();
        const int priority = 1;
        queue.TryAdd(priority, "Element1");
        bool preDeleted = queue.TryDelete(priority);
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
        bool preDeleted = queue.TryDelete(priority);
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
        Assert.ThrowsExactly<ArgumentNullException>(() => queue.ContainsPriority(nullPriority));
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
        queue.TryDelete(2);
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
            queue.TryDelete(3);

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
    #region Concurrency Tests (Basic)

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
        const int maxQueueSize = (numAddThreads * itemsPerAddThread) + 10;
        var queue = CreateDefaultQueue(maxSize: maxQueueSize);
        var tasks = new List<Task>();

        // Define Delete action (part of Arrange)
        Action deleteAction = () =>
        {
            for (int j = 0; j < itemsPerDeleteThread; j++)
            {
                queue.TryDeleteAbsoluteMin(out _);
                // Small sleep can sometimes help induce different interleavings, but makes tests non-deterministic
                // Thread.Sleep(5);
            }
        };

        // Prepare Add tasks (still part of Arrange)
        for (int i = 0; i < numAddThreads; i++)
        {
            int threadId = i;
            tasks.Add(Task.Run(() => AddAction(threadId)));
        }

        // Prepare Delete tasks (still part of Arrange)
        for (int i = 0; i < numDeleteThreads; i++)
        {
             tasks.Add(Task.Run(deleteAction));
        }

        // Theoretical minimum count if all deletes succeeded immediately after adds they could target
        int expectedMinCount = Math.Max(0, (numAddThreads * itemsPerAddThread) - (numDeleteThreads * itemsPerDeleteThread));
        // Estimate minimum number of deletion tasks scheduled (very rough)
        const int minExpectedDeletionTasks = (numDeleteThreads * itemsPerDeleteThread) / 2;
        
        // Act
        Task.WaitAll(tasks.ToArray());
        int finalCount = queue.GetCount();

        // Assert
        Assert.IsTrue(finalCount >= 0, "Final count should not be negative.");
        Console.WriteLine($"Concurrent Add/Delete: Final Count = {finalCount}, Theoretical Min = {expectedMinCount}");
        // Assert.IsTrue(finalCount >= expectedMinCount - (itemsPerDeleteThread * numDeleteThreads / 2), "Final count seems lower than expected range.");

        // Verify background deletion tasks were scheduled (at least some deletions were likely successful)
        _mockTaskOrchestrator.Verify(o => o.Run(It.IsAny<Func<Task>>()), Times.AtLeast(minExpectedDeletionTasks),
            $"At least roughly {minExpectedDeletionTasks} background removal tasks should have been scheduled.");
        return;

        // Define Add action (part of Arrange)
        void AddAction(int threadId)
        {
            for (int j = 0; j < itemsPerAddThread; j++)
            {
                int priority = threadId * itemsPerAddThread + j;
                queue.TryAdd(priority, $"Element{priority}");
            }
        }
    }

    #endregion
    #region Internal Class Tests (Optional but useful)
    /*
    [TestMethod]
    [DataRow(100, 10, 1, 1, 4, 13, 1)]   // Example values, replace with calculated expected
    [DataRow(1000, 20, 2, 3, 8, 918, 1)] // Example values, replace with calculated expected
    [DataRow(2, 5, 1, 1, 1, 1, 1)]       // Edge case: very small count
    [DataRow(100000, 31, 1, 1, 11, 1520, 2)] // Large count
    public void SprayParameters_CalculateParameters_ReturnsCorrectValues(int count, int topLevel, int k, int m, int expectedH, int expectedY, int expectedD)
    {
        // Note: Due to potential floating point and rounding differences,
        // these expected values might need slight adjustment based on actual execution.
        // Also, StartHeight clamping and divisibility adjustment affects the final value.

        var parameters = ConcurrentPriorityQueue<int, string>.SprayParameters.CalculateParameters(count, topLevel, k, m);

        // Calculate expected StartHeight considering clamping and divisibility
         double logN = Math.Log(count);
         int h_raw = (int)logN + k;
         int expectedStartHeightRaw = Math.Min(topLevel, Math.Max(0, h_raw));
         int descentLength = Math.Max(1, (int)Math.Log(Math.Max(1.0, logN)));
         int finalExpectedStartHeight = expectedStartHeightRaw;
          if (finalExpectedStartHeight >= descentLength && finalExpectedStartHeight % descentLength != 0)
         {
             finalExpectedStartHeight = descentLength * (finalExpectedStartHeight / descentLength);
         }
         else if (finalExpectedStartHeight < descentLength)
         {
             finalExpectedStartHeight = Math.Max(0, finalExpectedStartHeight);
         }


        Console.WriteLine($"Calculated: H={parameters.StartHeight}, Y={parameters.MaxJumpLength}, D={parameters.DescentLength}");
        Console.WriteLine($"Expected:   H={finalExpectedStartHeight}, Y={expectedY}, D={expectedD}");

        Assert.AreEqual(finalExpectedStartHeight, parameters.StartHeight, "StartHeight differs.");
        Assert.AreEqual(expectedY, parameters.MaxJumpLength, "MaxJumpLength differs.");
        Assert.AreEqual(expectedD, parameters.DescentLength, "DescentLength differs.");

    }

    // --- SkipListNode Tests ---
    // Mostly tested implicitly via the main queue operations.
    // Could add specific tests for CompareTo, CompareToPriority if complex logic existed.
    [TestMethod]
    public void SkipListNode_CompareTo_HandlesHeadTailDataAndSequence()
    {
        var comparer = Comparer<int>.Default;
        // Need access to SkipListNode constructor or a way to create nodes
        // This might require making SkipListNode constructor public or internal visible

        // Assuming we can create nodes (adjust based on actual accessibility)
        var head = new ConcurrentPriorityQueue<int, string>.SkipListNode(ConcurrentPriorityQueue<int, string>.SkipListNode.NodeType.Head, 5);
        var tail = new ConcurrentPriorityQueue<int, string>.SkipListNode(ConcurrentPriorityQueue<int, string>.SkipListNode.NodeType.Tail, 5);
        var node5a = new ConcurrentPriorityQueue<int, string>.SkipListNode(5, "A", 3, comparer); // Lower sequence number assumed
        var node5b = new ConcurrentPriorityQueue<int, string>.SkipListNode(5, "B", 4, comparer); // Higher sequence number assumed
        var node10 = new ConcurrentPriorityQueue<int, string>.SkipListNode(10, "C", 2, comparer);

        Assert.IsTrue(head.CompareTo(tail) < 0);
        Assert.IsTrue(head.CompareTo(node5a) < 0);
        Assert.IsTrue(node5a.CompareTo(head) > 0);

        Assert.IsTrue(tail.CompareTo(head) > 0);
        Assert.IsTrue(tail.CompareTo(node10) > 0);
        Assert.IsTrue(node10.CompareTo(tail) < 0);

        Assert.IsTrue(node5a.CompareTo(node10) < 0);
        Assert.IsTrue(node10.CompareTo(node5a) > 0);

        // Compare nodes with same priority but different sequence numbers
        Assert.IsTrue(node5a.CompareTo(node5b) < 0, "Node 5a should be less than 5b due to sequence number");
        Assert.IsTrue(node5b.CompareTo(node5a) > 0, "Node 5b should be greater than 5a due to sequence number");
        Assert.IsTrue(node5a.CompareTo(node5a) == 0, "Node 5a should be equal to itself");

        // CompareToPriority
        Assert.IsTrue(node5a.CompareToPriority(5) == 0);
        Assert.IsTrue(node5a.CompareToPriority(4) > 0);
        Assert.IsTrue(node5a.CompareToPriority(6) < 0);
        Assert.IsTrue(head.CompareToPriority(100) < 0); // Head always less
        Assert.IsTrue(tail.CompareToPriority(0) > 0);   // Tail always greater
    }
    */
    #endregion
}