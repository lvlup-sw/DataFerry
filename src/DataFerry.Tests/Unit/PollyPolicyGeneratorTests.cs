using Microsoft.Extensions.Logging;
using Moq;
using Polly.CircuitBreaker;
using Polly.Timeout;
using Polly.Wrap;
using lvlup.DataFerry.Properties;
using lvlup.DataFerry.Utilities;

namespace lvlup.DataFerry.Tests.Unit;

[TestClass]
public class PollyPolicyGeneratorTests
{
    private Mock<ILogger> _mockLogger = null!;
    private ResiliencySettings _defaultSettings = null!;
    private ResiliencySettings _basicSettings = null!;
    private ResiliencySettings _advancedSettings = null!;

    [TestInitialize]
    public void TestInitialize()
    {
        _mockLogger = new Mock<ILogger>();
        _defaultSettings = new ResiliencySettings { DesiredPolicy = ResiliencyPatterns.None, TimeoutIntervalSeconds = 5 };
        _basicSettings = new ResiliencySettings { DesiredPolicy = ResiliencyPatterns.Basic, TimeoutIntervalSeconds = 5, RetryCount = 3, RetryIntervalSeconds = 1 };
        _advancedSettings = new ResiliencySettings
        {
            DesiredPolicy = ResiliencyPatterns.Advanced,
            TimeoutIntervalSeconds = 5,
            RetryCount = 3,
            RetryIntervalSeconds = 1,
            UseExponentialBackoff = true,
            CircuitBreakerCount = 2,
            CircuitBreakerIntervalMinutes = 1,
            BulkheadMaxParallelization = 10,
            BulkheadMaxQueuingActions = 5
        };
    }

    #region GenerateSyncPolicy Tests

    [TestMethod]
    public void GenerateSyncPolicy_WithNullLogger_ShouldThrowArgumentNullException()
    {
        // Arrange
        ResiliencySettings settings = _defaultSettings;
        string fallbackValue = "fallback";

        // Act & Assert
        Assert.ThrowsException<ArgumentNullException>(() =>
            PollyPolicyGenerator.GenerateSyncPolicy<string>(null!, settings, fallbackValue));
    }

    [TestMethod]
    public void GenerateSyncPolicy_WithNullSettings_ShouldThrowArgumentNullException()
    {
        // Arrange
        string fallbackValue = "fallback";

        // Act & Assert
        Assert.ThrowsException<ArgumentNullException>(() =>
            PollyPolicyGenerator.GenerateSyncPolicy<string>(_mockLogger.Object, null!, fallbackValue));
    }

    [TestMethod]
    public void GenerateSyncPolicy_DefaultPattern_ShouldReturnPolicyWrap()
    {
        // Arrange
        string fallbackValue = "default_fallback";

        // Act
        var policy = PollyPolicyGenerator.GenerateSyncPolicy<string>(_mockLogger.Object, _defaultSettings, fallbackValue);

        // Assert
        Assert.IsNotNull(policy);
        Assert.IsInstanceOfType(policy, typeof(PolicyWrap<string>));
        // Further assertions could potentially inspect the policy structure if needed/possible,
        // or execute it against mock actions to verify behavior (e.g., fallback triggers).
        // For simplicity here, we check it's created.

        // Example execution test (conceptual)
        var result = policy.Execute(() => throw new TimeoutRejectedException("Simulated timeout"));
        Assert.AreEqual(fallbackValue, result);
         _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Fallback policy executed")),
                It.IsAny<TimeoutRejectedException>(), // Check the *inner* exception type logged
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [TestMethod]
    public void GenerateSyncPolicy_BasicPattern_ShouldReturnPolicyWrap()
    {
        // Arrange
        string fallbackValue = "basic_fallback";

        // Act
        var policy = PollyPolicyGenerator.GenerateSyncPolicy<string>(_mockLogger.Object, _basicSettings, fallbackValue, typeof(IOException));

        // Assert
        Assert.IsNotNull(policy);
        Assert.IsInstanceOfType(policy, typeof(PolicyWrap<string>));

         // Example execution test (conceptual) - force multiple failures to test retry and fallback
        int attempts = 0;
        var result = policy.Execute(() =>
        {
            attempts++;
            if (attempts <= _basicSettings.RetryCount) // Fail for all initial attempts + retries
            {
                 throw new IOException("Simulated IO failure");
            }
             // This part should ideally not be reached if fallback works correctly after retries
             return "should_not_reach";
        });

        Assert.AreEqual(fallbackValue, result);
        Assert.AreEqual(_basicSettings.RetryCount + 1, attempts); // Initial + retries
         _mockLogger.Verify(
             log => log.Log(
                 It.Is<LogLevel>(l => l == LogLevel.Warning || l == LogLevel.Error), // Retries log Warning, final logs Error
                 It.IsAny<EventId>(),
                 It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Retry") || v.ToString()!.Contains("Fallback policy executed")),
                 It.IsAny<Exception>(),
                 It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
             Times.Exactly(_basicSettings.RetryCount + 1)); // Retries + Fallback log
    }

    [TestMethod]
    public void GenerateSyncPolicy_AdvancedPattern_ShouldReturnPolicyWrap()
    {
        // Arrange
        string fallbackValue = "advanced_fallback";

        // Act
        var policy = PollyPolicyGenerator.GenerateSyncPolicy<string>(_mockLogger.Object, _advancedSettings, fallbackValue, typeof(HttpRequestException));

        // Assert
        Assert.IsNotNull(policy);
        Assert.IsInstanceOfType(policy, typeof(PolicyWrap<string>));
        // More complex execution tests needed here to verify circuit breaker, bulkhead etc.
    }

    #endregion

    #region GenerateAsyncPolicy Tests

    [TestMethod]
    public async Task GenerateAsyncPolicy_WithNullLogger_ShouldThrowArgumentNullException()
    {
        // Arrange
        ResiliencySettings settings = _defaultSettings;
        string fallbackValue = "fallback";

        // Act & Assert
        await Assert.ThrowsExceptionAsync<ArgumentNullException>(async () =>
             PollyPolicyGenerator.GenerateAsyncPolicy<string>(null!, settings, fallbackValue));
    }

    [TestMethod]
    public async Task GenerateAsyncPolicy_WithNullSettings_ShouldThrowArgumentNullException()
    {
        // Arrange
        string fallbackValue = "fallback";

        // Act & Assert
        await Assert.ThrowsExceptionAsync<ArgumentNullException>(async () =>
             PollyPolicyGenerator.GenerateAsyncPolicy<string>(_mockLogger.Object, null!, fallbackValue));
    }

    [TestMethod]
    public async Task GenerateAsyncPolicy_DefaultPattern_ShouldReturnAsyncPolicyWrap()
    {
        // Arrange
        string fallbackValue = "default_async_fallback";

        // Act
        var policy = PollyPolicyGenerator.GenerateAsyncPolicy<string>(_mockLogger.Object, _defaultSettings, fallbackValue);

        // Assert
        Assert.IsNotNull(policy);
        Assert.IsInstanceOfType(policy, typeof(AsyncPolicyWrap<string>));

        // Example execution test (conceptual)
        var result = await policy.ExecuteAsync(async () =>
        {
            await Task.Delay(10); // Simulate work
            throw new TimeoutRejectedException("Simulated async timeout");
        });
        Assert.AreEqual(fallbackValue, result);
         _mockLogger.Verify(
             x => x.Log(
                 LogLevel.Error,
                 It.IsAny<EventId>(),
                 It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Fallback policy executed")),
                 It.IsAny<TimeoutRejectedException>(),
                 It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
             Times.Once);
    }

    [TestMethod]
    public async Task GenerateAsyncPolicy_BasicPattern_ShouldReturnAsyncPolicyWrap()
    {
        // Arrange
        string fallbackValue = "basic_async_fallback";

        // Act
        var policy = PollyPolicyGenerator.GenerateAsyncPolicy<string>(_mockLogger.Object, _basicSettings, fallbackValue, typeof(IOException));

        // Assert
        Assert.IsNotNull(policy);
        Assert.IsInstanceOfType(policy, typeof(AsyncPolicyWrap<string>));

        // Example execution test (conceptual)
        int attempts = 0;
        var result = await policy.ExecuteAsync(async () =>
        {
            attempts++;
            await Task.Delay(10); // Simulate async work
            if (attempts <= _basicSettings.RetryCount)
            {
                throw new IOException("Simulated async IO failure");
            }
            return "should_not_reach";
        });

        Assert.AreEqual(fallbackValue, result);
        Assert.AreEqual(_basicSettings.RetryCount + 1, attempts);
        _mockLogger.Verify(
             log => log.Log(
                 It.Is<LogLevel>(l => l == LogLevel.Warning || l == LogLevel.Error),
                 It.IsAny<EventId>(),
                 It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Retry") || v.ToString()!.Contains("Fallback policy executed")),
                 It.IsAny<Exception>(),
                 It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
             Times.Exactly(_basicSettings.RetryCount + 1)); // Retries + Fallback
    }

    [TestMethod]
    public async Task GenerateAsyncPolicy_AdvancedPattern_ShouldReturnAsyncPolicyWrap()
    {
        // Arrange
        string fallbackValue = "advanced_async_fallback";

        // Act
        var policy = PollyPolicyGenerator.GenerateAsyncPolicy<string>(_mockLogger.Object, _advancedSettings, fallbackValue, typeof(HttpRequestException));

        // Assert
        Assert.IsNotNull(policy);
        Assert.IsInstanceOfType(policy, typeof(AsyncPolicyWrap<string>));
        // More complex execution tests needed here.
    }

    #endregion

    #region CreateExceptionPredicate Tests

    [TestMethod]
    public void CreateExceptionPredicate_NullTypes_ShouldHandleAnyException()
    {
        // Arrange
        var predicate = PollyPolicyGenerator.CreateExceptionPredicate(null);

        // Act & Assert
        Assert.IsTrue(predicate(new Exception()));
        Assert.IsTrue(predicate(new IOException()));
        Assert.IsTrue(predicate(new ArgumentNullException()));
        Assert.IsTrue(predicate(new TimeoutRejectedException())); // Polly specific
        Assert.IsTrue(predicate(new BrokenCircuitException()));  // Polly specific
    }

    [TestMethod]
    public void CreateExceptionPredicate_EmptyTypes_ShouldHandleAnyException()
    {
        // Arrange
        var predicate = PollyPolicyGenerator.CreateExceptionPredicate(Array.Empty<Type>());

        // Act & Assert
        Assert.IsTrue(predicate(new Exception()));
        Assert.IsTrue(predicate(new IOException()));
        Assert.IsTrue(predicate(new ArgumentNullException()));
    }

    [TestMethod]
    public void CreateExceptionPredicate_SpecificTypes_ShouldHandleOnlySpecifiedAndDerivedTypes()
    {
        // Arrange
        var predicate = PollyPolicyGenerator.CreateExceptionPredicate(new[] { typeof(IOException), typeof(ArgumentException) });

        // Act & Assert
        Assert.IsTrue(predicate(new IOException()));
        Assert.IsTrue(predicate(new FileNotFoundException())); // Derived from IOException
        Assert.IsTrue(predicate(new ArgumentException()));
        Assert.IsTrue(predicate(new ArgumentNullException())); // Derived from ArgumentException

        Assert.IsFalse(predicate(new InvalidOperationException()));
        Assert.IsFalse(predicate(new HttpRequestException()));
        Assert.IsFalse(predicate(new Exception())); // Base Exception type not explicitly listed
        Assert.IsFalse(predicate(new TimeoutRejectedException()));
    }

    #endregion

     #region Internal Pattern Method Tests (Conceptual - testing via public methods is preferred)

     // These tests demonstrate how you *might* test internal methods if needed,
     // but often testing the public API that uses them is sufficient.
     // Note: Making internal methods visible might require [InternalsVisibleTo] attribute.

     [TestMethod]
     public void GetDefaultAsyncPattern_ShouldIncludeTimeoutAndFallback()
     {
         // Arrange
         string fallback = "test";

         // Act
         // Assuming InternalsVisibleTo is set for the test project
         var policy = PollyPolicyGenerator.GetDefaultAsyncPattern<string>(_mockLogger.Object, _defaultSettings, fallback);
         var result = policy.ExecuteAsync(() => throw new InvalidOperationException("force fallback")).Result; // Sync over async for simplicity here

         // Assert
         Assert.IsNotNull(policy);
         Assert.AreEqual(fallback, result);
         // Check logs for fallback execution
         _mockLogger.Verify(
             x => x.Log(
                 LogLevel.Error, It.IsAny<EventId>(), It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Fallback policy executed")), It.IsAny<InvalidOperationException>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
             Times.Once);
     }

     [TestMethod]
     public void GetBasicSyncPattern_ShouldIncludeTimeoutRetryFallback()
     {
          // Arrange
          string fallback = "test_sync_basic";
          var predicate = PollyPolicyGenerator.CreateExceptionPredicate(new[] { typeof(IOException) });

          // Act
          var policy = PollyPolicyGenerator.GetBasicSyncPattern<string>(_mockLogger.Object, _basicSettings, fallback, predicate);

          int attempts = 0;
          var result = policy.Execute(() => {
              attempts++;
              if (attempts <= _basicSettings.RetryCount) throw new IOException();
              return "success"; // Should not be reached if fallback triggered
          });


          // Assert
          Assert.IsNotNull(policy);
          Assert.AreEqual(fallback, result);
          Assert.AreEqual(_basicSettings.RetryCount + 1, attempts); // Initial + All retries failed

         // Verify retry logging and fallback logging occurred (similar to public method tests)
         _mockLogger.Verify(
              log => log.Log(LogLevel.Warning, It.IsAny<EventId>(), It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Retry 1 of")), It.IsAny<IOException>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
         _mockLogger.Verify(
              log => log.Log(LogLevel.Warning, It.IsAny<EventId>(), It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Retry 2 of")), It.IsAny<IOException>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
         _mockLogger.Verify(
              log => log.Log(LogLevel.Error, It.IsAny<EventId>(), It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains($"Retry limit ({_basicSettings.RetryCount}) reached")), It.IsAny<IOException>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once); // Final retry logs error
         _mockLogger.Verify(
             log => log.Log(LogLevel.Error, It.IsAny<EventId>(), It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Fallback policy executed")), It.IsAny<IOException>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
     }

     #endregion
     [TestMethod]
        public void GetAdvancedSyncPattern_ShouldIncludeAllPoliciesAndTriggerFallback()
        {
            // Arrange
            string fallbackValue = "advanced_sync_fallback";
            // Use settings where circuit breaker opens quickly
            var specificAdvancedSettings = new ResiliencySettings
            {
                DesiredPolicy = ResiliencyPatterns.Advanced, // Not strictly needed for internal call
                TimeoutIntervalSeconds = 1, // Short timeout
                RetryCount = 1, // Few retries
                RetryIntervalSeconds = 1,
                CircuitBreakerCount = 2, // Open after 2 handled exceptions
                CircuitBreakerIntervalMinutes = 1,
                BulkheadMaxParallelization = 10,
                BulkheadMaxQueuingActions = 5
            };
            var predicate = PollyPolicyGenerator.CreateExceptionPredicate(new[] { typeof(HttpRequestException) });
            int attempts = 0;

            // Act
            var policy = PollyPolicyGenerator.GetAdvancedSyncPattern<string>(_mockLogger.Object, specificAdvancedSettings, fallbackValue, predicate);

            // Execute multiple times to trigger retries and circuit breaker
            string result = "initial";
            Exception? caughtException = null;
            try
            {
                // First failure (attempt 1) -> Retry 1
                // Second failure (retry 1) -> Circuit Breaker Opens (2 failures handled) -> Fallback
                 result = policy.Execute(() =>
                 {
                     attempts++;
                     throw new HttpRequestException("Simulated HTTP failure");
                 });

                // Try executing again - should hit open circuit breaker immediately -> Fallback
                // Note: Depending on Polly version/exact config, fallback might catch BrokenCircuitException
                 string resultAfterBreak = policy.Execute(() => "Should not execute");

                 // Assert this second result if needed, depends on exact test goal
                 Assert.AreEqual(fallbackValue, resultAfterBreak, "Execution after break should also fallback");

            }
            catch (Exception ex)
            {
                caughtException = ex; // Should not happen if fallback works
            }


            // Assert
            Assert.IsNull(caughtException, "Fallback should have prevented exception propagation");
            Assert.AreEqual(fallbackValue, result);
            // Attempts: 1 initial + 1 retry = 2
            Assert.AreEqual(specificAdvancedSettings.RetryCount + 1, attempts);

            // Verify logging: Retry, Circuit Breaker Open, Fallback
            _mockLogger.Verify(log => log.Log(LogLevel.Error, It.IsAny<EventId>(), It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains($"Retry limit ({specificAdvancedSettings.RetryCount}) reached")), It.IsAny<HttpRequestException>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once); // Final retry logs error
            _mockLogger.Verify(log => log.Log(LogLevel.Error, It.IsAny<EventId>(), It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Circuit breaker opened")), It.IsAny<HttpRequestException>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once); // Circuit breaker logs onBreak
             // Fallback logging - Exception type might be HttpRequestException or wrapped by Polly
            _mockLogger.Verify(log => log.Log(LogLevel.Error, It.IsAny<EventId>(), It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Fallback policy executed")), It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.AtLeastOnce); // Fallback logs error
        }

        [TestMethod]
        public async Task GetAdvancedAsyncPattern_ShouldIncludeAllPoliciesAndTriggerFallback()
        {
            // Arrange
            string fallbackValue = "advanced_async_fallback";
             var specificAdvancedSettings = new ResiliencySettings
            {
                DesiredPolicy = ResiliencyPatterns.Advanced,
                TimeoutIntervalSeconds = 1,
                RetryCount = 1,
                RetryIntervalSeconds = 1,
                CircuitBreakerCount = 2, // Open after 2 handled exceptions
                CircuitBreakerIntervalMinutes = 1,
                BulkheadMaxParallelization = 10,
                BulkheadMaxQueuingActions = 5
            };
            var predicate = PollyPolicyGenerator.CreateExceptionPredicate(new[] { typeof(HttpRequestException) });
            int attempts = 0;

            // Act
            var policy = PollyPolicyGenerator.GetAdvancedAsyncPattern<string>(_mockLogger.Object, specificAdvancedSettings, fallbackValue, predicate);

            string result = "initial";
            Exception? caughtException = null;
             try
             {
                // First failure (attempt 1) -> Retry 1
                // Second failure (retry 1) -> Circuit Breaker Opens (2 failures handled) -> Fallback
                  result = await policy.ExecuteAsync(async () =>
                  {
                      attempts++;
                      await Task.Delay(10);
                      throw new HttpRequestException("Simulated Async HTTP failure");
                  });

                 // Try executing again - should hit open circuit breaker immediately -> Fallback
                 string resultAfterBreak = await policy.ExecuteAsync(async () =>
                 {
                    await Task.Delay(1); // Simulate work that won't happen
                    return "Should not execute";
                 });
                 Assert.AreEqual(fallbackValue, resultAfterBreak, "Execution after break should also fallback");
             }
             catch (Exception ex)
             {
                 caughtException = ex; // Should not happen
             }

            // Assert
            Assert.IsNull(caughtException, "Fallback should have prevented exception propagation");
            Assert.AreEqual(fallbackValue, result);
            Assert.AreEqual(specificAdvancedSettings.RetryCount + 1, attempts); // Initial + 1 retry = 2

             // Verify logging: Retry, Circuit Breaker Open, Fallback (async versions)
             _mockLogger.Verify(log => log.Log(LogLevel.Error, It.IsAny<EventId>(), It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains($"Retry limit ({specificAdvancedSettings.RetryCount}) reached")), It.IsAny<HttpRequestException>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
             _mockLogger.Verify(log => log.Log(LogLevel.Error, It.IsAny<EventId>(), It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Circuit breaker opened")), It.IsAny<HttpRequestException>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
             _mockLogger.Verify(log => log.Log(LogLevel.Error, It.IsAny<EventId>(), It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Fallback policy executed")), It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.AtLeastOnce);
        }


        [TestMethod]
        public async Task GetAsyncBackgroundTaskPattern_ShouldRetryAndPropagateFinalException()
        {
            // Arrange
            // Use settings with a few retries
             var specificBasicSettings = new ResiliencySettings
             {
                 DesiredPolicy = ResiliencyPatterns.Basic, // Not strictly needed for internal call
                 TimeoutIntervalSeconds = 1, // Short timeout
                 RetryCount = 2, // Allow a couple of retries
                 RetryIntervalSeconds = 1,
             };
            var predicate = PollyPolicyGenerator.CreateExceptionPredicate(new[] { typeof(IOException) });
            int attempts = 0;
            var expectedException = new IOException("Simulated final IO failure");

            // Act
            var policy = PollyPolicyGenerator.GetAsyncBackgroundTaskPattern(_mockLogger.Object, specificBasicSettings, predicate);

            // Execute and expect exception
            IOException? caughtException = await Assert.ThrowsExceptionAsync<IOException>(async () =>
            {
                 await policy.ExecuteAsync(async () =>
                 {
                     attempts++;
                     await Task.Delay(10);
                     // Always throw the same exception type to ensure predicate matches and retries occur
                     throw expectedException;
                 });
            });

            // Assert
            Assert.IsNotNull(caughtException);
            Assert.AreSame(expectedException, caughtException, "The original exception instance should be propagated.");
            // Attempts: 1 initial + 2 retries = 3
            Assert.AreEqual(specificBasicSettings.RetryCount + 1, attempts);

            // Verify logging: Retries should happen, including the final error log
            _mockLogger.Verify(log => log.Log(LogLevel.Warning, It.IsAny<EventId>(), It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Retry 1 of")), It.IsAny<IOException>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
            _mockLogger.Verify(log => log.Log(LogLevel.Error, It.IsAny<EventId>(), It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains($"Retry limit ({specificBasicSettings.RetryCount}) reached")), It.IsAny<IOException>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once); // Final retry logs error

            // Crucially, verify NO fallback logging occurred
            _mockLogger.Verify(log => log.Log(It.IsAny<LogLevel>(), It.IsAny<EventId>(), It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("Fallback policy executed")), It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Never);
        }
}