// ===========================================================================
// <copyright file="SimpleAgent4Tests.cs" company="Level Up Software">
// Copyright (c) Level Up Software. All rights reserved.
// </copyright>
// ===========================================================================

using Microsoft.VisualStudio.TestTools.UnitTesting;

using lvlup.DataFerry.Concurrency.Algorithms;

namespace DataFerry.Tests.Unit;

/// <summary>
/// Simple unit tests to verify Agent 4 components compile and function correctly.
/// </summary>
[TestClass]
public class SimpleAgent4Tests
{
    /// <summary>
    /// Tests that AdaptiveSprayTracker can be created and returns valid offsets.
    /// </summary>
    [TestMethod]
    public void TestAdaptiveSprayTracker_CreatesAndReturnsOffsets()
    {
        // Arrange
        var tracker = new AdaptiveSprayTracker(offsetK: 2, offsetM: 3, windowSize: 50);

        // Act
        var (offsetK, offsetM) = tracker.GetCurrentOffsets();

        // Assert
        Assert.AreEqual(2, offsetK, "Initial OffsetK should match constructor parameter");
        Assert.AreEqual(3, offsetM, "Initial OffsetM should match constructor parameter");
    }

    /// <summary>
    /// Tests that AdaptiveSprayTracker can record results without throwing.
    /// </summary>
    [TestMethod]
    public void TestAdaptiveSprayTracker_RecordsResults()
    {
        // Arrange
        var tracker = new AdaptiveSprayTracker(offsetK: 1, offsetM: 1, windowSize: 10);

        // Act - Record multiple results
        for (int i = 0; i < 20; i++)
        {
            tracker.RecordResult(
                success: i % 2 == 0,
                jumpLength: i * 2,
                duration: TimeSpan.FromMilliseconds(i),
                contentionLevel: i % 5);
        }

        // Assert - Should not throw and offsets may have adjusted
        var (offsetK, offsetM) = tracker.GetCurrentOffsets();
        Assert.IsTrue(offsetK > 0, "OffsetK should remain positive");
        Assert.IsTrue(offsetM > 0, "OffsetM should remain positive");
    }

    /// <summary>
    /// Tests that BatchOperationResult calculates success rate correctly.
    /// </summary>
    [TestMethod]
    public void TestBatchOperationResult_CalculatesSuccessRate()
    {
        // Arrange
        var result = new BatchOperationResult
        {
            SuccessCount = 75,
            FailureCount = 25,
            Duration = TimeSpan.FromSeconds(1)
        };

        // Act
        var successRate = result.SuccessRate;

        // Assert
        Assert.AreEqual(75.0, successRate, 0.01, "Success rate should be 75%");
    }

    /// <summary>
    /// Tests that BatchOperationOptions has reasonable defaults.
    /// </summary>
    [TestMethod]
    public void TestBatchOperationOptions_HasReasonableDefaults()
    {
        // Arrange & Act
        var options = new BatchOperationOptions();

        // Assert
        Assert.AreEqual(32, options.BatchSize, "Default batch size should be 32");
        Assert.IsTrue(options.SortBatch, "Sorting should be enabled by default");
        Assert.AreEqual(100, options.ParallelThreshold, "Parallel threshold should be 100");
        Assert.IsNull(options.MaxItems, "MaxItems should be null by default");
    }

    /// <summary>
    /// Tests that UpdateOptions can be created with proper values.
    /// </summary>
    [TestMethod]
    public void TestUpdateOptions_CanBeCreatedAndConfigured()
    {
        // Arrange & Act
        var options = new UpdateOptions<string>
        {
            UseStrictMatching = true,
            ElementMatcher = s => s.StartsWith("Test"),
            EnableRollback = true
        };

        // Assert
        Assert.IsTrue(options.UseStrictMatching, "UseStrictMatching should be set");
        Assert.IsNotNull(options.ElementMatcher, "ElementMatcher should be set");
        Assert.IsTrue(options.EnableRollback, "EnableRollback should be set");
        
        // Test the matcher
        Assert.IsTrue(options.ElementMatcher("TestString"), "Matcher should return true for TestString");
        Assert.IsFalse(options.ElementMatcher("OtherString"), "Matcher should return false for OtherString");
    }
}