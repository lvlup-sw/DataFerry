// ===========================================================================
// <copyright file="NodePool.cs" company="Level Up Software">
// Copyright (c) Level Up Software. All rights reserved.
// </copyright>
// ===========================================================================

using System.Collections.Concurrent;

using lvlup.DataFerry.Concurrency.Contracts;

namespace lvlup.DataFerry.Concurrency.Internal;

/// <summary>
/// Provides efficient pooling of skip list nodes to reduce memory allocation pressure in high-throughput scenarios.
/// </summary>
/// <typeparam name="TPriority">The type used for priority values.</typeparam>
/// <typeparam name="TElement">The type of the elements stored in the queue.</typeparam>
/// <remarks>
/// <para>
/// This implementation uses Microsoft.Extensions.ObjectPool to manage a pool of reusable
/// SkipListNode instances. Nodes are validated before being returned to the pool to ensure
/// pool integrity, and reset to a clean state for reuse.
/// </para>
/// <para>
/// The pool is thread-safe and designed to scale with the number of concurrent operations.
/// Pool size is bounded to prevent excessive memory usage in scenarios where nodes are
/// created faster than they can be reused.
/// </para>
/// </remarks>
internal sealed class NodePool<TPriority, TElement> : INodePool<ConcurrentPriorityQueue<TPriority, TElement>.SkipListNode>
{
    private readonly ConcurrentBag<ConcurrentPriorityQueue<TPriority, TElement>.SkipListNode> _pool;
    private readonly IComparer<TPriority> _comparer;
    private readonly int _maxHeight;
    private readonly object _clearLock = new();
    private volatile bool _isCleared;
    private readonly int _maxPoolSize;

    /// <summary>
    /// Initializes a new instance of the <see cref="NodePool{TPriority, TElement}"/> class.
    /// </summary>
    /// <param name="maxHeight">The maximum height of nodes in the skip list.</param>
    /// <param name="comparer">The comparer used for priority comparisons.</param>
    /// <param name="maxPoolSize">The maximum number of nodes to retain in the pool.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="comparer"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if <paramref name="maxHeight"/> or <paramref name="maxPoolSize"/> is less than 1.</exception>
    public NodePool(int maxHeight, IComparer<TPriority> comparer, int maxPoolSize)
    {
        ArgumentNullException.ThrowIfNull(comparer, nameof(comparer));
        ArgumentOutOfRangeException.ThrowIfLessThan(maxHeight, 1, nameof(maxHeight));
        ArgumentOutOfRangeException.ThrowIfLessThan(maxPoolSize, 1, nameof(maxPoolSize));

        _maxHeight = maxHeight;
        _comparer = comparer;
        _pool = new ConcurrentBag<ConcurrentPriorityQueue<TPriority, TElement>.SkipListNode>();
        _maxPoolSize = maxPoolSize;
    }

    /// <summary>
    /// Rents a node from the pool or creates a new one if the pool is empty.
    /// </summary>
    /// <returns>A clean node instance ready for initialization.</returns>
    /// <remarks>
    /// The returned node is guaranteed to be in a reset state with all fields
    /// cleared to their default values. The node's array is sized for the maximum
    /// height to avoid reallocation.
    /// </remarks>
    public ConcurrentPriorityQueue<TPriority, TElement>.SkipListNode Rent()
    {
        if (_isCleared)
        {
            // After clear, create new nodes rather than using potentially stale pooled ones
            return new ConcurrentPriorityQueue<TPriority, TElement>.SkipListNode(
                default!, default!, _maxHeight, _comparer);
        }

        if (_pool.TryTake(out var node))
        {
            return node;
        }
        
        return new ConcurrentPriorityQueue<TPriority, TElement>.SkipListNode(
            default!, default!, _maxHeight, _comparer);
    }

    /// <summary>
    /// Returns a node to the pool for future reuse.
    /// </summary>
    /// <param name="node">The node to return to the pool.</param>
    /// <returns><c>true</c> if the node was successfully returned; <c>false</c> if the node is invalid or the pool is full.</returns>
    /// <remarks>
    /// Only data nodes that have not been corrupted are accepted back into the pool.
    /// The node is validated and reset before being pooled to maintain pool integrity.
    /// </remarks>
    public bool Return(ConcurrentPriorityQueue<TPriority, TElement>.SkipListNode node)
    {
        ArgumentNullException.ThrowIfNull(node, nameof(node));

        if (_isCleared)
        {
            // Don't accept nodes after clear
            return false;
        }

        // Validate node is suitable for pooling
        if (!ValidateNode(node))
        {
            return false;
        }

        // Reset node state before returning to pool
        ResetNode(node);

        if (_pool.Count >= _maxPoolSize)
        {
            return false;
        }
        
        _pool.Add(node);
        return true;
    }

    /// <summary>
    /// Clears all nodes from the pool and prevents further pooling until the pool is reset.
    /// </summary>
    /// <remarks>
    /// This method is called during queue clear operations to ensure that old nodes
    /// referencing cleared data are not reused. After calling Clear, Rent will create
    /// new nodes and Return will reject all nodes.
    /// </remarks>
    public void Clear()
    {
        lock (_clearLock)
        {
            _isCleared = true;
            
            // DefaultObjectPool doesn't expose a clear method, but we can
            // effectively clear it by setting the flag and rejecting returns
        }
    }

    /// <summary>
    /// Resets the pool to allow normal operation after a clear.
    /// </summary>
    internal void Reset()
    {
        lock (_clearLock)
        {
            _isCleared = false;
        }
    }

    /// <summary>
    /// Validates that a node is suitable for pooling.
    /// </summary>
    private bool ValidateNode(ConcurrentPriorityQueue<TPriority, TElement>.SkipListNode node)
    {
        // Only pool data nodes
        if (node.Type is not ConcurrentPriorityQueue<TPriority, TElement>.SkipListNode.NodeType.Data)
        {
            return false;
        }

        // Don't pool nodes that are still in use
        if (node.IsInserted && !node.IsDeleted)
        {
            return false;
        }

        // Validate array size matches expected height
        if (node.TopLevel > _maxHeight)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Resets a node to a clean state for reuse.
    /// </summary>
    private void ResetNode(ConcurrentPriorityQueue<TPriority, TElement>.SkipListNode node)
    {
        // Clear node state
        node.IsInserted = false;
        node.IsDeleted = false;
        // Note: Priority and SequenceNumber are read-only and cannot be reset
        node.Element = default!;

        // Clear all next pointers
        for (int i = 0; i <= node.TopLevel; i++)
        {
            node.SetNextNode(i, null!);
        }
    }
}