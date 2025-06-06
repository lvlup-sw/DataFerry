// ===========================================================================
// <copyright file="SnapshotEnumerator.cs" company="Level Up Software">
// Copyright (c) Level Up Software. All rights reserved.
// </copyright>
// ===========================================================================

using System.Collections;
using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace lvlup.DataFerry.Concurrency.Enumerators;

/// <summary>
/// Provides an enumerator that iterates through a snapshot of priority-element pairs from the concurrent priority queue.
/// </summary>
/// <typeparam name="TPriority">The type used for priority values.</typeparam>
/// <typeparam name="TElement">The type of the elements stored in the queue.</typeparam>
/// <remarks>
/// <para>
/// This enumerator pre-materializes a snapshot of the queue state at construction time,
/// providing a stable view that is immune to concurrent modifications. This is useful
/// when a consistent view of the queue is needed for operations like serialization or
/// complex queries.
/// </para>
/// <para>
/// The snapshot approach trades memory for consistency - all valid elements are copied
/// into an immutable list at construction time. This makes enumeration faster and
/// completely thread-safe, but requires O(n) additional memory.
/// </para>
/// <para>
/// The snapshot reflects the queue state at a specific point in time and will not
/// reflect any subsequent modifications to the queue.
/// </para>
/// </remarks>
public sealed class SnapshotEnumerator<TPriority, TElement> : 
    IEnumerator<(TPriority Priority, TElement Element)>,
    IEnumerable<(TPriority Priority, TElement Element)>
{
    #region Fields

    /// <summary>
    /// The immutable snapshot of queue items.
    /// </summary>
    private readonly ImmutableList<(TPriority Priority, TElement Element)> _snapshot;

    /// <summary>
    /// The enumerator for the snapshot list.
    /// </summary>
    private ImmutableList<(TPriority Priority, TElement Element)>.Enumerator _enumerator;

    /// <summary>
    /// Indicates whether the enumerator has been initialized.
    /// </summary>
    private bool _enumeratorInitialized;

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="SnapshotEnumerator{TPriority, TElement}"/> class
    /// with a pre-captured snapshot.
    /// </summary>
    /// <param name="snapshot">The pre-captured snapshot of queue items.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="snapshot"/> is null.</exception>
    public SnapshotEnumerator(ImmutableList<(TPriority Priority, TElement Element)> snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot, nameof(snapshot));
        _snapshot = snapshot;
        _enumeratorInitialized = false;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SnapshotEnumerator{TPriority, TElement}"/> class
    /// by capturing a snapshot from an enumerable source.
    /// </summary>
    /// <param name="source">The source enumerable to capture.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="source"/> is null.</exception>
    public SnapshotEnumerator(IEnumerable<(TPriority Priority, TElement Element)> source)
    {
        ArgumentNullException.ThrowIfNull(source, nameof(source));
        _snapshot = source.ToImmutableList();
        _enumeratorInitialized = false;
    }

    #endregion

    #region Properties

    /// <summary>
    /// Gets the number of items in the snapshot.
    /// </summary>
    public int Count => _snapshot.Count;

    /// <summary>
    /// Gets a value indicating whether the snapshot is empty.
    /// </summary>
    public bool IsEmpty => _snapshot.IsEmpty;

    #endregion

    #region IEnumerator Implementation

    /// <summary>
    /// Gets the element in the collection at the current position of the enumerator.
    /// </summary>
    public (TPriority Priority, TElement Element) Current
    {
        get
        {
            EnsureEnumeratorInitialized();
            return _enumerator.Current;
        }
    }

    /// <summary>
    /// Gets the element in the collection at the current position of the enumerator.
    /// </summary>
    object IEnumerator.Current => Current;

    /// <summary>
    /// Advances the enumerator to the next element of the collection.
    /// </summary>
    /// <returns><c>true</c> if the enumerator was successfully advanced to the next element; <c>false</c> if the enumerator has passed the end of the collection.</returns>
    public bool MoveNext()
    {
        EnsureEnumeratorInitialized();
        return _enumerator.MoveNext();
    }

    /// <summary>
    /// Sets the enumerator to its initial position, which is before the first element in the collection.
    /// </summary>
    public void Reset()
    {
        _enumerator = _snapshot.GetEnumerator();
        _enumeratorInitialized = true;
    }

    /// <summary>
    /// Performs application-defined tasks associated with freeing, releasing, or resetting unmanaged resources.
    /// </summary>
    public void Dispose()
    {
        if (_enumeratorInitialized)
        {
            _enumerator.Dispose();
        }
    }

    #endregion

    #region IEnumerable Implementation

    /// <summary>
    /// Returns an enumerator that iterates through the collection.
    /// </summary>
    /// <returns>An enumerator that can be used to iterate through the collection.</returns>
    public IEnumerator<(TPriority Priority, TElement Element)> GetEnumerator()
    {
        return _snapshot.GetEnumerator();
    }

    /// <summary>
    /// Returns an enumerator that iterates through a collection.
    /// </summary>
    /// <returns>An <see cref="IEnumerator"/> object that can be used to iterate through the collection.</returns>
    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Gets the item at the specified index in the snapshot.
    /// </summary>
    /// <param name="index">The zero-based index of the item to get.</param>
    /// <returns>The item at the specified index.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if <paramref name="index"/> is out of range.</exception>
    public (TPriority Priority, TElement Element) this[int index] => _snapshot[index];

    /// <summary>
    /// Creates a new snapshot enumerator containing only items that match the specified predicate.
    /// </summary>
    /// <param name="predicate">The predicate to test each item.</param>
    /// <returns>A new snapshot enumerator containing only matching items.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="predicate"/> is null.</exception>
    public SnapshotEnumerator<TPriority, TElement> Where(Func<(TPriority Priority, TElement Element), bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate, nameof(predicate));
        return new SnapshotEnumerator<TPriority, TElement>(_snapshot.Where(predicate).ToImmutableList());
    }

    /// <summary>
    /// Creates a new snapshot enumerator containing at most the specified number of items.
    /// </summary>
    /// <param name="count">The maximum number of items to include.</param>
    /// <returns>A new snapshot enumerator containing at most <paramref name="count"/> items.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if <paramref name="count"/> is negative.</exception>
    public SnapshotEnumerator<TPriority, TElement> Take(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count, nameof(count));
        return new SnapshotEnumerator<TPriority, TElement>(_snapshot.Take(count).ToImmutableList());
    }

    /// <summary>
    /// Creates a new snapshot enumerator with items sorted by the specified key selector.
    /// </summary>
    /// <typeparam name="TKey">The type of the key returned by <paramref name="keySelector"/>.</typeparam>
    /// <param name="keySelector">A function to extract a key from each item.</param>
    /// <returns>A new snapshot enumerator with sorted items.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="keySelector"/> is null.</exception>
    public SnapshotEnumerator<TPriority, TElement> OrderBy<TKey>(Func<(TPriority Priority, TElement Element), TKey> keySelector)
    {
        ArgumentNullException.ThrowIfNull(keySelector, nameof(keySelector));
        return new SnapshotEnumerator<TPriority, TElement>(_snapshot.OrderBy(keySelector).ToImmutableList());
    }

    /// <summary>
    /// Converts the snapshot to an immutable list.
    /// </summary>
    /// <returns>An immutable list containing all items in the snapshot.</returns>
    public ImmutableList<(TPriority Priority, TElement Element)> ToImmutableList()
    {
        return _snapshot;
    }

    /// <summary>
    /// Converts the snapshot to a regular list.
    /// </summary>
    /// <returns>A list containing all items in the snapshot.</returns>
    public List<(TPriority Priority, TElement Element)> ToList()
    {
        return _snapshot.ToList();
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Ensures the enumerator is initialized before use.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EnsureEnumeratorInitialized()
    {
        if (!_enumeratorInitialized)
        {
            Reset();
        }
    }

    #endregion
}