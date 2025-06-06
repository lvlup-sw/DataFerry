// ===========================================================================
// <copyright file="IActivityExtensions.cs" company="Level Up Software">
// Copyright (c) Level Up Software. All rights reserved.
// </copyright>
// ===========================================================================

using lvlup.DataFerry.Concurrency.Contracts;

namespace lvlup.DataFerry.Concurrency.Observability;

/// <summary>
/// Extension methods for the IActivity interface to provide queue-specific functionality.
/// </summary>
public static class IActivityExtensions
{
    /// <summary>
    /// Adds queue-specific tags to an activity.
    /// </summary>
    /// <param name="activity">The activity to enhance.</param>
    /// <param name="queueName">The name of the queue.</param>
    /// <param name="operationType">The type of operation being performed.</param>
    /// <param name="queueSize">Optional current queue size.</param>
    /// <returns>The activity for method chaining.</returns>
    public static IActivity? AddQueueTags(
        this IActivity? activity,
        string queueName,
        string operationType,
        int? queueSize = null)
    {
        if (activity == null)
        {
            return null;
        }

        activity.AddTag("queue.name", queueName);
        activity.AddTag("queue.operation", operationType);
        
        if (queueSize.HasValue)
        {
            activity.AddTag("queue.size", queueSize.Value);
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
    public static IActivity? AddPriorityInfo<TPriority>(
        this IActivity? activity,
        TPriority priority,
        bool hasElement = true)
    {
        if (activity == null)
        {
            return null;
        }

        activity.AddTag("queue.priority", priority?.ToString() ?? "null");
        activity.AddTag("queue.has_element", hasElement);
        
        return activity;
    }

    /// <summary>
    /// Records the successful completion of an operation.
    /// </summary>
    /// <param name="activity">The activity to update.</param>
    /// <param name="resultDetails">Optional details about the result.</param>
    /// <returns>The activity for method chaining.</returns>
    public static IActivity? RecordSuccess(
        this IActivity? activity,
        string? resultDetails = null)
    {
        if (activity == null)
        {
            return null;
        }

        activity.SetStatus(ActivityStatusCode.Ok);
        activity.AddTag("queue.operation.success", true);
        
        if (!string.IsNullOrEmpty(resultDetails))
        {
            activity.AddTag("queue.operation.result", resultDetails);
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
    public static IActivity? RecordFailure(
        this IActivity? activity,
        string reason,
        Exception? exception = null)
    {
        if (activity == null)
        {
            return null;
        }

        activity.SetStatus(ActivityStatusCode.Error, reason);
        activity.AddTag("queue.operation.success", false);
        activity.AddTag("queue.operation.failure_reason", reason);
        
        if (exception != null)
        {
            activity.AddEvent("exception");
            activity.AddTag("exception.type", exception.GetType().Name);
            activity.AddTag("exception.message", exception.Message);
        }
        
        return activity;
    }
}