// ===========================================================================
// <copyright file="ConcurrentPriorityQueueEnumeratorTests.cs" company="Level Up Software">
// Copyright (c) Level Up Software. All rights reserved.
// </copyright>
// ===========================================================================

using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Linq;

using lvlup.DataFerry.Concurrency;
using lvlup.DataFerry.Concurrency.Enumerators;
using lvlup.DataFerry.Concurrency.Extensions;
using lvlup.DataFerry.Orchestrators.Contracts;

using Moq;

namespace lvlup.DataFerry.Tests.Unit;

/// <summary>
/// Unit tests for ConcurrentPriorityQueue enumerators and extension methods.
/// </summary>
[TestClass]
public class ConcurrentPriorityQueueEnumeratorTests
{
    #pragma warning disable CS8618
    private Mock<ITaskOrchestrator> _mockTaskOrchestrator;
    private IComparer<int> _intComparer;
    #pragma warning restore CS8618

    [TestInitialize]
    public void TestInitialize()
    {
        _mockTaskOrchestrator = new Mock<ITaskOrchestrator>();
        _mockTaskOrchestrator
            .Setup(o => o.Run(It.IsAny<Func<Task>>()))
            .Callback<Func<Task>>(action => action());
        _intComparer = Comparer<int>.Default;
    }

    #region Test Helpers

    private ConcurrentPriorityQueue<int, string> CreateDefaultQueue(int maxSize = ConcurrentPriorityQueue<int, string>.DefaultMaxSize)
    {
        return new ConcurrentPriorityQueue<int, string>(_mockTaskOrchestrator.Object, _intComparer, maxSize);
    }

    private ConcurrentPriorityQueue<int, string> CreatePopulatedQueue(int count)
    {
        var queue = CreateDefaultQueue();
        for (int i = 1; i <= count; i++)
        {
            queue.TryAdd(i, $"Element{i}");
        }
        return queue;
    }

    #endregion

    #region Full Enumerator Tests

    [TestMethod]
    public void TestFullEnumerator_ReturnsAllElements()
    {
        // Arrange
        var queue = CreatePopulatedQueue(5);

        // Act
        var items = queue.GetFullEnumerator().ToList();

        // Assert
        Assert.AreEqual(5, items.Count, "Should return all elements");
        for (int i = 0; i < items.Count; i++)
        {
            Assert.AreEqual(i + 1, items[i].Priority, $"Priority at index {i} should be {i + 1}");
            Assert.AreEqual($"Element{i + 1}", items[i].Element);
        }
    }

    [TestMethod]
    public void TestFullEnumerator_EmptyQueue_ReturnsNoElements()
    {
        // Arrange
        var queue = CreateDefaultQueue();

        // Act
        var items = queue.GetFullEnumerator().ToList();

        // Assert
        Assert.AreEqual(0, items.Count, "Should return no elements for empty queue");
    }

    [TestMethod]
    public void TestFullEnumerator_HandlesModificationsDuringIteration()
    {
        // Arrange
        var queue = CreatePopulatedQueue(10);
        var enumerator = queue.GetFullEnumerator().GetEnumerator();
        var itemsBeforeModification = new List<(int, string)>();

        // Act - Start enumeration
        for (int i = 0; i < 5; i++)
        {
            Assert.IsTrue(enumerator.MoveNext());
            itemsBeforeModification.Add(enumerator.Current);
        }

        // Modify queue during enumeration
        queue.TryAdd(11, "Element11");
        queue.TryDelete(8);

        // Continue enumeration
        var itemsAfterModification = new List<(int, string)>();
        while (enumerator.MoveNext())
        {
            itemsAfterModification.Add(enumerator.Current);
        }

        // Assert
        Assert.AreEqual(5, itemsBeforeModification.Count);
        // The enumerator should continue and may or may not see the modifications
        // depending on timing, but it should complete without errors
        Assert.IsTrue(itemsAfterModification.Count >= 4, "Should have remaining items");
    }

    [TestMethod]
    public void TestFullEnumerator_ClearDuringEnumeration_ThrowsException()
    {
        // Arrange
        var queue = CreatePopulatedQueue(10);
        var enumerator = queue.GetFullEnumerator().GetEnumerator();

        // Start enumeration
        Assert.IsTrue(enumerator.MoveNext());

        // Act - Clear queue during enumeration
        queue.Clear();

        // Assert - Next move should throw
        Assert.ThrowsException<InvalidOperationException>(() =>
        {
            enumerator.MoveNext();
        }, "Should throw when queue is cleared during enumeration");
    }

    [TestMethod]
    public async Task TestAsyncEnumerator_WorksCorrectly()
    {
        // Arrange
        var queue = CreatePopulatedQueue(5);
        var items = new List<(int Priority, string Element)>();

        // Act
        await using var enumerator = queue.GetAsyncEnumerator();
        while (await enumerator.MoveNextAsync())
        {
            items.Add(enumerator.Current);
        }

        // Assert
        Assert.AreEqual(5, items.Count, "Should enumerate all items");
        for (int i = 0; i < items.Count; i++)
        {
            Assert.AreEqual(i + 1, items[i].Priority);
        }
    }

    #endregion

    #region Snapshot Enumerator Tests

    [TestMethod]
    public void TestSnapshotEnumerator_CreatesStableSnapshot()
    {
        // Arrange
        var queue = CreatePopulatedQueue(5);
        var snapshot = queue.TakeSnapshot();
        var snapshotEnumerator = new SnapshotEnumerator<int, string>(snapshot);

        // Act - Modify queue after snapshot
        queue.TryAdd(6, "Element6");
        queue.TryDelete(3);

        // Assert - Snapshot should be unchanged
        Assert.AreEqual(5, snapshotEnumerator.Count, "Snapshot count should remain 5");
        var items = snapshotEnumerator.ToList();
        Assert.AreEqual(5, items.Count);
        Assert.IsTrue(items.Any(i => i.Priority == 3), "Deleted item should still be in snapshot");
        Assert.IsFalse(items.Any(i => i.Priority == 6), "New item should not be in snapshot");
    }

    [TestMethod]
    public void TestSnapshotEnumerator_SupportsMultipleEnumerations()
    {
        // Arrange
        var snapshot = new SnapshotEnumerator<int, string>(
            new[] { (1, "One"), (2, "Two"), (3, "Three") }.ToImmutableList());

        // Act - Enumerate multiple times
        var firstPass = snapshot.ToList();
        var secondPass = snapshot.ToList();

        // Assert
        Assert.AreEqual(3, firstPass.Count);
        Assert.AreEqual(3, secondPass.Count);
        CollectionAssert.AreEqual(firstPass, secondPass);
    }

    [TestMethod]
    public void TestSnapshotEnumerator_FilteringWorks()
    {
        // Arrange
        var snapshot = new SnapshotEnumerator<int, string>(
            Enumerable.Range(1, 10).Select(i => (i, $"Element{i}")).ToImmutableList());

        // Act
        var filtered = snapshot.Where(item => item.Priority % 2 == 0);

        // Assert
        Assert.AreEqual(5, filtered.Count, "Should have 5 even numbers");
        Assert.IsTrue(filtered.All(item => item.Item.Priority % 2 == 0), "All items should be even");
    }

    #endregion

    #region Query Extensions Tests

    [TestMethod]
    public void TestQueryExtensions_FilterCorrectly()
    {
        // Arrange
        var queue = CreatePopulatedQueue(10);

        // Act
        var evenPriorities = queue.Where(p => p % 2 == 0).ToList();

        // Assert
        Assert.AreEqual(5, evenPriorities.Count, "Should have 5 even priorities");
        Assert.IsTrue(evenPriorities.All(e => e.StartsWith("Element")), "All elements should match pattern");
    }

    [TestMethod]
    public void TestQueryExtensions_WhereWithPriority()
    {
        // Arrange
        var queue = CreatePopulatedQueue(10);

        // Act
        var filtered = queue.WhereWithPriority((p, e) => p > 5 && e.Contains("8")).ToList();

        // Assert
        Assert.AreEqual(1, filtered.Count, "Should find one matching item");
        Assert.AreEqual(8, filtered[0].Priority);
        Assert.AreEqual("Element8", filtered[0].Element);
    }

    [TestMethod]
    public void TestQueryExtensions_Select()
    {
        // Arrange
        var queue = CreatePopulatedQueue(5);

        // Act
        var projected = queue.Select((p, e) => $"{p}:{e}").ToList();

        // Assert
        Assert.AreEqual(5, projected.Count);
        Assert.AreEqual("1:Element1", projected[0]);
        Assert.AreEqual("5:Element5", projected[4]);
    }

    [TestMethod]
    public async Task TestQueryExtensions_ToAsyncEnumerable()
    {
        // Arrange
        var queue = CreatePopulatedQueue(5);
        var items = new List<(int, string)>();

        // Act
        await foreach (var item in queue.ToAsyncEnumerable())
        {
            items.Add(item);
        }

        // Assert
        Assert.AreEqual(5, items.Count);
        for (int i = 0; i < items.Count; i++)
        {
            Assert.AreEqual(i + 1, items[i].Item1);
        }
    }

    [TestMethod]
    public void TestQueryExtensions_CreateSnapshot()
    {
        // Arrange
        var queue = CreatePopulatedQueue(10);

        // Act
        var snapshot = queue.CreateSnapshot();

        // Assert
        Assert.AreEqual(10, snapshot.Count);
        Assert.IsFalse(snapshot.IsEmpty);
        Assert.AreEqual((1, "Element1"), snapshot[0]);
        Assert.AreEqual((10, "Element10"), snapshot[9]);
    }

    [TestMethod]
    public void TestQueryExtensions_CountWithPredicate()
    {
        // Arrange
        var queue = CreatePopulatedQueue(10);

        // Act
        var count = queue.Count((p, e) => p <= 5);

        // Assert
        Assert.AreEqual(5, count, "Should count 5 items with priority <= 5");
    }

    [TestMethod]
    public void TestQueryExtensions_Any()
    {
        // Arrange
        var queue = CreatePopulatedQueue(10);

        // Act & Assert
        Assert.IsTrue(queue.Any((p, e) => p == 5), "Should find priority 5");
        Assert.IsFalse(queue.Any((p, e) => p > 10), "Should not find priority > 10");
    }

    #endregion

    #region Enumerator Extensions Tests

    [TestMethod]
    public void TestEnumeratorExtensions_Buffer()
    {
        // Arrange
        var source = Enumerable.Range(1, 10);
        var enumerator = source.GetEnumerator();

        // Act
        var buffers = enumerator.Buffer(3).ToList();

        // Assert
        Assert.AreEqual(4, buffers.Count, "Should have 4 buffers");
        Assert.AreEqual(3, buffers[0].Count);
        Assert.AreEqual(3, buffers[1].Count);
        Assert.AreEqual(3, buffers[2].Count);
        Assert.AreEqual(1, buffers[3].Count, "Last buffer should have 1 item");
    }

    [TestMethod]
    public void TestEnumeratorExtensions_Page()
    {
        // Arrange
        var source = Enumerable.Range(1, 25);

        // Act
        var pages = source.Page(10).ToList();

        // Assert
        Assert.AreEqual(3, pages.Count, "Should have 3 pages");
        Assert.AreEqual(10, pages[0].Count);
        Assert.AreEqual(10, pages[1].Count);
        Assert.AreEqual(5, pages[2].Count);
    }

    [TestMethod]
    public void TestEnumeratorExtensions_Window()
    {
        // Arrange
        var source = Enumerable.Range(1, 5);

        // Act
        var windows = source.Window(3).ToList();

        // Assert
        Assert.AreEqual(3, windows.Count, "Should have 3 windows");
        CollectionAssert.AreEqual(new[] { 1, 2, 3 }, windows[0]);
        CollectionAssert.AreEqual(new[] { 2, 3, 4 }, windows[1]);
        CollectionAssert.AreEqual(new[] { 3, 4, 5 }, windows[2]);
    }

    [TestMethod]
    public void TestEnumeratorExtensions_Sample()
    {
        // Arrange
        var source = Enumerable.Range(1, 20);

        // Act
        var sampled = source.Sample(5).ToList();

        // Assert
        Assert.AreEqual(4, sampled.Count, "Should sample every 5th element");
        CollectionAssert.AreEqual(new[] { 1, 6, 11, 16 }, sampled);
    }

    [TestMethod]
    public void TestEnumeratorExtensions_WithIndex()
    {
        // Arrange
        var source = new[] { "A", "B", "C" };

        // Act
        var indexed = source.WithIndex().ToList();

        // Assert
        Assert.AreEqual(3, indexed.Count);
        Assert.AreEqual(("A", 0), indexed[0]);
        Assert.AreEqual(("B", 1), indexed[1]);
        Assert.AreEqual(("C", 2), indexed[2]);
    }

    [TestMethod]
    public void TestEnumeratorExtensions_RandomSample()
    {
        // Arrange
        var source = Enumerable.Range(1, 1000);
        var seed = 42;

        // Act
        var sampled1 = source.RandomSample(0.1, seed).ToList();
        var sampled2 = source.RandomSample(0.1, seed).ToList();

        // Assert
        Assert.IsTrue(sampled1.Count > 50 && sampled1.Count < 150, 
            "Should sample approximately 10% of items");
        CollectionAssert.AreEqual(sampled1, sampled2, 
            "Same seed should produce same results");
    }

    #endregion
}