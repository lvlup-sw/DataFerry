// ===========================================================================
// <copyright file="FullEnumerator.cs" company="Level Up Software">
// Copyright (c) Level Up Software. All rights reserved.
// </copyright>
// ===========================================================================

using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

using lvlup.DataFerry.Concurrency.Internal;

namespace lvlup.DataFerry.Concurrency.Enumerators;

/// <summary>
/// Provides a full enumerator that iterates through all priority-element pairs in the concurrent priority queue.
/// </summary>
/// <typeparam name="TPriority">The type used for priority values.</typeparam>
/// <typeparam name="TElement">The type of the elements stored in the queue.</typeparam>
/// <remarks>
/// <para>
/// This enumerator provides both synchronous and asynchronous enumeration capabilities,
/// traversing the queue in priority order while handling concurrent modifications gracefully.
/// </para>
/// <para>
/// The enumerator uses lock-free reads and provides "read committed" semantics, meaning it
/// only returns nodes that are fully inserted and not logically deleted at the time of reading.
/// </para>
/// <para>
/// If the queue is cleared during enumeration, an <see cref="InvalidOperationException"/> is thrown
/// to maintain consistency with standard collection modification behavior.
/// </para>
/// </remarks>
internal sealed class FullEnumerator<TPriority, TElement> : 
    IEnumerator<(TPriority Priority, TElement Element)>, 
    IAsyncEnumerator<(TPriority Priority, TElement Element)>
{
    #region Fields

    /// <summary>
    /// The head node of the skip list at the time the enumerator was created.
    /// </summary>
    private readonly ConcurrentPriorityQueue<TPriority, TElement>.SkipListNode _head;

    /// <summary>
    /// The clear version at the time the enumerator was created.
    /// </summary>
    private readonly int _clearVersion;

    /// <summary>
    /// Function to get the current clear version for detecting concurrent clears.
    /// </summary>
    private readonly Func<int> _getCurrentVersion;

    /// <summary>
    /// The current node being enumerated.
    /// </summary>
    private ConcurrentPriorityQueue<TPriority, TElement>.SkipListNode? _current;

    /// <summary>
    /// The current value returned by the enumerator.
    /// </summary>
    private (TPriority Priority, TElement Element) _currentValue;

    /// <summary>
    /// Counter for yielding control in async enumeration.
    /// </summary>
    private int _asyncYieldCounter;

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="FullEnumerator{TPriority, TElement}"/> class.
    /// </summary>
    /// <param name="head">The head node of the skip list.</param>
    /// <param name="clearVersion">The clear version at enumeration start.</param>
    /// <param name="getCurrentVersion">Function to get the current clear version.</param>
    /// <exception cref="ArgumentNullException">Thrown if required parameters are null.</exception>
    public FullEnumerator(ConcurrentPriorityQueue<TPriority, TElement>.SkipListNode head, int clearVersion, Func<int> getCurrentVersion)
    {
        ArgumentNullException.ThrowIfNull(head, nameof(head));
        ArgumentNullException.ThrowIfNull(getCurrentVersion, nameof(getCurrentVersion));

        _head = head;
        _clearVersion = clearVersion;
        _getCurrentVersion = getCurrentVersion;
        _current = null;
        _currentValue = default;
        _asyncYieldCounter = 0;
    }

    #endregion

    #region IEnumerator Implementation

    /// <summary>
    /// Gets the element in the collection at the current position of the enumerator.
    /// </summary>
    public (TPriority Priority, TElement Element) Current => _currentValue;

    /// <summary>
    /// Gets the element in the collection at the current position of the enumerator.
    /// </summary>
    object IEnumerator.Current => Current;

    /// <summary>
    /// Advances the enumerator to the next element of the collection.
    /// </summary>
    /// <returns><c>true</c> if the enumerator was successfully advanced to the next element; <c>false</c> if the enumerator has passed the end of the collection.</returns>
    /// <exception cref="InvalidOperationException">Thrown if the queue was cleared during enumeration.</exception>
    public bool MoveNext()
    {
        ThrowIfQueueCleared();
        
        _current = _current?.GetNextNode(0) ?? _head.GetNextNode(0);
        
        while (_current.Type is not ConcurrentPriorityQueue<TPriority, TElement>.SkipListNode.NodeType.Tail)
        {
            if (!NodeIsInvalidOrDeleted(_current))
            {
                _currentValue = (_current.Priority, _current.Element);
                return true;
            }
            _current = _current.GetNextNode(0);
        }
        
        return false;
    }

    /// <summary>
    /// Sets the enumerator to its initial position, which is before the first element in the collection.
    /// </summary>
    public void Reset()
    {
        _current = null;
        _currentValue = default;
        _asyncYieldCounter = 0;
    }

    /// <summary>
    /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
    /// </summary>
    public void Dispose()
    {
        // No unmanaged resources to dispose
    }

    #endregion

    #region IAsyncEnumerator Implementation

    /// <summary>
    /// Advances the enumerator asynchronously to the next element of the collection.
    /// </summary>
    /// <returns>A <see cref="ValueTask{Boolean}"/> that represents the asynchronous operation. The task result contains <c>true</c> if the enumerator was successfully advanced to the next element; <c>false</c> if the enumerator has passed the end of the collection.</returns>
    public async ValueTask<bool> MoveNextAsync()
    {
        // Yield periodically for better async behavior
        if (++_asyncYieldCounter % 100 == 0)
        {
            await Task.Yield();
        }
            
        return MoveNext();
    }

    /// <summary>
    /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources asynchronously.
    /// </summary>
    /// <returns>A task that represents the asynchronous dispose operation.</returns>
    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Checks if the queue has been cleared since enumeration started.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if the queue was cleared.</exception>
    private void ThrowIfQueueCleared()
    {
        if (_clearVersion != _getCurrentVersion())
        {
            throw new InvalidOperationException("Queue was cleared during enumeration");
        }
    }

    /// <summary>
    /// Determines if a node is invalid or deleted and should be skipped during enumeration.
    /// </summary>
    /// <param name="node">The node to check.</param>
    /// <returns><c>true</c> if the node should be skipped; otherwise, <c>false</c>.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool NodeIsInvalidOrDeleted([NotNullWhen(false)] ConcurrentPriorityQueue<TPriority, TElement>.SkipListNode? node)
    {
        return node is null || 
               node.Type is not ConcurrentPriorityQueue<TPriority, TElement>.SkipListNode.NodeType.Data ||
               !node.IsInserted || 
               node.IsDeleted;
    }

    #endregion
}