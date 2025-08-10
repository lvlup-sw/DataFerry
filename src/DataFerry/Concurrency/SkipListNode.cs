// ===========================================================================
// <copyright file="SkipListNode.cs" company="Level Up Software">
// Copyright (c) Level Up Software. All rights reserved.
// </copyright>
// ===========================================================================

using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace lvlup.DataFerry.Concurrency;

/// <summary>
/// Represents a node in the SkipList.
/// </summary>
/// <typeparam name="TPriority">The type used for priority values.</typeparam>
/// <typeparam name="TElement">The type of the elements stored in the node.</typeparam>
[SuppressMessage("ReSharper", "StaticMemberInGenericType", Justification = "Accessed by reference.")]
internal sealed class SkipListNode<TPriority, TElement>
{
    private readonly Lock _nodeLock = new();
    private SkipListNode<TPriority, TElement>[] _nextNodeArray;
    private IComparer<TPriority> _priorityComparer;
    private static long s_sequenceGenerator;

    // Volatile fields have 'release' and 'acquire' semantics
    // which act as one-way memory barriers; combining these properties
    // with locks provides sufficient memory ordering guarantees
    private volatile bool _isInserted;
    private volatile bool _isDeleted;

    /// <summary>
    /// Initializes a new instance of the <see cref="SkipListNode{TPriority,TElement}"/> class for Head/Tail nodes.
    /// </summary>
    /// <param name="nodeType">The type of the node.</param>
    /// <param name="height">The height (level) of the node.</param>
    public SkipListNode(NodeType nodeType, int height)
    {
        Priority = default!;
        Element = default!;
        Type = nodeType;
        _nextNodeArray = new SkipListNode<TPriority, TElement>[height + 1];
        _priorityComparer = Comparer<TPriority>.Default;
        SequenceNumber = nodeType switch
        {
            NodeType.Head => long.MinValue,
            NodeType.Tail => long.MaxValue,
            _ => -1
        };
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SkipListNode{TPriority,TElement}"/> class for Data nodes.
    /// </summary>
    /// <param name="priority">The priority associated with the node.</param>
    /// <param name="element">The element associated with the node.</param>
    /// <param name="height">The height (level) of the node.</param>
    /// <param name="comparer">The comparer to use for priorities.</param>
    public SkipListNode(TPriority priority, TElement element, int height, IComparer<TPriority>? comparer)
    {
        Priority = priority;
        Element = element;
        Type = NodeType.Data;
        _priorityComparer = comparer ?? Comparer<TPriority>.Default;
        _nextNodeArray = new SkipListNode<TPriority, TElement>[height + 1];
        SequenceNumber = Interlocked.Increment(ref s_sequenceGenerator);
    }

    /// <summary>
    /// Defines the types of nodes in the SkipList.
    /// </summary>
    public enum NodeType : byte
    {
        /// <summary>
        /// Represents the head node of the SkipList.
        /// </summary>
        Head = 0,

        /// <summary>
        /// Represents the tail node of the SkipList.
        /// </summary>
        Tail = 1,

        /// <summary>
        /// Represents a regular data node in the SkipList.
        /// </summary>
        Data = 2
    }

    /// <summary>
    /// Gets the priority associated with the node.
    /// </summary>
    public TPriority Priority { get; private set; }

    /// <summary>
    /// Gets or sets the element associated with the node.
    /// </summary>
    public TElement Element { get; set; }

    /// <summary>
    /// Reinitializes a pooled node instance with new values.
    /// </summary>
    /// <param name="priority">The priority for the node.</param>
    /// <param name="element">The element for the node.</param>
    /// <param name="height">The height (level) of the node.</param>
    /// <param name="comparer">The comparer to use for priorities.</param>
    internal void Reinitialize(TPriority priority, TElement element, int height, IComparer<TPriority>? comparer)
    {
        // Only allow reinitialization of data nodes
        if (Type is not NodeType.Data) throw new InvalidOperationException("Cannot reinitialize non-data nodes.");

        // Update fields that need to change
        Priority = priority;
        Element = element;
        _priorityComparer = comparer ?? Comparer<TPriority>.Default;
        SequenceNumber = Interlocked.Increment(ref s_sequenceGenerator);

        // Resize array if needed
        if (_nextNodeArray.Length != height + 1)
        {
            Array.Resize(ref _nextNodeArray, height + 1);
        }

        // Clear any existing pointers beyond the current height
        for (int i = 0; i < _nextNodeArray.Length; i++)
        {
            _nextNodeArray[i] = null!;
        }

        // Reset state flags
        _isInserted = false;
        _isDeleted = false;
    }

    /// <summary>
    /// Unique identifier for this node instance. Readonly after construction.
    /// </summary>
    public long SequenceNumber { get; private set; }

    /// <summary>
    /// Gets or sets the type of the node.
    /// </summary>
    public NodeType Type { get; set; }

    /// <summary>
    /// Gets or sets an element indicating whether the node has been logically inserted.
    /// </summary>
    public bool IsInserted { get => _isInserted; set => _isInserted = value; }

    /// <summary>
    /// Gets or sets an element indicating whether the node has been logically deleted.
    /// </summary>
    public bool IsDeleted { get => _isDeleted; set => _isDeleted = value; }

    /// <summary>
    /// Gets the top level (highest index) of the node in the SkipList.
    /// </summary>
    public int TopLevel => _nextNodeArray.Length - 1;

    /// <summary>
    /// Gets the next node at the specified height (level).
    /// </summary>
    /// <param name="height">The height (level) at which to get the next node.</param>
    /// <returns>The next node at the specified height.</returns>
    public SkipListNode<TPriority, TElement> GetNextNode(int height) => _nextNodeArray[height];

    /// <summary>
    /// Sets the next node at the specified height (level).
    /// </summary>
    /// <param name="height">The height (level) at which to set the next node.</param>
    /// <param name="next">The next node to set.</param>
    public void SetNextNode(int height, SkipListNode<TPriority, TElement> next) => _nextNodeArray[height] = next;

    /// <summary>
    /// Attempts to acquire the lock associated with the node without blocking.
    /// </summary>
    /// <returns>true if the lock was acquired; otherwise, false.</returns>
    public bool TryEnter()
    {
        // Directly use the TryEnter method of System.Threading.Lock
        return _nodeLock.TryEnter();
    }

    /// <summary>
    /// Acquires the lock associated with the node.
    /// </summary>
    public void Lock() => _nodeLock.Enter();

    /// <summary>
    /// Releases the lock associated with the node.
    /// </summary>
    public void Unlock() => _nodeLock.Exit();

    /// <summary>
    /// Compares this node to another node based first on Priority, then SequenceNumber.
    /// Required for correct ordering when priorities are duplicated.
    /// Handles Head/Tail nodes.
    /// </summary>
    /// <param name="other">The SkipListNode to compare against this instance.</param>
    /// <returns>
    /// <c>-1</c> if this node is less than <paramref name="other"/>.
    /// <c>0</c> if this node is equal to other (should only happen if comparing node to itself, as SequenceNumbers are unique).
    /// <c>1</c> if this node is greater than <paramref name="other"/>.
    /// </returns>
    public int CompareTo(SkipListNode<TPriority, TElement> other)
    {
        // Handle identity comparison
        if (ReferenceEquals(this, other)) return 0;

        // Handle Head/Tail comparisons explicitly
        switch (this.Type)
        {
            case NodeType.Head: return -1;
            case NodeType.Tail: return 1;
            case NodeType.Data:
                if (other.Type is NodeType.Head) return 1;
                if (other.Type is NodeType.Tail) return -1;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(other), "Unknown node type.");
        }

        // Both are Data nodes: Compare priorities, then sequence number
        int priorityComparison = _priorityComparer.Compare(this.Priority, other.Priority);

        return priorityComparison is not 0
            ? priorityComparison
            : this.SequenceNumber.CompareTo(other.SequenceNumber);
    }

    /// <summary>
    /// Compares this node's priority to a given priority value.
    /// Does NOT use the sequence number. Useful for initial phase of search or priority-only checks.
    /// </summary>
    /// <param name="priority">The priority value to compare against this node's priority.</param>
    /// <returns>
    /// <c>-1</c> if this node's priority is less than <paramref name="priority"/>.
    /// <c>0</c> if the priorities are equal.
    /// <c>1</c> if this node's priority is greater than <paramref name="priority"/>.
    /// Head nodes are always considered less, Tail nodes are always considered greater.
    /// </returns>
    public int CompareToPriority(TPriority priority)
    {
        return this.Type switch
        {
            // Head is always less
            NodeType.Head => -1,

            // Tail is always greater
            NodeType.Tail => 1,

            // Compare priorities using the comparer for TPriority
            _ => _priorityComparer.Compare(this.Priority, priority)
        };
    }
}