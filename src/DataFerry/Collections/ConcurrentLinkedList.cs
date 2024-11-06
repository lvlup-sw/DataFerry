using lvlup.DataFerry.Collections.Abstractions;

namespace lvlup.DataFerry.Collections
{
    /// <summary>
    /// Represents a non-blocking doubly linked list.
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
    /// <typeparam name="T">The type of elements in the list.</typeparam>
    public class ConcurrentLinkedList<T> : IConcurrentLinkedList<T> where T : notnull
    {
        /// <summary>
        /// The head of the linked list.
        /// </summary>
        private static volatile Node<T> _head = InitializeHead();

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
            _comparer = comparer ?? Comparer<T>.Default;
            _count = 0;
        }

        /// <inheritdoc/>
        public bool TryInsert(T key)
        {
            Node<T> newNode = new(key, NodeState.INS);
            Enlist(newNode);

            bool result = HelpInsert(_head, newNode);

            if (Interlocked.CompareExchange(ref _head.State, NodeState.DAT, NodeState.INS) != NodeState.INS)
            {
                HelpRemove(_head, key);
                _head.State = NodeState.INV;
            }

            Interlocked.Increment(ref _count);
            return result;
        }

        /// <inheritdoc/>
        public bool TryRemove(T key)
        {
            Node<T> newNode = new(key, NodeState.REM);
            Enlist(newNode);

            bool result = HelpRemove(_head, key);
            _head.State = NodeState.INV;

            Interlocked.Decrement(ref _count);
            return result;
        }

        /// <inheritdoc/>
        public bool Contains(T key)
        {
            for (Node<T>? curr = _head; curr is not null; curr = curr.Next)
            {
                if (_comparer.Compare(curr.Key, key) == 0 && curr.State != NodeState.INV)
                {
                    return curr.State == NodeState.INS || curr.State == NodeState.DAT;
                }
            }

            return false;
        }

        /// <inheritdoc/>
        public Node<T>? Find(T value)
        {
            for (Node<T>? curr = _head.Next; curr != null; curr = curr.Next)
            {
                if ((curr.State == NodeState.DAT || curr.State == NodeState.INS) 
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
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, array.Length);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(_count, array.Length - index, 
                "The number of elements in the list is greater than the available space from index to the end of the destination array.");

            int i = index;
            Node<T>? curr = _head.Next;
            while (curr is not null)
            {
                if (curr.State == NodeState.DAT || curr.State == NodeState.INS)
                {
                    array[i++] = curr.Key;
                }

                curr = curr.Next;
            }
        }

        /// <inheritdoc/>
        public int Count => _count;

        /// <summary>
        /// Initialize a default Node state for the head.
        /// </summary>
        private static Node<T> InitializeHead() => new(default!, NodeState.INV);

        /// <summary>
        /// Enlists a new node into the linked list.
        /// </summary>
        /// <param name="node">The node to enlist.</param>
        /// <returns>true if the node was enlisted successfully; otherwise, false.</returns>
        private static bool Enlist(Node<T> node)
        {
            while (true)
            {
                var old = _head;
                node.Next = old;

                if (Interlocked.CompareExchange(ref _head, node, old) == old)
                {
                    return true;
                }
            }
        }

        /// <summary>
        /// Helper function to insert a node into the list.
        /// </summary>
        /// <param name="head">The head of the list.</param>
        /// <param name="newNode">The node to insert.</param>
        /// <returns>true if the node was inserted successfully; otherwise, false.</returns>
        private bool HelpInsert(Node<T> head, Node<T> newNode)
        {
            Node<T> pred = head;
            Node<T> curr = pred.Next;

            while (curr is not null)
            {
                if (curr.State == NodeState.INV)
                {   // State is Invalid
                    ReplaceNode(pred, curr);
                    curr = pred.Next;
                }
                else if (_comparer.Compare(curr.Key, newNode.Key) == 0)
                {   // Key match found
                    return curr.State switch
                    {
                        NodeState.REM => true,
                        NodeState.INS or NodeState.DAT => false,
                        _ => throw new InvalidOperationException("Unexpected node state")
                    };
                }
                else
                {   // Keys don't match, move to the next node
                    pred = curr;
                    curr = curr.Next;
                }
            }

            pred.Next = newNode;
            newNode.Prev = pred;
            return true;
        }

        /// <summary>
        /// Helper function to remove a node from the list.
        /// </summary>
        /// <param name="head">The head of the list.</param>
        /// <param name="key">The key of the node to remove.</param>
        /// <returns>true if the node was removed successfully; otherwise, false.</returns>
        private bool HelpRemove(Node<T> head, T key)
        {
            Node<T> pred = head;
            Node<T> curr = pred.Next;

            while (curr is not null)
            {
                if (curr.State == NodeState.INV)
                {   // State is Invalid
                    ReplaceNode(pred, curr);
                    curr = pred.Next;
                }
                else if (_comparer.Compare(curr.Key, key) == 0)
                {   // Key match found
                    return curr.State switch
                    {
                        NodeState.REM => false,
                        NodeState.INS => Interlocked.CompareExchange(ref curr.State, NodeState.REM, NodeState.INS) == NodeState.INS,
                        NodeState.DAT => SetStateToInv(curr),
                        _ => throw new InvalidOperationException("Unexpected node state")
                    };
                }
                else
                {   // Keys don't match, move to the next node
                    pred = curr;
                    curr = curr.Next;
                }
            }

            return false;
        }

        /// <summary>
        /// Replaces a node in the linked list.
        /// </summary>
        /// <param name="pred">The predecessor node.</param>
        /// <param name="curr">The node to replace.</param>
        private static void ReplaceNode(Node<T> pred, Node<T> curr)
        {
            Node<T> succ = curr.Next;
            pred.Next = succ;

            if (succ is not null)
            {
                succ.Prev = pred;
            }
        }

        /// <summary>
        /// Sets the state of a node to <see cref="NodeState.INV"/>.
        /// </summary>
        /// <param name="node">The node to update.</param>
        /// <returns>true.</returns>
        private static bool SetStateToInv(Node<T> node)
        {
            node.State = NodeState.INV;
            return true;
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
}
