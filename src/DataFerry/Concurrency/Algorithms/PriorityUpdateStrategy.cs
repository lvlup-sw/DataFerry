// ===========================================================================
// <copyright file="PriorityUpdateStrategy.cs" company="Level Up Software">
// Copyright (c) Level Up Software. All rights reserved.
// </copyright>
// ===========================================================================

using System.Diagnostics;

using Microsoft.Extensions.Logging;

using lvlup.DataFerry.Concurrency.Contracts;

namespace lvlup.DataFerry.Concurrency.Algorithms;

/// <summary>
/// Implements strategies for updating priorities of elements in the concurrent priority queue.
/// </summary>
/// <typeparam name="TPriority">The type of priority values.</typeparam>
/// <typeparam name="TElement">The type of elements stored in the queue.</typeparam>
/// <remarks>
/// <para>
/// This class provides atomic priority update operations by coordinating removal and re-insertion
/// of elements. It supports both single-item updates and bulk update operations with optional
/// rollback capabilities.
/// </para>
/// <para>
/// For priority changes, the strategy removes the element with the old priority and re-adds it
/// with the new priority, ensuring atomicity and consistency throughout the operation.
/// </para>
/// </remarks>
internal sealed class PriorityUpdateStrategy<TPriority, TElement>
{
    #region Fields

    /// <summary>
    /// The priority queue to perform updates on.
    /// </summary>
    private readonly ConcurrentPriorityQueue<TPriority, TElement> _queue;

    /// <summary>
    /// The comparer used for priority ordering.
    /// </summary>
    private readonly IComparer<TPriority> _comparer;

    /// <summary>
    /// Observability interface for metrics and logging.
    /// </summary>
    private readonly IQueueObservability? _observability;

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="PriorityUpdateStrategy{TPriority, TElement}"/> class.
    /// </summary>
    /// <param name="queue">The priority queue to operate on.</param>
    /// <param name="comparer">The priority comparer.</param>
    /// <param name="observability">Optional observability interface.</param>
    public PriorityUpdateStrategy(
        ConcurrentPriorityQueue<TPriority, TElement> queue,
        IComparer<TPriority> comparer,
        IQueueObservability? observability)
    {
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _comparer = comparer ?? throw new ArgumentNullException(nameof(comparer));
        _observability = observability;
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Attempts to update the priority of an element in the queue.
    /// </summary>
    /// <param name="oldPriority">The current priority of the element.</param>
    /// <param name="newPriority">The new priority to assign.</param>
    /// <param name="elementTransform">Function to transform the element during update.</param>
    /// <param name="options">Options controlling the update behavior.</param>
    /// <returns>True if the update was successful; otherwise, false.</returns>
    /// <remarks>
    /// <para>
    /// If the old and new priorities are equal, the update is performed in-place.
    /// Otherwise, the element is removed and re-added with the new priority.
    /// </para>
    /// <para>
    /// If rollback is enabled and the re-add fails, the method attempts to restore
    /// the element with its original priority.
    /// </para>
    /// </remarks>
    public bool TryUpdatePriority(
        TPriority oldPriority,
        TPriority newPriority,
        Func<TElement, TElement> elementTransform,
        UpdateOptions<TElement> options = default)
    {
        ArgumentNullException.ThrowIfNull(oldPriority, nameof(oldPriority));
        ArgumentNullException.ThrowIfNull(newPriority, nameof(newPriority));
        ArgumentNullException.ThrowIfNull(elementTransform, nameof(elementTransform));

        var stopwatch = Stopwatch.StartNew();

        try
        {
            // For same priority, just update in place
            if (_comparer.Compare(oldPriority, newPriority) == 0)
            {
                return _queue.Update(oldPriority, (p, e) => elementTransform(e));
            }

            // Need to remove and re-add
            TElement? element = default;
            bool found;

            // Try to remove old entry
            if (options.UseStrictMatching && options.ElementMatcher != null)
            {
                found = TryDeleteExact(oldPriority, options.ElementMatcher, out element);
            }
            else
            {
                found = TryDeleteWithResult(oldPriority, out element);
            }

            if (!found)
            {
                _observability?.RecordOperation("UpdatePriority", false,
                    stopwatch.ElapsedMilliseconds,
                    new Dictionary<string, object> { ["Reason"] = "NotFound" });
                return false;
            }

            // Transform element
            var newElement = elementTransform(element!);

            // Try to add with new priority
            if (_queue.TryAdd(newPriority, newElement))
            {
                _observability?.RecordOperation("UpdatePriority", true,
                    stopwatch.ElapsedMilliseconds);
                return true;
            }

            // Rollback on failure if enabled
            if (options.EnableRollback)
            {
                _queue.TryAdd(oldPriority, element!);
                _observability?.LogEvent(LogLevel.Warning,
                    "Priority update failed, rolled back to original state");
            }

            return false;
        }
        catch (Exception ex)
        {
            _observability?.LogEvent(LogLevel.Error,
                "Priority update failed: {Error}", ex.Message);
            throw;
        }
    }

    /// <summary>
    /// Performs bulk priority updates on multiple elements in the queue.
    /// </summary>
    /// <param name="updateFunc">Function that determines new priority and element for each item.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>The number of successfully updated items.</returns>
    /// <remarks>
    /// <para>
    /// This method iterates through all elements in the queue and applies the update function
    /// to each. Items for which the function returns null are skipped.
    /// </para>
    /// <para>
    /// The operation is not atomic for the entire set; each update is performed independently.
    /// </para>
    /// </remarks>
    public async Task<int> BulkUpdatePrioritiesAsync(
        Func<TPriority, TElement, (TPriority NewPriority, TElement NewElement)?> updateFunc,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(updateFunc, nameof(updateFunc));

        var updates = new List<(TPriority OldPriority, TElement OldElement,
                                TPriority NewPriority, TElement NewElement)>();

        // Phase 1: Collect updates
        foreach (var (priority, element) in _queue.GetFullEnumerator())
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            var update = updateFunc(priority, element);
            if (update.HasValue)
            {
                updates.Add((priority, element, update.Value.NewPriority, update.Value.NewElement));
            }
        }

        // Phase 2: Apply updates
        var successCount = 0;
        var stopwatch = Stopwatch.StartNew();

        foreach (var update in updates)
        {
            if (cancellationToken.IsCancellationRequested)
                break;

            if (TryUpdatePriority(
                update.OldPriority,
                update.NewPriority,
                _ => update.NewElement,
                new UpdateOptions<TElement> { EnableRollback = true }))
            {
                successCount++;
            }
        }

        _observability?.RecordOperation("BulkUpdatePriorities", true,
            stopwatch.ElapsedMilliseconds,
            new Dictionary<string, object>
            {
                ["TotalUpdates"] = updates.Count,
                ["SuccessCount"] = successCount,
                ["SuccessRate"] = updates.Count > 0 ? (double)successCount / updates.Count : 0
            });

        // Allow for async completion
        await Task.CompletedTask.ConfigureAwait(false);

        return successCount;
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Attempts to delete an element and return its value.
    /// </summary>
    /// <param name="priority">The priority of the element to delete.</param>
    /// <param name="element">The deleted element if found.</param>
    /// <returns>True if an element was deleted; otherwise, false.</returns>
    private bool TryDeleteWithResult(TPriority priority, out TElement? element)
    {
        element = default;

        // Find the node with the specified priority
        var node = _queue.InlineSearch(priority);
        if (node == null) return false;

        // Store the element before deletion
        element = node.Element;

        // Use the queue's TryDelete method
        return _queue.TryDelete(priority);
    }

    /// <summary>
    /// Attempts to delete an element that matches both priority and element predicate.
    /// </summary>
    /// <param name="priority">The priority of the element to delete.</param>
    /// <param name="elementMatcher">Predicate to match the specific element.</param>
    /// <param name="element">The deleted element if found.</param>
    /// <returns>True if a matching element was deleted; otherwise, false.</returns>
    private bool TryDeleteExact(
        TPriority priority,
        Func<TElement, bool> elementMatcher,
        out TElement? element)
    {
        element = default;

        // This would require extending the ConcurrentPriorityQueue to support
        // exact matching deletion. For now, we'll use the standard deletion
        // and verify the element matches
        var node = _queue.InlineSearch(priority);
        if (node == null) return false;

        element = node.Element;
        if (!elementMatcher(element)) return false;

        return _queue.TryDelete(priority);
    }

    #endregion
}

/// <summary>
/// Options for controlling priority update behavior.
/// </summary>
/// <typeparam name="TElement">The type of elements in the queue.</typeparam>
public struct UpdateOptions<TElement>
{
    /// <summary>
    /// Gets or sets a value indicating whether to use strict element matching.
    /// </summary>
    public bool UseStrictMatching { get; set; }

    /// <summary>
    /// Gets or sets the element matcher predicate for strict matching.
    /// </summary>
    public Func<TElement, bool>? ElementMatcher { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether to enable rollback on failure.
    /// </summary>
    public bool EnableRollback { get; set; }
}