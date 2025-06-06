// ===========================================================================
// <copyright file="INodePool.cs" company="Level Up Software">
// Copyright (c) Level Up Software. All rights reserved.
// </copyright>
// ===========================================================================

namespace lvlup.DataFerry.Concurrency.Contracts;

/// <summary>
/// Provides object pooling functionality for reusable node instances to reduce memory allocation pressure.
/// </summary>
/// <typeparam name="TNode">The type of node to pool.</typeparam>
/// <remarks>
/// <para>
/// This interface defines a contract for node pooling to improve performance in high-throughput scenarios
/// by reusing node instances instead of allocating new ones. Implementations should ensure thread-safety
/// for concurrent access.
/// </para>
/// </remarks>
public interface INodePool<TNode>
{
    /// <summary>
    /// Rents a node from the pool or creates a new one if the pool is empty.
    /// </summary>
    /// <returns>A node instance ready for use.</returns>
    /// <remarks>
    /// The returned node should be in a clean state, ready for initialization.
    /// This method should be thread-safe and performant under contention.
    /// </remarks>
    TNode Rent();

    /// <summary>
    /// Returns a node to the pool for future reuse.
    /// </summary>
    /// <param name="node">The node to return to the pool.</param>
    /// <returns><c>true</c> if the node was successfully returned; <c>false</c> if the pool is full or the node is invalid.</returns>
    /// <remarks>
    /// Implementations should validate and reset the node before adding it back to the pool.
    /// Invalid or corrupted nodes should be rejected to maintain pool integrity.
    /// </remarks>
    bool Return(TNode node);

    /// <summary>
    /// Clears all nodes from the pool, releasing resources.
    /// </summary>
    /// <remarks>
    /// This method is typically called during queue cleanup or disposal.
    /// After clearing, the pool should be ready to accept new nodes.
    /// </remarks>
    void Clear();
}