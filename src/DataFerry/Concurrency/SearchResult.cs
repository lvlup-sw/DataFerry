// ===========================================================================
// <copyright file="SearchResult.cs" company="Level Up Software">
// Copyright (c) Level Up Software. All rights reserved.
// </copyright>
// ===========================================================================

using System.Buffers;

namespace lvlup.DataFerry.Concurrency;

/// <summary>
/// Represents the result of a structural search operation in the SkipList.
/// </summary>
/// <typeparam name="TPriority">The type used for priority values.</typeparam>
/// <typeparam name="TElement">The type of the elements stored in the queue.</typeparam>
internal sealed class SearchResult<TPriority, TElement> : IDisposable
{
    /// <summary>
    /// Represents the level element when a priority is not found in the SkipList.
    /// </summary>
    private const int NotFoundLevel = -1;

    private readonly ArrayPool<SkipListNode<TPriority, TElement>> _arrayPool;
    private readonly int _arraySize;
    private SkipListNode<TPriority, TElement>[]? _predecessorArray;
    private SkipListNode<TPriority, TElement>[]? _successorArray;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="SearchResult{TPriority, TElement}"/> class.
    /// </summary>
    /// <param name="levelFound">The highest level index at which the exact node was found (or <see cref="NotFoundLevel"/> if not found).</param>
    /// <param name="predecessorArray">An array of predecessor nodes at each level.</param>
    /// <param name="successorArray">An array of successor nodes at each level.</param>
    /// <param name="nodeFound">A reference to the exact node instance found, or null if not found.</param>
    /// <param name="arrayPool">The array pool used to rent the arrays.</param>
    /// <param name="arraySize">The size of the arrays.</param>
    public SearchResult(
        int levelFound,
        SkipListNode<TPriority, TElement>[] predecessorArray,
        SkipListNode<TPriority, TElement>[] successorArray,
        SkipListNode<TPriority, TElement>? nodeFound,
        ArrayPool<SkipListNode<TPriority, TElement>> arrayPool,
        int arraySize)
    {
        LevelFound = levelFound;
        _predecessorArray = predecessorArray;
        _successorArray = successorArray;
        NodeFound = nodeFound;
        _arrayPool = arrayPool;
        _arraySize = arraySize;
    }

    /// <summary>
    /// Gets the highest level index at which the exact node was found.
    /// </summary>
    public int LevelFound { get; }

    /// <summary>
    /// Gets a reference to the exact node instance found, or null if not found.
    /// </summary>
    public SkipListNode<TPriority, TElement>? NodeFound { get; }

    /// <summary>
    /// Gets an element indicating whether the priority was found in the SkipList.
    /// </summary>
    public bool IsFound => LevelFound != NotFoundLevel && NodeFound is not null;

    /// <summary>
    /// Gets the predecessor node at the specified level.
    /// </summary>
    /// <param name="level">The level at which to get the predecessor node.</param>
    /// <returns>The predecessor node at the specified level.</returns>
    /// <exception cref="ObjectDisposedException">Thrown if the object has been disposed.</exception>
    /// <exception cref="IndexOutOfRangeException">Thrown if level is invalid.</exception>
    public SkipListNode<TPriority, TElement> GetPredecessor(int level)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(_predecessorArray, nameof(_predecessorArray));
        return _predecessorArray[level];
    }

    /// <summary>
    /// Gets the successor node at the specified level.
    /// </summary>
    /// <param name="level">The level at which to get the successor node.</param>
    /// <returns>The successor node at the specified level.</returns>
    /// <exception cref="ObjectDisposedException">Thrown if the object has been disposed.</exception>
    /// <exception cref="IndexOutOfRangeException">Thrown if level is invalid.</exception>
    public SkipListNode<TPriority, TElement> GetSuccessor(int level)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(_successorArray, nameof(_successorArray));
        return _successorArray[level];
    }

    /// <summary>
    /// Gets the exact node instance that was found during the search.
    /// </summary>
    /// <returns>The node that was found.</returns>
    /// <exception cref="InvalidOperationException">Thrown if the node was not found (<see cref="IsFound"/> is false).</exception>
    /// <exception cref="ObjectDisposedException">Thrown if the object has been disposed.</exception>
    public SkipListNode<TPriority, TElement> GetNodeFound()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsFound || NodeFound is null)
        {
            throw new InvalidOperationException("Cannot get node found because the exact node instance was not found during the search.");
        }

        return NodeFound;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed) return;

        if (_predecessorArray is not null)
        {
            // Clear the array before returning to pool
            Array.Clear(_predecessorArray, 0, _arraySize);
            _arrayPool.Return(_predecessorArray);
            _predecessorArray = null;
        }

        if (_successorArray is not null)
        {
            // Clear the array before returning to pool
            Array.Clear(_successorArray, 0, _arraySize);
            _arrayPool.Return(_successorArray);
            _successorArray = null;
        }

        _disposed = true;
    }
}