// ===========================================================================
// <copyright file="ObservabilityAdapter.cs" company="Level Up Software">
// Copyright (c) Level Up Software. All rights reserved.
// </copyright>
// ===========================================================================

using System.Diagnostics;

using Microsoft.Extensions.Logging;

using lvlup.DataFerry.Concurrency.Contracts;

namespace lvlup.DataFerry.Concurrency.Observability;

/// <summary>
/// Adapts the legacy ConcurrentPriorityQueueObservabilityHelper to implement IQueueObservability.
/// </summary>
/// <remarks>
/// This adapter allows gradual migration from the old observability helper to the new
/// interface-based approach while maintaining backward compatibility.
/// </remarks>
internal sealed class ObservabilityAdapter<TPriority, TElement> : IQueueObservability
{
    private readonly ConcurrentPriorityQueue<TPriority, TElement>.ConcurrentPriorityQueueObservabilityHelper _helper;
    private readonly ActivitySource _activitySource;

    /// <summary>
    /// Initializes a new instance of the <see cref="ObservabilityAdapter{TPriority,TElement}"/> class.
    /// </summary>
    /// <param name="helper">The legacy observability helper to wrap.</param>
    public ObservabilityAdapter(ConcurrentPriorityQueue<TPriority, TElement>.ConcurrentPriorityQueueObservabilityHelper helper)
    {
        ArgumentNullException.ThrowIfNull(helper, nameof(helper));
        
        _helper = helper;
        _activitySource = new ActivitySource($"DataFerry.ConcurrentPriorityQueue.Adapter");
    }

    /// <inheritdoc/>
    public void RecordOperation(string operation, bool success, double durationMs, Dictionary<string, object>? tags = null)
    {
        switch (operation)
        {
            case "TryAdd" when success && tags?.TryGetValue("Priority", out var priority) == true:
                _helper.RecordAddSuccess((TPriority)priority, tags.TryGetValue("Count", out var count) ? (int)count : 0, durationMs);
                break;
                
            case "TryAdd" when !success && tags?.TryGetValue("Priority", out var failPriority) == true:
                _helper.RecordAddFailure((TPriority)failPriority, durationMs);
                break;
                
            case "TryDeleteMin":
            case "TryDelete":
            case "TryDeleteAbsoluteMin":
                if (success)
                {
                    var deleteCount = 0;
                    if (tags?.TryGetValue("Count", out var delCount) == true && delCount is int countValue)
                    {
                        deleteCount = countValue;
                    }
                    _helper.RecordDeleteSuccess(operation.Replace("Try", "").ToLower(), deleteCount, durationMs);
                }
                else
                {
                    _helper.RecordDeleteFailure(operation.Replace("Try", "").ToLower(), durationMs);
                }
                break;
        }
    }

    /// <inheritdoc/>
    public IActivity? StartActivity(string operationName)
    {
        var activity = _helper.StartActivity(operationName);
        return activity != null ? new LegacyActivityWrapper(activity) : null;
    }

    /// <inheritdoc/>
    public void LogEvent(LogLevel level, string message, params object[] args)
    {
        // The legacy helper doesn't expose direct logging, so we'll use a workaround
        switch (level)
        {
            case LogLevel.Information when message.Contains("MaxSize exceeded"):
                _helper.LogMaxSizeExceeded(args.Length > 0 ? (int)args[0] : 0);
                break;
                
            case LogLevel.Debug when message.Contains("TryAdd called"):
                if (args.Length > 0 && args[0] is TPriority priority)
                {
                    _helper.LogAddAttempt(priority);
                }
                break;
                
            case LogLevel.Debug when message.Contains("Scheduling physical node removal"):
                if (args.Length >= 2 && args[0] is TPriority removePriority && args[1] is long sequence)
                {
                    _helper.LogPhysicalRemovalScheduled(removePriority, sequence);
                }
                break;
        }
    }

    /// <summary>
    /// Wraps a legacy Activity to implement IActivity.
    /// </summary>
    private sealed class LegacyActivityWrapper : IActivity
    {
        private readonly Activity _activity;
        private bool _disposed;

        public LegacyActivityWrapper(Activity activity)
        {
            _activity = activity ?? throw new ArgumentNullException(nameof(activity));
        }

        public void AddTag(string key, object? value)
        {
            ObjectDisposedException.ThrowIf(_disposed, nameof(LegacyActivityWrapper));
            _activity.SetTag(key, value);
        }

        public void AddEvent(string name)
        {
            ObjectDisposedException.ThrowIf(_disposed, nameof(LegacyActivityWrapper));
            _activity.AddEvent(new ActivityEvent(name));
        }

        public void SetStatus(Contracts.ActivityStatusCode status, string? description = null)
        {
            ObjectDisposedException.ThrowIf(_disposed, nameof(LegacyActivityWrapper));
            _activity.SetStatus(
                status == Contracts.ActivityStatusCode.Ok ? System.Diagnostics.ActivityStatusCode.Ok : System.Diagnostics.ActivityStatusCode.Error,
                description);
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _activity.Dispose();
                _disposed = true;
            }
        }
    }
}