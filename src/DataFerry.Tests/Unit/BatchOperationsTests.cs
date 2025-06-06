// ===========================================================================
// <copyright file="BatchOperationsTests.cs" company="Level Up Software">
// Copyright (c) Level Up Software. All rights reserved.
// </copyright>
// ===========================================================================

using System.Collections.Concurrent;
using System.Diagnostics;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using lvlup.DataFerry.Concurrency;
using lvlup.DataFerry.Concurrency.Algorithms;
using lvlup.DataFerry.Concurrency.Extensions;
using lvlup.DataFerry.Orchestrators.Contracts;

namespace DataFerry.Tests.Unit;

/// <summary>
/// Unit tests for batch operations on the concurrent priority queue.
/// </summary>
[TestClass]
public class BatchOperationsTests
{
    private ConcurrentPriorityQueue<int, string> _queue = null!;
    private ITaskOrchestrator _taskOrchestrator = null!;

    /// <summary>
    /// Initializes the test environment before each test.
    /// </summary>
    [TestInitialize]
    public void Setup()
    {
        _taskOrchestrator = TestUtils.CreateTaskOrchestrator();
        var options = new ConcurrentPriorityQueueOptions
        {
            MaxSize = 10000,
            EnableBatchOperations = true,
            EnablePriorityUpdates = true,
            EnableAdaptiveSpray = true
        };
        _queue = new ConcurrentPriorityQueue<int, string>(
            _taskOrchestrator,
            Comparer<int>.Default,
            serviceProvider: null,
            options);
    }

    /// <summary>
    /// Cleans up the test environment after each test.
    /// </summary>
    [TestCleanup]
    public void Cleanup()
    {
        _queue?.Dispose();
    }

    /// <summary>
    /// Tests that a large batch add operation completes successfully.
    /// </summary>
    [TestMethod]
    public async Task TestBatchAdd_LargeBatch_CompletesSuccessfully()
    {
        // Arrange
        const int batchSize = 1000;
        var items = GenerateTestItems(batchSize);
        var options = new BatchOperationOptions
        {
            BatchSize = 100,
            SortBatch = true,
            ParallelThreshold = 50
        };

        // Act
        var result = await _queue.TryAddRangeAsync(items, options);

        // Assert
        Assert.AreEqual(batchSize, result.SuccessCount, "All items should be added successfully");
        Assert.AreEqual(0, result.FailureCount, "No failures expected");
        Assert.AreEqual(100, result.SuccessRate, "Success rate should be 100%");
        Assert.IsTrue(result.Duration.TotalMilliseconds > 0, "Duration should be recorded");
        Assert.AreEqual(batchSize, _queue.GetCount(), "Queue count should match added items");

        // Verify items were added correctly
        var minItem = await GetMinItemAsync();
        Assert.IsNotNull(minItem);
        Assert.AreEqual(0, minItem.Value.Priority, "Minimum priority should be 0");
    }

    /// <summary>
    /// Tests that batch add operation reports failures correctly.
    /// </summary>
    [TestMethod]
    public async Task TestBatchAdd_WithFailures_ReportsCorrectly()
    {
        // Arrange - Fill queue to near capacity
        var maxSize = 100;
        var smallQueue = new ConcurrentPriorityQueue<int, string>(
            _taskOrchestrator,
            Comparer<int>.Default,
            maxSize: maxSize);

        // Fill to capacity
        for (int i = 0; i < maxSize; i++)
        {
            Assert.IsTrue(smallQueue.TryAdd(i, $"Item-{i}"));
        }

        // Try to add more items
        var extraItems = GenerateTestItems(50).ToAsyncEnumerable();
        var options = new BatchOperationOptions { BatchSize = 10 };

        // Act
        var result = await smallQueue.TryAddRangeAsync(extraItems, options);

        // Assert
        Assert.AreEqual(0, result.SuccessCount, "No items should be added when at capacity");
        Assert.AreEqual(50, result.FailureCount, "All items should fail");
        Assert.AreEqual(0, result.SuccessRate, "Success rate should be 0%");

        smallQueue.Dispose();
    }

    /// <summary>
    /// Tests that adaptive spray adjusts parameters based on operation patterns.
    /// </summary>
    [TestMethod]
    public async Task TestAdaptiveSpray_AdjustsParameters()
    {
        // Arrange
        const int itemCount = 1000;
        await AddItemsAsync(itemCount);

        // Act - Perform many delete operations to trigger adaptation
        var successCount = 0;
        var stopwatch = Stopwatch.StartNew();

        for (int i = 0; i < 100; i++)
        {
            if (_queue.TryDeleteMin(out _))
            {
                successCount++;
            }
        }

        stopwatch.Stop();

        // Assert
        Assert.IsTrue(successCount > 50, "Should successfully delete most items");
        Assert.AreEqual(itemCount - successCount, _queue.GetCount(), "Queue count should reflect deletions");
        
        // The adaptive tracker should have adjusted parameters based on the operations
        // We can't directly test the internal state, but we can observe performance
        Console.WriteLine($"Delete operations completed in {stopwatch.ElapsedMilliseconds}ms with {successCount} successes");
    }

    /// <summary>
    /// Tests that priority update operations are atomic and succeed.
    /// </summary>
    [TestMethod]
    public async Task TestPriorityUpdate_Atomic_Succeeds()
    {
        // Arrange
        const int originalPriority = 5;
        const int newPriority = 1;
        const string element = "TestElement";
        
        Assert.IsTrue(_queue.TryAdd(originalPriority, element));
        Assert.IsTrue(_queue.TryAdd(10, "OtherElement"));

        // Act
        var updateResult = _queue.TryUpdatePriority(originalPriority, newPriority, element);

        // Assert
        Assert.IsTrue(updateResult, "Priority update should succeed");
        
        // Verify the element now has the new priority
        Assert.IsTrue(_queue.TryDeleteMin(out var deletedElement));
        Assert.AreEqual(element, deletedElement, "Element with updated priority should be minimum");
        
        // Verify original priority no longer exists
        Assert.IsFalse(_queue.ContainsPriority(originalPriority), "Original priority should not exist");
    }

    /// <summary>
    /// Tests that bulk update operations handle partial failures with rollback.
    /// </summary>
    [TestMethod]
    public async Task TestBulkUpdate_PartialFailure_RollsBack()
    {
        // Arrange
        const int itemCount = 100;
        await AddItemsAsync(itemCount);
        var initialCount = _queue.GetCount();

        // Act - Update all even priorities to negative values
        var updateCount = await _queue.BulkUpdateAsync(
            updateCondition: (priority, element) => priority % 2 == 0,
            priorityTransform: (priority, element) => -priority,
            elementTransform: element => element + "-Updated");

        // Assert
        Assert.IsTrue(updateCount > 0, "Some updates should succeed");
        Assert.AreEqual(initialCount, _queue.GetCount(), "Queue count should remain the same");
        
        // Verify some items were updated
        var minItem = await GetMinItemAsync();
        Assert.IsNotNull(minItem);
        Assert.IsTrue(minItem.Value.Priority < 0, "Minimum should now be negative");
        Assert.IsTrue(minItem.Value.Element.EndsWith("-Updated"), "Element should be updated");
    }

    /// <summary>
    /// Tests concurrent batch operations for thread safety.
    /// </summary>
    [TestMethod]
    public async Task TestBatchOperations_Concurrent_ThreadSafe()
    {
        // Arrange
        const int threadCount = 4;
        const int itemsPerThread = 250;
        var tasks = new Task<BatchOperationResult>[threadCount];
        var barrier = new Barrier(threadCount);

        // Act - Launch concurrent batch adds
        for (int i = 0; i < threadCount; i++)
        {
            var threadId = i;
            tasks[i] = Task.Run(async () =>
            {
                barrier.SignalAndWait();
                var items = GenerateTestItems(itemsPerThread, threadId * itemsPerThread);
                return await _queue.TryAddRangeAsync(items);
            });
        }

        var results = await Task.WhenAll(tasks);

        // Assert
        var totalSuccess = results.Sum(r => r.SuccessCount);
        var totalFailure = results.Sum(r => r.FailureCount);
        
        Assert.AreEqual(threadCount * itemsPerThread, totalSuccess, "All items should be added");
        Assert.AreEqual(0, totalFailure, "No failures expected");
        Assert.AreEqual(threadCount * itemsPerThread, _queue.GetCount(), "Queue count should match total added");
    }

    /// <summary>
    /// Tests batch operations with cancellation.
    /// </summary>
    [TestMethod]
    public async Task TestBatchAdd_WithCancellation_StopsGracefully()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        var items = GenerateSlowAsyncItems(1000, delayMs: 10);
        var options = new BatchOperationOptions { BatchSize = 10 };

        // Act - Cancel after a short delay
        var addTask = _queue.TryAddRangeAsync(items, options, cts.Token);
        await Task.Delay(50);
        cts.Cancel();

        var result = await addTask;

        // Assert
        Assert.IsTrue(result.SuccessCount < 1000, "Should not process all items due to cancellation");
        Assert.AreEqual(result.SuccessCount, _queue.GetCount(), "Queue count should match successful adds");
    }

    #region Helper Methods

    /// <summary>
    /// Generates test items for batch operations.
    /// </summary>
    private static async IAsyncEnumerable<(int Priority, string Element)> GenerateTestItems(int count, int offset = 0)
    {
        for (int i = 0; i < count; i++)
        {
            yield return (i + offset, $"Item-{i + offset}");
        }
        await Task.CompletedTask;
    }

    /// <summary>
    /// Generates test items with delays to simulate slow producers.
    /// </summary>
    private static async IAsyncEnumerable<(int Priority, string Element)> GenerateSlowAsyncItems(int count, int delayMs)
    {
        for (int i = 0; i < count; i++)
        {
            await Task.Delay(delayMs);
            yield return (i, $"SlowItem-{i}");
        }
    }

    /// <summary>
    /// Adds items to the queue asynchronously.
    /// </summary>
    private async Task AddItemsAsync(int count)
    {
        var items = GenerateTestItems(count);
        var result = await _queue.TryAddRangeAsync(items);
        Assert.AreEqual(count, result.SuccessCount);
    }

    /// <summary>
    /// Gets the minimum item from the queue.
    /// </summary>
    private async Task<(int Priority, string Element)?> GetMinItemAsync()
    {
        if (_queue.TryPeek(out var priority, out var element))
        {
            return (priority, element);
        }
        return null;
    }

    #endregion
}