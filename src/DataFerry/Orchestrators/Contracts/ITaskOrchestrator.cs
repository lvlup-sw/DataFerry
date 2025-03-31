// ===========================================================================
// <copyright file="ITaskOrchestrator.cs" company="Level Up Software">
// Copyright (c) Level Up Software. All rights reserved.
// </copyright>
// ===========================================================================

namespace lvlup.DataFerry.Orchestrators.Contracts;

/// <summary>
/// Represents a contract for a background task scheduler that handles queuing and executing asynchronous actions.
/// </summary>
public interface ITaskOrchestrator : IAsyncDisposable
{
    /// <summary>
    /// Gets the approximate count of work items successfully queued via the Run method.
    /// This count increments when an item is successfully added to the queue.
    /// It does not reflect the number of items processed or currently pending.
    /// </summary>
    long QueuedCount { get; }

    /// <summary>
    /// Attempts to queue the specified work to run asynchronously in the background.
    /// Behavior when the queue is full depends on the orchestrator's configuration (e.g., wait or drop).
    /// </summary>
    /// <param name="action">The asynchronous work delegate to execute.</param>
    /// <exception cref="InvalidOperationException">Thrown if queuing fails and dropping is disallowed.</exception>
    /// <exception cref="ObjectDisposedException">Thrown if the orchestrator has been disposed.</exception>
    void Run(Func<Task> action);

    /// <summary>
    /// Asynchronously attempts to queue the specified work to run in the background.
    /// This method waits if the queue is full and dropping tasks is disallowed.
    /// </summary>
    /// <param name="action">The asynchronous work delegate to execute.</param>
    /// <param name="cancellationToken">A token to observe for cancellation requests.</param>
    /// <returns>A task representing the asynchronous queuing operation.</returns>
    /// <exception cref="OperationCanceledException">Thrown if the cancellation token is signaled.</exception>
    /// <exception cref="ObjectDisposedException">Thrown if the orchestrator has been disposed.</exception>
    ValueTask RunAsync(Func<Task> action, CancellationToken cancellationToken = default);

    /// <summary>
    /// Signals the orchestrator to stop accepting new work and attempts to wait for currently executing and queued tasks to complete.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token to signal interruption of the shutdown process.</param>
    /// <returns>A task that represents the asynchronous shutdown process.</returns>
    Task StopAsync(CancellationToken cancellationToken = default);
}