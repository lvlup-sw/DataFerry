namespace lvlup.DataFerry.Orchestrators.Contracts;

/// <summary>
/// Represents a contract for a scheduler that handles the low-level work of queuing tasks onto threads.
/// </summary>
public interface ITaskOrchestrator
{
    /// <summary>
    /// Gets a value indicating whether this scheduler runs tasks in the background.
    /// </summary>
    bool IsBackground { get; }

    /// <summary>
    /// Gets the count of scheduled work items.
    /// </summary>
    long RunCount { get; }

    /// <summary>
    /// Queues the specified work to run.
    /// </summary>
    /// <param name="action">The work to execute.</param>
    void Run(Action action);
}