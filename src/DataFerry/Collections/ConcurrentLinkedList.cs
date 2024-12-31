using lvlup.DataFerry.Collections.Contracts;

namespace lvlup.DataFerry.Collections;

/// <summary>
/// A non-blocking thread-safe doubly linked list.
/// </summary>
/// <typeparam name="T">The type of elements in the list.</typeparam>
/// <remarks>
/// <para>
/// This concurrent linked list is implemented using a lock-free algorithm based on the research paper 
/// "Practical Non-blocking Unordered Lists" by Kunlong Zhang, Yujiao Zhao, Yajun Yang, Yujie Liu, and Michael Spear.
/// </para>
/// <para>
/// This implementation offers:
/// </para>
/// <list type="bullet">
/// <item>Thread-safe operations for concurrent environments via compare-and-swap (CAS) instructions.</item>
/// <item>Support for custom key comparers.</item>
/// <item>Lock-free count.</item>
/// </list>
/// </remarks>
public class ConcurrentLinkedList<T> : IConcurrentLinkedList<T> where T : notnull
{
    /// <summary>
    /// The head of the linked list.
    /// </summary>
    private volatile Node<T> _head;

    /// <summary>
    /// The comparer used to compare keys.
    /// </summary>
    private readonly IComparer<T> _comparer;

    /// <summary>
    /// The number of nodes in the list.
    /// </summary>
    private int _count = 0;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConcurrentLinkedList{T}"/> class.
    /// </summary>
    /// <param name="comparer">The comparer to use for comparing keys.</param>
    public ConcurrentLinkedList(IComparer<T>? comparer = default)
    {
        _head = new(default!, NodeState.REM);
        _comparer = comparer ?? Comparer<T>.Default;
    }

    /// <inheritdoc/>
    public bool TryInsert(T key)
    {
        Node<T> newNode = new(key, NodeState.INS);
        Enlist(newNode);

        bool result = HelpInsert(newNode, key);

        // Attempt to transition node state from INS to DAT or INV
        // If CAS fails, another thread has modified the node, so retry
        // HelpRemove is called only if CAS succeeds and result is false
        if (Interlocked.CompareExchange(ref newNode.State, result ? NodeState.DAT : NodeState.INV, NodeState.INS) != NodeState.INS)
        {
            HelpRemove(newNode, key);
            newNode.State = NodeState.INV;
            Interlocked.Decrement(ref _count);
        }

        return result;
    }

    /// <inheritdoc/>
    public bool TryRemove(T key)
    {
        Node<T> newNode = new(key, NodeState.REM);
        Enlist(newNode);

        bool result = HelpRemove(newNode, key);
        newNode.State = NodeState.INV;

        return result;
    }

    /// <inheritdoc/>
    public bool Contains(T key)
    {
        for (Node<T>? curr = _head; curr is not null; curr = curr.Next)
        {
            if (_comparer.Compare(curr.Key, key) == 0 && curr.State != NodeState.INV)
            {
                return curr.State is NodeState.INS or NodeState.DAT;
            }
        }

        return false;
    }

    /// <inheritdoc/>
    public Node<T>? Find(T value)
    {
        for (Node<T>? curr = _head; curr is not null; curr = curr.Next)
        {
            if (curr.State is NodeState.DAT or NodeState.INS
                && _comparer.Compare(curr.Key, value) == 0)
            {
                return curr;
            }
        }

        return default;
    }

    /// <inheritdoc/>
    public void CopyTo(T[] array, int index)
    {
        ArgumentNullException.ThrowIfNull(array, nameof(array));
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(array.Length, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, array.Length);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(_count, array.Length - index, 
            "The number of elements in the list is greater than the available space from index to the end of the destination array.");

        int i = index;
        Node<T>? curr = _head;
        while (curr is not null)
        {
            if (curr.State is NodeState.DAT or NodeState.INS)
            {
                array[i++] = curr.Key;
            }

            curr = curr.Next;
        }
    }

    /// <inheritdoc/>
    public int Count => _count;

    /// <inheritdoc/>
    public void Clear()
    {
        if (_head is null) return;

        // Atomically detach the head and reset the tail
        Node<T> currentHead = Interlocked.Exchange(ref _head!, null);

        ClearNodePointers(currentHead);

        // Reset the count
        Interlocked.Exchange(ref _count, 0);
        return;

        // Inner function to clear Prev pointers
        static void ClearNodePointers(Node<T> node)
        {
            while (node is not null)
            {
                if (node.Prev is not null) 
                    _ = Interlocked.Exchange(ref node.Prev!, null);

                node = node.Next;
            }
        }
    }

    /// <summary>
    /// Enlists a new node into the linked list.
    /// </summary>
    /// <param name="newNode">The node to enlist.</param>
    private void Enlist(Node<T> newNode)
    {
        while (true)
        {
            var old = _head;
            newNode.Next = old;

            if (Interlocked.CompareExchange(ref _head, newNode, old) == old)
            {
                return;
            }
        }
    }

    /// <summary>
    /// Helper function to insert a node into the list.
    /// </summary>
    /// <param name="newNode">The node to insert.</param>
    /// <param name="key">The key of the node.</param>
    /// <returns>true if the node was inserted successfully; otherwise, false.</returns>
    private bool HelpInsert(Node<T> newNode, T key)
    {
        Node<T> pred = newNode;
        Node<T> curr = pred.Next;

        while (curr is not null)
        {
            if (curr.State == NodeState.INV)
            {   // State is Invalid
                ReplaceNode(pred, ref curr);
            }
            else if (_comparer.Compare(curr.Key, key) != 0)
            {   // Keys don't match, move to the next node
                pred = curr;
                curr = curr.Next;
            }
            else if (curr.State == NodeState.REM)
            {   // Curr is being removed
                return true;
            }
            else if (curr.State is NodeState.INS or NodeState.DAT)
            {   // Curr is being inserted or has been inserted
                return false;
            }
        }

        Interlocked.Increment(ref _count);
        return true;
    }

    /// <summary>
    /// Helper function to remove a node from the list.
    /// </summary>
    /// <param name="newNode">The node to remove.</param>
    /// <param name="key">The key of the node.</param>
    /// <returns>true if the node was removed successfully; otherwise, false.</returns>
    private bool HelpRemove(Node<T> newNode, T key)
    {
        Node<T> pred = newNode;
        Node<T> curr = pred.Next;

        while (curr is not null)
        {
            if (curr.State == NodeState.INV)
            {   // State is Invalid
                ReplaceNode(pred, ref curr);
            }
            else if (_comparer.Compare(curr.Key, key) != 0)
            {   // Keys don't match, move to the next node
                pred = curr;
                curr = curr.Next;
            }
            else if (curr.State == NodeState.REM)
            {   // Curr was successfully marked for removal
                return true;
            }
            else if (TryUpdateState(curr))
            {   // Validate the state and return
                if (curr.State == NodeState.DAT)
                {
                    curr.State = NodeState.INV;
                    Interlocked.Decrement(ref _count);
                }

                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Replaces a node in the linked list.
    /// </summary>
    /// <param name="pred">The predecessor node.</param>
    /// <param name="curr">The node to replace.</param>
    private static void ReplaceNode(Node<T> pred, ref Node<T> curr)
    {
        Node<T> succ = curr.Next;
        pred.Next = succ;

        // Null value is expected behavior
        Interlocked.Exchange(ref curr, succ);
    }

    /// <summary>
    /// Tries to update the state of the given node.
    /// </summary>
    /// <param name="curr">The node to update.</param>
    /// <returns>
    /// <c>true</c> if the state was successfully updated to REM (from INS) or is already DAT; otherwise, <c>false</c>.
    /// </returns>
    private bool TryUpdateState(Node<T> curr)
    {
        if (curr.State == NodeState.INS 
            && Interlocked.CompareExchange(ref curr.State, NodeState.REM, NodeState.INS) == NodeState.INS)
        {
            Interlocked.Decrement(ref _count);
            return true;
        }

        return curr.State == NodeState.DAT;
    }
}

/// <summary>
/// Represents a node in the concurrent linked list.
/// </summary>
/// <typeparam name="T">The type of the key stored in the node.</typeparam>
public class Node<T>
{
    /// <summary>
    /// The key stored in the node.
    /// </summary>
    public T Key;

    /// <summary>
    /// The ID of the thread that created the node.
    /// </summary>
    public int ThreadId;

    /// <summary>
    /// The next node in the list.
    /// </summary>
    public Node<T> Next;

    /// <summary>
    /// The previous node in the list.
    /// </summary>
    public Node<T> Prev;

    /// <summary>
    /// The state of the node.
    /// </summary>
    public NodeState State;

    /// <summary>
    /// Initializes a new instance of the <see cref="Node{T}"/> class.
    /// </summary>
    /// <param name="key">The key to store in the node.</param>
    /// <param name="state">The initial state of the node.</param>
    public Node(T key, NodeState state)
    {
        Key = key;
        State = state;
        Next = default!;
        Prev = default!;
        ThreadId = Environment.CurrentManagedThreadId;
    }
}

/// <summary>
/// Defines the possible states of a node in the concurrent linked list.
/// </summary>
public enum NodeState
{
    /// <summary>
    /// The node is being inserted.
    /// </summary>
    INS = 0,

    /// <summary>
    /// The node is being removed.
    /// </summary>
    REM = 1,

    /// <summary>
    /// The node contains valid data.
    /// </summary>
    DAT = 2,

    /// <summary>
    /// The node is invalid.
    /// </summary>
    INV = 3
}