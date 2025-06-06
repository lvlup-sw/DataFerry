// ===========================================================================
// <copyright file="ActivityExtensions.cs" company="Level Up Software">
// Copyright (c) Level Up Software. All rights reserved.
// </copyright>
// ===========================================================================

using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace lvlup.DataFerry.Concurrency.Observability;

/// <summary>
/// Provides extension methods for enhanced distributed tracing and activity management.
/// </summary>
/// <remarks>
/// <para>
/// This static class contains utility methods that simplify working with .NET's Activity API
/// for distributed tracing. It provides convenience methods for adding structured data,
/// handling errors, and measuring operation performance.
/// </para>
/// <para>
/// The extensions support both synchronous and asynchronous operations, with automatic
/// exception handling and performance measurement capabilities.
/// </para>
/// </remarks>
public static class ActivityExtensions
{
    #region Activity Enhancement Methods

    /// <summary>
    /// Adds queue-specific tags to an activity.
    /// </summary>
    /// <param name="activity">The activity to enhance.</param>
    /// <param name="queueName">The name of the queue.</param>
    /// <param name="operationType">The type of operation being performed.</param>
    /// <param name="queueSize">Optional current queue size.</param>
    /// <returns>The activity for method chaining.</returns>
    public static Activity? AddQueueTags(
        this Activity? activity,
        string queueName,
        string operationType,
        int? queueSize = null)
    {
        if (activity == null)
        {
            return null;
        }

        activity.SetTag("queue.name", queueName);
        activity.SetTag("queue.operation", operationType);
        
        if (queueSize.HasValue)
        {
            activity.SetTag("queue.size", queueSize.Value);
        }
        
        return activity;
    }

    /// <summary>
    /// Adds priority and element information to an activity.
    /// </summary>
    /// <typeparam name="TPriority">The type of the priority.</typeparam>
    /// <param name="activity">The activity to enhance.</param>
    /// <param name="priority">The priority value.</param>
    /// <param name="hasElement">Whether an element is associated with this operation.</param>
    /// <returns>The activity for method chaining.</returns>
    public static Activity? AddPriorityInfo<TPriority>(
        this Activity? activity,
        TPriority priority,
        bool hasElement = true)
    {
        if (activity == null)
        {
            return null;
        }

        activity.SetTag("queue.priority", priority?.ToString() ?? "null");
        activity.SetTag("queue.has_element", hasElement);
        
        return activity;
    }

    /// <summary>
    /// Adds contention information to an activity.
    /// </summary>
    /// <param name="activity">The activity to enhance.</param>
    /// <param name="contentionType">The type of contention encountered.</param>
    /// <param name="retryCount">The number of retries performed.</param>
    /// <param name="waitTimeMs">The total time spent waiting.</param>
    /// <returns>The activity for method chaining.</returns>
    public static Activity? AddContentionInfo(
        this Activity? activity,
        string contentionType,
        int retryCount,
        double waitTimeMs)
    {
        if (activity == null)
        {
            return null;
        }

        activity.SetTag("queue.contention.type", contentionType);
        activity.SetTag("queue.contention.retries", retryCount);
        activity.SetTag("queue.contention.wait_ms", waitTimeMs);
        
        if (retryCount > 0)
        {
            activity.AddEvent(new ActivityEvent("ContentionDetected", 
                tags: new ActivityTagsCollection
                {
                    ["type"] = contentionType,
                    ["retries"] = retryCount
                }));
        }
        
        return activity;
    }

    /// <summary>
    /// Records the successful completion of an operation.
    /// </summary>
    /// <param name="activity">The activity to update.</param>
    /// <param name="resultDetails">Optional details about the result.</param>
    /// <returns>The activity for method chaining.</returns>
    public static Activity? RecordSuccess(
        this Activity? activity,
        string? resultDetails = null)
    {
        if (activity == null)
        {
            return null;
        }

        activity.SetStatus(ActivityStatusCode.Ok);
        activity.SetTag("queue.operation.success", true);
        
        if (!string.IsNullOrEmpty(resultDetails))
        {
            activity.SetTag("queue.operation.result", resultDetails);
        }
        
        return activity;
    }

    /// <summary>
    /// Records the failure of an operation.
    /// </summary>
    /// <param name="activity">The activity to update.</param>
    /// <param name="reason">The reason for failure.</param>
    /// <param name="exception">Optional exception that caused the failure.</param>
    /// <returns>The activity for method chaining.</returns>
    public static Activity? RecordFailure(
        this Activity? activity,
        string reason,
        Exception? exception = null)
    {
        if (activity == null)
        {
            return null;
        }

        activity.SetStatus(ActivityStatusCode.Error, reason);
        activity.SetTag("queue.operation.success", false);
        activity.SetTag("queue.operation.failure_reason", reason);
        
        if (exception != null)
        {
            activity.RecordException(exception);
        }
        
        return activity;
    }

    /// <summary>
    /// Records an exception with structured data in the activity.
    /// </summary>
    /// <param name="activity">The activity to update.</param>
    /// <param name="exception">The exception to record.</param>
    /// <returns>The activity for method chaining.</returns>
    public static Activity? RecordException(this Activity? activity, Exception exception)
    {
        if (activity == null)
        {
            return null;
        }

        activity.AddEvent(new ActivityEvent("exception",
            tags: new ActivityTagsCollection
            {
                ["exception.type"] = exception.GetType().FullName,
                ["exception.message"] = exception.Message,
                ["exception.stacktrace"] = exception.StackTrace
            }));
            
        activity.SetTag("error", true);
        activity.SetTag("exception.type", exception.GetType().Name);
        
        return activity;
    }

    #endregion

    #region Operation Execution Methods

    /// <summary>
    /// Executes an operation within an activity context, automatically handling timing and errors.
    /// </summary>
    /// <typeparam name="T">The return type of the operation.</typeparam>
    /// <param name="activitySource">The activity source to create activities from.</param>
    /// <param name="operationName">The name of the operation.</param>
    /// <param name="operation">The operation to execute.</param>
    /// <param name="tags">Optional tags to add to the activity.</param>
    /// <returns>The result of the operation.</returns>
    public static T ExecuteWithActivity<T>(
        this ActivitySource activitySource,
        string operationName,
        Func<Activity?, T> operation,
        params KeyValuePair<string, object?>[] tags)
    {
        using var activity = activitySource.StartActivity(operationName, ActivityKind.Internal);
        
        if (activity != null && tags.Length > 0)
        {
            foreach (var tag in tags)
            {
                activity.SetTag(tag.Key, tag.Value);
            }
        }
        
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            var result = operation(activity);
            activity?.RecordSuccess();
            return result;
        }
        catch (Exception ex)
        {
            activity?.RecordFailure("Operation threw exception", ex);
            throw;
        }
        finally
        {
            activity?.SetTag("duration.ms", stopwatch.ElapsedMilliseconds);
        }
    }

    /// <summary>
    /// Executes an asynchronous operation within an activity context.
    /// </summary>
    /// <typeparam name="T">The return type of the operation.</typeparam>
    /// <param name="activitySource">The activity source to create activities from.</param>
    /// <param name="operationName">The name of the operation.</param>
    /// <param name="operation">The asynchronous operation to execute.</param>
    /// <param name="tags">Optional tags to add to the activity.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    public static async Task<T> ExecuteWithActivityAsync<T>(
        this ActivitySource activitySource,
        string operationName,
        Func<Activity?, Task<T>> operation,
        params KeyValuePair<string, object?>[] tags)
    {
        using var activity = activitySource.StartActivity(operationName, ActivityKind.Internal);
        
        if (activity != null && tags.Length > 0)
        {
            foreach (var tag in tags)
            {
                activity.SetTag(tag.Key, tag.Value);
            }
        }
        
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            var result = await operation(activity).ConfigureAwait(false);
            activity?.RecordSuccess();
            return result;
        }
        catch (Exception ex)
        {
            activity?.RecordFailure("Operation threw exception", ex);
            throw;
        }
        finally
        {
            activity?.SetTag("duration.ms", stopwatch.ElapsedMilliseconds);
        }
    }

    #endregion

    #region Baggage and Context Methods

    /// <summary>
    /// Adds queue context to the current activity's baggage.
    /// </summary>
    /// <param name="queueName">The name of the queue.</param>
    /// <param name="queueId">Optional unique identifier for the queue instance.</param>
    public static void SetQueueContext(string queueName, string? queueId = null)
    {
        Activity.Current?.SetBaggage("queue.name", queueName);
        
        if (!string.IsNullOrEmpty(queueId))
        {
            Activity.Current?.SetBaggage("queue.id", queueId);
        }
    }

    /// <summary>
    /// Retrieves queue context from the current activity's baggage.
    /// </summary>
    /// <returns>A tuple containing the queue name and optional queue ID.</returns>
    public static (string? QueueName, string? QueueId) GetQueueContext()
    {
        var current = Activity.Current;
        if (current == null)
        {
            return (null, null);
        }
        
        var queueName = current.GetBaggageItem("queue.name");
        var queueId = current.GetBaggageItem("queue.id");
        
        return (queueName, queueId);
    }

    #endregion

    #region Performance Measurement Methods

    /// <summary>
    /// Creates a disposable timer that records elapsed time to an activity when disposed.
    /// </summary>
    /// <param name="activity">The activity to record timing to.</param>
    /// <param name="metricName">The name of the timing metric.</param>
    /// <returns>A disposable timer.</returns>
    public static IDisposable? MeasureTime(this Activity? activity, string metricName)
    {
        if (activity == null)
        {
            return null;
        }
        
        return new ActivityTimer(activity, metricName);
    }

    /// <summary>
    /// Measures the time taken by a code block and adds it as an event.
    /// </summary>
    /// <param name="activity">The activity to add the measurement to.</param>
    /// <param name="blockName">The name of the code block being measured.</param>
    /// <param name="action">The action to measure.</param>
    public static void MeasureBlock(this Activity? activity, string blockName, Action action)
    {
        if (activity == null)
        {
            action();
            return;
        }
        
        var stopwatch = Stopwatch.StartNew();
        try
        {
            action();
            activity.AddEvent(new ActivityEvent($"{blockName}.completed",
                tags: new ActivityTagsCollection
                {
                    ["duration.ms"] = stopwatch.ElapsedMilliseconds,
                    ["success"] = true
                }));
        }
        catch
        {
            activity.AddEvent(new ActivityEvent($"{blockName}.failed",
                tags: new ActivityTagsCollection
                {
                    ["duration.ms"] = stopwatch.ElapsedMilliseconds,
                    ["success"] = false
                }));
            throw;
        }
    }

    #endregion

    #region Nested Types

    /// <summary>
    /// Implements a disposable timer for measuring operation duration.
    /// </summary>
    private sealed class ActivityTimer : IDisposable
    {
        private readonly Activity _activity;
        private readonly string _metricName;
        private readonly Stopwatch _stopwatch;
        private bool _disposed;

        public ActivityTimer(Activity activity, string metricName)
        {
            _activity = activity;
            _metricName = metricName;
            _stopwatch = Stopwatch.StartNew();
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _activity.SetTag($"{_metricName}.duration.ms", _stopwatch.ElapsedMilliseconds);
                _disposed = true;
            }
        }
    }

    #endregion

    #region Queue-Specific Activity Helpers

    /// <summary>
    /// Creates an activity span for a queue operation with automatic resource tracking.
    /// </summary>
    /// <param name="activitySource">The activity source.</param>
    /// <param name="operationType">The type of queue operation.</param>
    /// <param name="queueName">The name of the queue.</param>
    /// <param name="currentSize">The current size of the queue.</param>
    /// <param name="maxSize">The maximum size of the queue.</param>
    /// <returns>A started activity or null if tracing is disabled.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static Activity? StartQueueOperation(
        this ActivitySource activitySource,
        string operationType,
        string queueName,
        int currentSize,
        int maxSize)
    {
        var activity = activitySource.StartActivity(
            $"Queue.{operationType}",
            ActivityKind.Internal);
            
        if (activity != null)
        {
            activity.SetTag("queue.name", queueName);
            activity.SetTag("queue.operation", operationType);
            activity.SetTag("queue.current_size", currentSize);
            activity.SetTag("queue.max_size", maxSize);
            activity.SetTag("queue.utilization", (double)currentSize / maxSize);
        }
        
        return activity;
    }

    /// <summary>
    /// Adds spray operation details to an activity.
    /// </summary>
    /// <param name="activity">The activity to enhance.</param>
    /// <param name="jumpLength">The spray jump length.</param>
    /// <param name="startLevel">The starting level of the spray.</param>
    /// <param name="nodesVisited">The number of nodes visited.</param>
    /// <returns>The activity for method chaining.</returns>
    public static Activity? AddSprayDetails(
        this Activity? activity,
        int jumpLength,
        int startLevel,
        int nodesVisited)
    {
        if (activity == null)
        {
            return null;
        }

        activity.SetTag("spray.jump_length", jumpLength);
        activity.SetTag("spray.start_level", startLevel);
        activity.SetTag("spray.nodes_visited", nodesVisited);
        
        activity.AddEvent(new ActivityEvent("SprayCompleted",
            tags: new ActivityTagsCollection
            {
                ["jump_length"] = jumpLength,
                ["nodes_visited"] = nodesVisited
            }));
            
        return activity;
    }

    #endregion
}