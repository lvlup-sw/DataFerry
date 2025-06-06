// ===========================================================================
// <copyright file="IQueueObservability.cs" company="Level Up Software">
// Copyright (c) Level Up Software. All rights reserved.
// </copyright>
// ===========================================================================

using System.Diagnostics;

using Microsoft.Extensions.Logging;

namespace lvlup.DataFerry.Concurrency.Contracts;

/// <summary>
/// Provides observability capabilities for queue operations including metrics, logging, and distributed tracing.
/// </summary>
/// <remarks>
/// <para>
/// This interface defines a contract for comprehensive observability of queue operations,
/// enabling monitoring, debugging, and performance analysis in production environments.
/// Implementations should minimize performance overhead while providing valuable insights.
/// </para>
/// </remarks>
public interface IQueueObservability
{
    /// <summary>
    /// Records metrics for a completed queue operation.
    /// </summary>
    /// <param name="operation">The name of the operation (e.g., "TryAdd", "TryDeleteMin").</param>
    /// <param name="success">Indicates whether the operation completed successfully.</param>
    /// <param name="durationMs">The duration of the operation in milliseconds.</param>
    /// <param name="tags">Optional additional tags for metric filtering and aggregation.</param>
    /// <remarks>
    /// This method should be called after each significant queue operation to track
    /// performance metrics and success rates. Tags can include contextual information
    /// like queue size, contention level, or operation-specific data.
    /// </remarks>
    void RecordOperation(string operation, bool success, double durationMs, Dictionary<string, object>? tags = null);

    /// <summary>
    /// Starts a distributed tracing activity for an operation.
    /// </summary>
    /// <param name="operationName">The name of the operation being traced.</param>
    /// <returns>An activity wrapper that should be disposed when the operation completes, or <c>null</c> if tracing is disabled.</returns>
    /// <remarks>
    /// Activities enable distributed tracing across service boundaries. The returned
    /// IActivity should be disposed in a finally block to ensure proper cleanup.
    /// </remarks>
    IActivity? StartActivity(string operationName);

    /// <summary>
    /// Logs an event with the specified log level and message.
    /// </summary>
    /// <param name="level">The severity level of the log event.</param>
    /// <param name="message">The log message template with placeholders for structured logging.</param>
    /// <param name="args">Arguments to be substituted into the message template.</param>
    /// <remarks>
    /// Uses structured logging to enable efficient log querying and analysis.
    /// Message templates should use named placeholders for better searchability.
    /// </remarks>
    void LogEvent(LogLevel level, string message, params object[] args);
}

/// <summary>
/// Represents a distributed tracing activity that tracks the execution of an operation.
/// </summary>
public interface IActivity : IDisposable
{
    /// <summary>
    /// Adds a tag to the activity for additional context.
    /// </summary>
    /// <param name="key">The tag key.</param>
    /// <param name="value">The tag value.</param>
    void AddTag(string key, object? value);

    /// <summary>
    /// Records an event within the activity timeline.
    /// </summary>
    /// <param name="name">The event name.</param>
    void AddEvent(string name);

    /// <summary>
    /// Sets the status of the activity to indicate success or failure.
    /// </summary>
    /// <param name="status">The activity status.</param>
    /// <param name="description">Optional description of the status.</param>
    void SetStatus(ActivityStatusCode status, string? description = null);
}

/// <summary>
/// Represents the status of an activity.
/// </summary>
public enum ActivityStatusCode
{
    /// <summary>
    /// The operation completed successfully.
    /// </summary>
    Ok,

    /// <summary>
    /// The operation encountered an error.
    /// </summary>
    Error
}