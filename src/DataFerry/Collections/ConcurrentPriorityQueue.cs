using lvlup.DataFerry.Collections.Abstractions;
using lvlup.DataFerry.Orchestrators.Abstractions;

namespace lvlup.DataFerry.Collections
{
    /// <summary>
    /// A concurrent PriorityQueue implemented as a lock-based SkipList with relaxed semantics.
    /// </summary>
    /// <typeparam name="TPriority">The priority.</typeparam>
    /// <typeparam name="TElement">The element.</typeparam>
    /// <remarks>
    /// <para>
    /// A SkipList is a probabilistic data structure that provides efficient search, insertion, and deletion operations with an expected logarithmic time complexity. 
    /// Unlike balanced search trees (e.g., AVL trees, Red-Black trees), SkipLists achieve efficiency through probabilistic balancing, making them well-suited for concurrent implementations.
    /// </para>
    /// <para>
    /// This implementation offers:
    /// </para>
    /// <list type="bullet">
    /// <item>Expected O(log n) time complexity for `Contains`, `TryGet`, `TryAdd`, `Update`, and `TryRemove` operations.</item> 
    /// <item>Lock-free and wait-free `Contains` and `TryGet` operations.</item>
    /// <item>Lock-free priority enumerations.</item>
    /// </list>
    /// <para>
    /// <b>Implementation Details:</b>
    /// </para>
    /// <para>
    /// This implementation employs logical deletion and insertion to optimize performance and ensure thread safety. 
    /// Nodes are marked as deleted logically before being physically removed, and they are inserted level by level to maintain consistency.
    /// A background task on a dedicated thread processes the physical deletion of nodes.
    /// </para>
    /// <para>
    /// <b>Invariants:</b>
    /// </para>
    /// <para>
    /// The list at a lower level is always a subset of the list at a higher level. This invariant ensures that nodes are added from the bottom up and removed from the top down.
    /// </para>
    /// <para>
    /// <b>Locking:</b>
    /// </para>
    /// <para>
    /// Locks are acquired in a bottom-up manner to prevent deadlocks. The order of lock release is not critical.
    /// </para>
    /// </remarks>
    public class ConcurrentPriorityQueue<TPriority, TElement> : IConcurrentPriorityQueue<TPriority, TElement>
    {
        #region Global Variables

        /// <summary>
        /// Invalid level.
        /// </summary>
        internal const int InvalidLevel = -1;

        /// <summary>
        /// Bottom level.
        /// </summary>
        internal const int BottomLevel = 0;

        /// <summary>
        /// Default size limit
        /// </summary>
        internal const int DefaultMaxSize = 10000;

        /// <summary>
        /// Default number of levels
        /// </summary>
        internal const int DefaultNumberOfLevels = 32;

        /// <summary>
        /// Default promotion chance for each level. [0, 1).
        /// </summary>
        internal const double DefaultPromotionProbability = 0.5;

        /// <summary>
        /// The maximum number of elements allowed in the queue.
        /// </summary>
        private readonly int _maxSize;

        /// <summary>
        /// The threshold for updating the head node.
        /// </summary>
        /// <remarks>
        /// This should scale with the number of threads.
        /// Higher values without a certain ratio of concurrency will incur
        /// performance impacts as we encounter higher memory latency.
        /// </remarks>
        private readonly int _maxOffset;

        /// <summary>
        /// Number of levels in the skip list.
        /// </summary>
        private readonly int _numberOfLevels;

        /// <summary>
        /// Heighest level allowed in the skip list,
        /// </summary>
        private readonly int _topLevel;

        /// <summary>
        /// The promotion chance for each level. [0, 1).
        /// </summary>
        private readonly double _promotionProbability;

        /// <summary>
        /// The number of nodes in the SkipList.
        /// </summary>
        private int _count;

        /// <summary>
        /// If true, permits the insertion of duplicate priorities.
        /// </summary>
        private bool _allowDuplicatePriorities;

        /// <summary>
        /// Head of the skip list.
        /// </summary>
        private readonly SkipListNode _head;

        /// <summary>
        /// Tail of the skip list.
        /// </summary>
        private readonly SkipListNode _tail;

        /// <summary>
        /// Priority comparer used to order the priorities.
        /// </summary>
        private readonly IComparer<TPriority> _comparer;

        /// <summary>
        /// Element comparer used to order the elements.
        /// </summary>
        private readonly IComparer<TElement> _elementComparer;

        /// <summary>
        /// Background task processor which handles node removal.
        /// </summary>
        private readonly ITaskOrchestrator _taskOrchestrator;

        /// <summary>
        /// Random number generator.
        /// </summary>
        private static readonly Random RandomGenerator = new();

        #endregion

        #region Constructors

        /// <summary>
        /// Initializes a new instance of the <see cref="ConcurrentPriorityQueue{TPriority, TElement}"/> class.
        /// </summary>
        /// <param name="comparer">The comparer used to compare prioritys.</param>
        /// <param name="numberOfLevels">The maximum number of levels in the SkipList.</param>
        /// <param name="promotionProbability">The probability of promoting a node to a higher level.</param>
        /// <exception cref="ArgumentNullException">Thrown if <paramref name="comparer"/> is null.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown if <paramref name="numberOfLevels"/> is less than or equal to 0.</exception>
        /// <exception cref="ArgumentOutOfRangeException">Thrown if <paramref name="promotionProbability"/> is less than 0 or greater than 1.</exception>
        public ConcurrentPriorityQueue(
            ITaskOrchestrator taskOrchestrator, 
            IComparer<TPriority> comparer, 
            IComparer<TElement>? elementComparer = default, 
            int maxSize = 10000, 
            int? numberOfLevels = default, 
            double promotionProbability = 0.5,
            int maxOffset = 32,
            bool allowDuplicatePriorities = true)
        {
            ArgumentNullException.ThrowIfNull(comparer, nameof(comparer));
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxSize, nameof(maxSize));
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(numberOfLevels ?? 1, 0, nameof(numberOfLevels));
            ArgumentOutOfRangeException.ThrowIfLessThan(promotionProbability, 0, nameof(promotionProbability));
            ArgumentOutOfRangeException.ThrowIfGreaterThan(promotionProbability, 1, nameof(promotionProbability));

            _taskOrchestrator = taskOrchestrator;
            _comparer = comparer;
            _elementComparer = elementComparer ?? Comparer<TElement>.Default;
            _numberOfLevels = numberOfLevels ?? (int)Math.Ceiling(Math.Log(maxSize / 10, 1 / 0.5));
            _promotionProbability = promotionProbability;
            _maxOffset = maxOffset;
            _allowDuplicatePriorities = allowDuplicatePriorities;
            _maxSize = maxSize;
            _topLevel = _numberOfLevels - 1;

            _head = new SkipListNode(SkipListNode.NodeType.Head, _topLevel);
            _tail = new SkipListNode(SkipListNode.NodeType.Tail, _topLevel);

            // Link head to tail at all levels
            for (int level = 0; level <= _topLevel; level++)
            {
                _head.SetNextNode(level, _tail);
                Interlocked.Increment(ref _count);
            }

            _head.IsInserted = true;
            _tail.IsInserted = true;
        }

        #endregion
        #region Background Tasks

        /// <summary>
        /// Schedule the node to be physically deleted.
        /// </summary>
        /// <remarks>Node must already be logically deleted.</remarks>
        /// <param name="node"></param>
        /// <param name="topLevel"></param>
        private void ScheduleNodeRemoval(SkipListNode node, int? topLevel = null)
        {
            if (!node.IsDeleted) return;

            _taskOrchestrator.Run(() =>
            {
                // To preserve the invariant that lower levels are super-sets
                // of higher levels, always unlink top to bottom.
                int startingLevel = topLevel ?? node.TopLevel;

                for (int level = startingLevel; level >= 0; level--)
                {
                    SkipListNode predecessor = _head;
                    while (predecessor.GetNextNode(level) != node)
                    {
                        predecessor = predecessor.GetNextNode(level);
                    }
                    predecessor.SetNextNode(level, node.GetNextNode(level));
                }
            });
        }

        #endregion
        #region Core Operations

        /// <inheritdoc/>
        public bool TryAdd(TPriority priority, TElement element)
        {
            ArgumentNullException.ThrowIfNull(priority, nameof(priority));

            int insertLevel = GenerateLevel();

            while (true)
            {
                var searchResult = WeakSearch(priority);

                if (searchResult.IsFound && !_allowDuplicatePriorities)
                {
                    var curr = searchResult.GetNodeFound();

                    if (curr.IsDeleted || curr is null) continue;

                    WaitUntilIsInserted(searchResult.GetNodeFound());
                    return false;
                }

                int highestLevelLocked = InvalidLevel;
                try
                {
                    if (!ValidateInsertion(searchResult, insertLevel, ref highestLevelLocked))
                    {
                        continue;
                    }

                    var newNode = new SkipListNode(priority, element, insertLevel);
                    InsertNode(newNode, searchResult, insertLevel);

                    // Linearization point: MemoryBarrier not required since IsInserted
                    // is a volatile member (hence implicitly uses MemoryBarrier). 
                    newNode.IsInserted = true;
                    if (Interlocked.Increment(ref _count) > _maxSize) TryDeleteMin(out _);
                    return true;
                }
                finally
                {
                    // Unlock order is not important.
                    for (int level = highestLevelLocked; level >= 0; level--)
                    {
                        searchResult.GetPredecessor(level).Unlock();
                    }
                }
            }
        }

        /// <inheritdoc/>
        public bool TryDelete(TPriority priority)
        {
            ArgumentNullException.ThrowIfNull(priority, nameof(priority));

            SkipListNode? nodeToBeDeleted = null;
            bool isLogicallyDeleted = false;

            // Level at which the to be deleted node was found.
            int topLevel = InvalidLevel;

            while (true)
            {
                var searchResult = WeakSearch(priority);
                nodeToBeDeleted ??= searchResult.GetNodeFound();

                if (!isLogicallyDeleted && NodeIsInvalidOrDeleted(nodeToBeDeleted, searchResult))
                {   // Node not fully linked or already deleted
                    return false;
                }

                // Logically delete the node if not already done
                if (!isLogicallyDeleted && !LogicallyDeleteNode(nodeToBeDeleted, searchResult, ref topLevel))
                {
                    return false;
                }
                isLogicallyDeleted = true;

                int highestLevelLocked = InvalidLevel;
                try
                {
                    if (!ValidateDeletion(nodeToBeDeleted, searchResult, topLevel, ref highestLevelLocked))
                    {
                        continue;
                    }

                    ScheduleNodeRemoval(nodeToBeDeleted, topLevel);

                    nodeToBeDeleted.Unlock();
                    Interlocked.Decrement(ref _count);
                    return true;
                }
                finally
                {
                    for (int level = highestLevelLocked; level >= 0; level--)
                    {
                        searchResult.GetPredecessor(level).Unlock();
                    }
                }
            }
        }

        /* ALGORITHM TO BE IMPLEMENTED:
            1. **Initialization**
               * Set `K` and `M` (start with 1, adjust later).
               * Calculate `H` (starting height), `L` (max jump length), and `D` (levels to descend).
               * Ensure `H` is divisible by `D`.
               * Create an empty `Spray`.

            2. **Spray Phase**
               * Start at head node, height `H`.
               * While `height > 0`:
                 * Generate random `jumpLength` in `[0, L]`.
                 * Jump horizontally, up to `jumpLength` steps, stopping if you reach the end of the list or a key greater than `MIN_KEY`.
                 * Add current node to `Spray`.
                 * Descend `D` levels.

            3. **Acquire Locks**
               * Sort nodes in `Spray` by hash code.
               * Acquire locks on nodes in sorted order.

            4. **Validate and Delete**
               * Check if `Spray` is valid (no nodes deleted) and if the next node after the head has the minimum key (`MIN_KEY`).
               * If valid:
                 * Remove the minimum node from the skiplist.
               * Release locks in reverse order.
               * Return `true` or `false` indicating success.

            **Notes**

            * Implement `MIN_KEY` based on priority type and duplicate handling.
            * Node removal is specific to your skiplist.
            * Test with different `K`, `M`, and loads.
        */
        public bool TryDeleteMin(out TElement element)
        {
            element = default!;
            return default;
        }

        // BLOCKING TRYDELETEMIN (NAIVE)
        public bool TryDeleteMinNaive(out TElement element)
        {
            while (true)
            {
                SkipListNode? nodeToBeDeleted = _head.GetNextNode(0);

                // If the first node is the tail, the list is empty
                if (nodeToBeDeleted.Type == SkipListNode.NodeType.Tail)
                {
                    element = default!;
                    return false;
                }

                // Try to delete the node
                nodeToBeDeleted.Lock();
                try
                {
                    if (nodeToBeDeleted.IsDeleted || !nodeToBeDeleted.IsInserted)
                    {
                        // Node is already deleted or not fully linked, retry
                        continue;
                    }

                    // Logically delete and schedule physical deletion
                    nodeToBeDeleted.IsDeleted = true;
                    ScheduleNodeRemoval(nodeToBeDeleted);

                    element = nodeToBeDeleted.Element;
                    Interlocked.Decrement(ref _count);
                    return true;
                }
                finally
                {
                    nodeToBeDeleted.Unlock();
                }
            }
        }

        /* Linden-Jonsson Implementation
         * Currently non-functional
        public bool TryDeleteMin(out TElement element)
        {
            // Setup locals
            SkipListNode curr = _head;
            SkipListNode obsHead = curr.GetNextNode(0);
            SkipListNode newHead = default!;
            SkipListNode next;
            element = default!;
            long offset = 0;

            Thread.MemoryBarrier();

            // Traverses the lowest-level linked list from the head
            // by following next pointers, searching for the first 
            // node not having its delete flag set
            do
            {
                offset++;
                next = curr.GetNextNode(0);

                if (next.Type == SkipListNode.NodeType.Tail)
                {
                    return false;
                }

                // Do not allow head to point past a node currently being inserted
                if (newHead is null && curr.IsBeingInserted)
                {
                    newHead = curr;
                }

                if (next.IsDeleted) continue;

                Thread.MemoryBarrier();
                LogicallyDeleteNode(next);

            } while (GetNextUnmarkedNode(ref curr, ref next) && next.IsDeleted);

            // Update our locals
            element = curr.Element;
            newHead ??= curr;

            // If the offset is less than or equal to the maximum offset, return
            if (offset <= _maxOffset) return true;

            // Optimization: If the head's next node is not the observed head, return
            if (_head.GetNextNode(0) != obsHead) return true;

            // Attempt to update the head's next node using CompareExchange
            if (Interlocked.CompareExchange(ref _head.GetNextNodeRef(0), newHead, obsHead) == obsHead)
            {
                // Update higher-level pointers
                Restructure();

                // Original C implementation manually marked
                // each dead node for garbage collection.
                // Mark nodes between obsHead and newHead for recycling
                //SkipListNode node = obsHead;
                //while (node != newHead)
                //{
                //    next = node.GetNextNode(0);
                //    MarkForRecycle(node);
                //    node = next;
                //}
            }

            return true;
        }

        public bool GetNextUnmarkedNode(ref SkipListNode curr, ref SkipListNode next)
        {
            // Loop until we find a non-deleted node
            while (next.IsDeleted)
            {
                if (next.Type == SkipListNode.NodeType.Tail)
                {
                    return false;
                }

                curr = next;
                next = curr.GetNextNode(0);
            }

            // Return the prev
            //curr = next;
            return true;
        }
        */

        /// <inheritdoc/>
        // Needs to be refactored
        public void Update(TPriority priority, TElement element)
        {
            ArgumentNullException.ThrowIfNull(priority, nameof(priority));

            var searchResult = WeakSearch(priority);

            if (NodeNotFoundOrInvalid(searchResult))
            {
                throw new ArgumentException("The priority does not exist or is being deleted.", nameof(priority));
            }

            SkipListNode nodeToBeUpdated = searchResult.GetNodeFound();
            nodeToBeUpdated.Lock();
            try
            {
                if (nodeToBeUpdated.IsDeleted)
                {
                    throw new ArgumentException("The priority does not exist or is being deleted.", nameof(priority));
                }

                nodeToBeUpdated.Element = element;
            }
            finally
            {
                nodeToBeUpdated.Unlock();
            }
        }

        /// <inheritdoc/>
        // Needs to be refactored
        public void Update(TPriority priority, Func<TPriority, TElement, TElement> updateFunction)
        {
            ArgumentNullException.ThrowIfNull(priority, nameof(priority));
            ArgumentNullException.ThrowIfNull(updateFunction, nameof(updateFunction));

            var searchResult = WeakSearch(priority);

            if (NodeNotFoundOrInvalid(searchResult))
            {
                throw new ArgumentException("The priority does not exist or is being deleted.", nameof(priority));
            }

            SkipListNode nodeToBeUpdated = searchResult.GetNodeFound();
            nodeToBeUpdated.Lock();
            try
            {
                if (nodeToBeUpdated.IsDeleted)
                {
                    throw new ArgumentException("The priority does not exist or is being deleted.", nameof(priority));
                }

                nodeToBeUpdated.Element = updateFunction(priority, nodeToBeUpdated.Element);
            }
            finally
            {
                nodeToBeUpdated.Unlock();
            }
        }

        /// <inheritdoc/>
        public bool ContainsPriority(TPriority priority)
        {
            ArgumentNullException.ThrowIfNull(priority, nameof(priority));

            var searchResult = WeakSearch(priority);

            // If node is not found, not logically inserted or logically removed, return false.
            return searchResult.IsFound
                && searchResult.GetNodeFound().IsInserted
                && !searchResult.GetNodeFound().IsDeleted;
        }

        /// <inheritdoc/>
        public int GetCount() => _count;

        /// <inheritdoc/>
        public IEnumerator<TPriority> GetEnumerator()
        {
            SkipListNode current = _head;
            while (true)
            {
                current = current.GetNextNode(BottomLevel);

                // If current is tail, this must be the end of the list.
                if (current.Type == SkipListNode.NodeType.Tail) yield break;

                // Takes advantage of the fact that next is set before 
                // the node is physically linked.
                if (!current.IsInserted || current.IsDeleted) continue;

                yield return current.Priority;
            }
        }

        #endregion
        #region Helper Methods

        /// <summary>
        /// Waits (spins) until the specified node is marked as logically inserted, 
        /// meaning it has been physically inserted at every level.
        /// </summary>
        /// <param name="node">The node to wait for.</param>
        private static void WaitUntilIsInserted(SkipListNode node)
        {
            SpinWait.SpinUntil(() => node.IsInserted);
        }

        /// <summary>
        /// Generates a random level for a new node based on the promotion probability.
        /// The probability of picking a given level is P(L) = p ^ -(L + 1) where p = PromotionChance.
        /// </summary>
        /// <returns>The generated level.</returns>
        private int GenerateLevel()
        {
            int level = 0;

            while (level < _topLevel && RandomGenerator.NextDouble() <= _promotionProbability)
            {
                level++;
            }

            return level;
        }

        /// <summary>
        /// Performs a lock-free search for the node with the specified priority.
        /// </summary>
        /// <param name="priority">The priority to search for.</param>
        /// <returns>A <see cref="SearchResult"/> instance containing the results of the search.</returns>
        /// <remarks>
        /// <para>
        /// If the priority is found, the <see cref="SearchResult.IsFound"/> property will be true, 
        /// and the <see cref="SearchResult.PredecessorArray"/> and <see cref="SearchResult.SuccessorArray"/> 
        /// will contain the predecessor and successor nodes at each level.
        /// </para>
        /// <para>
        /// If the priority is not found, the <see cref="SearchResult.IsFound"/> property will be false, 
        /// and the <see cref="SearchResult.PredecessorArray"/> and <see cref="SearchResult.SuccessorArray"/> 
        /// will contain the nodes that would have been the predecessor and successor of the node with the 
        /// specified priority if it existed.
        /// </para>
        /// </remarks>
        private SearchResult WeakSearch(TPriority priority)
        {
            int levelFound = InvalidLevel;
            SkipListNode[] predecessorArray = new SkipListNode[_numberOfLevels];
            SkipListNode[] successorArray = new SkipListNode[_numberOfLevels];

            SkipListNode predecessor = _head;
            for (int level = _topLevel; level >= 0; level--)
            {
                SkipListNode current = predecessor.GetNextNode(level);

                while (Compare(current, priority) < 0) 
                {
                    predecessor = current;
                    current = predecessor.GetNextNode(level);
                }

                // At this point, current is >= searchpriority
                if (levelFound == InvalidLevel && Compare(current, priority) == 0)
                {
                    levelFound = level;
                }

                predecessorArray[level] = predecessor;
                successorArray[level] = current;
            }

            return new SearchResult(levelFound, predecessorArray, successorArray);
        }

        /// <summary>
        /// Updates the pointers of <see cref="_head"/>
        /// except for level 0, starting from the highest level.
        /// </summary>
        private void Restructure()
        {
            SkipListNode pred = _head;
            int i = _topLevel;

            while (i > 0)
            {
                // Record observed head
                SkipListNode obsHead = _head.GetNextNode(i);
                Thread.MemoryBarrier();

                // Take one step forward from pred
                SkipListNode curr = pred.GetNextNode(i);

                // Check if observed head's next node is not deleted
                if (!obsHead.GetNextNode(0).IsDeleted)
                {
                    i--;
                    continue;
                }

                // Traverse level until a non-deleted node is found
                while (curr.GetNextNode(0).IsDeleted)
                {
                    pred = curr;
                    curr = pred.GetNextNode(i);
                }

                // Swing head pointer using CAS
                if (Interlocked.CompareExchange(ref _head.GetNextNodeRef(i), curr, obsHead) == obsHead)
                {
                    i--;
                }
            }
        }

        private int Compare(SkipListNode node, TPriority priority)
        {
            return node.Type switch
            {
                SkipListNode.NodeType.Head => -1,
                SkipListNode.NodeType.Tail => 1,
                _ => _comparer.Compare(node.Priority, priority)
            };
        }

        private int Compare(SkipListNode node, TElement element)
        {
            return node.Type switch
            {
                SkipListNode.NodeType.Head => -1,
                SkipListNode.NodeType.Tail => 1,
                _ => _elementComparer.Compare(node.Element, element)
            };
        }

        private static bool IsValidLevel(SkipListNode predecessor, SkipListNode successor, int level)
        {
            ArgumentNullException.ThrowIfNull(predecessor, nameof(predecessor));
            ArgumentNullException.ThrowIfNull(successor, nameof(successor));

            return !predecessor.IsDeleted
                   && !successor.IsDeleted
                   && predecessor.GetNextNode(level) == successor;
        }

        private static bool NodeNotFoundOrInvalid(SearchResult searchResult)
        {
            return !searchResult.IsFound
                || !searchResult.GetNodeFound().IsInserted
                || searchResult.GetNodeFound().IsDeleted;
        }

        private static bool ValidateInsertion(SearchResult searchResult, int insertLevel, ref int highestLevelLocked)
        {
            bool isValid = true;
            for (int level = 0; isValid && level <= insertLevel; level++)
            {
                var predecessor = searchResult.GetPredecessor(level);
                var successor = searchResult.GetSuccessor(level);

                predecessor.Lock();
                Interlocked.Exchange(ref highestLevelLocked, level);

                // If predecessor is locked and the predecessor is still pointing at the successor, successor cannot be deleted.
                isValid = IsValidLevel(predecessor, successor, level);
            }

            return isValid;
        }

        private static void InsertNode(SkipListNode newNode, SearchResult searchResult, int insertLevel)
        {
            // Initialize all the next pointers.
            for (int level = 0; level <= insertLevel; level++)
            {
                newNode.SetNextNode(level, searchResult.GetSuccessor(level));
            }

            // Ensure that the node is fully initialized before physical linking starts.
            Thread.MemoryBarrier();

            for (int level = 0; level <= insertLevel; level++)
            {
                // Note that this is required for correctness.
                // Remove takes a dependency of the fact that if found at expected level, all the predecessors have already been correctly linked.
                // Hence we only need to use a MemoryBarrier before linking in the top level. 
                if (level == insertLevel)
                {
                    Thread.MemoryBarrier();
                }

                searchResult.GetPredecessor(level).SetNextNode(level, newNode);
            }
        }

        private static bool NodeIsInvalidOrDeleted(SkipListNode nodeToBeDeleted, SearchResult searchResult)
        {
            return !nodeToBeDeleted.IsInserted
                || nodeToBeDeleted.TopLevel != searchResult.LevelFound
                || nodeToBeDeleted.IsDeleted;
        }

        private static bool LogicallyDeleteNode(SkipListNode nodeToBeDeleted, SearchResult searchResult, ref int topLevel)
        {
            Interlocked.Exchange(ref topLevel, searchResult.LevelFound);
            nodeToBeDeleted.Lock();
            if (nodeToBeDeleted.IsDeleted)
            {
                nodeToBeDeleted.Unlock();
                return false;
            }

            // Linearization point: IsDeleted is volatile.
            nodeToBeDeleted.IsDeleted = true;
            return true;
        }

        /* Used for Linden-Jonsson
        private static bool LogicallyDeleteNode(SkipListNode nodeToBeDeleted)
        {
            while (true)
            {
                // Linearization point: IsDeleted is volatile.
                if (nodeToBeDeleted.IsDeleted 
                    || Interlocked.CompareExchange(ref nodeToBeDeleted.GetIsDeletedRef(), true, false) == false)
                {
                    //nodeToBeDeleted = nodeToBeDeleted.GetNextNode(0);
                    return true;
                }
            }
        }
        */

        private static bool ValidateDeletion(SkipListNode nodeToBeDeleted, SearchResult searchResult, int topLevel, ref int highestLevelUnlocked)
        {
            bool isValid = true;
            for (int level = 0; isValid && level <= topLevel; level++)
            {
                var predecessor = searchResult.GetPredecessor(level);
                predecessor.Lock();
                Interlocked.Exchange(ref highestLevelUnlocked, level);
                isValid = predecessor.IsDeleted == false && predecessor.GetNextNode(level) == nodeToBeDeleted;
            }
            return isValid;
        }

        #endregion
        #region Internal Classes

        /// <summary>
        /// Represents a node in the SkipList.
        /// </summary>
        public sealed class SkipListNode
        {
            private readonly Lock _nodeLock = new();
            private readonly SkipListNode[] _nextNodeArray;
            private volatile bool _isInserted;
            private volatile bool _isDeleted;

            /// <summary>
            /// Initializes a new instance of the <see cref="Node"/> class.
            /// </summary>
            /// <param name="nodeType">The type of the node.</param>
            /// <param name="height">The height (level) of the node.</param>
            public SkipListNode(NodeType nodeType, int height)
            {
                Priority = default!;
                Element = default!;
                Type = nodeType;
                _nextNodeArray = new SkipListNode[height + 1];
            }

            /// <summary>
            /// Initializes a new instance of the <see cref="SkipListNode"/> class.
            /// </summary>
            /// <param name="priority">The priority associated with the node.</param>
            /// <param name="element">The element associated with the node.</param>
            /// <param name="height">The height (level) of the node.</param>
            public SkipListNode(TPriority priority, TElement element, int height)
            {
                Priority = priority;
                Element = element;
                Type = NodeType.Data;
                _nextNodeArray = new SkipListNode[height + 1];
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
            public TPriority Priority { get; }

            /// <summary>
            /// Gets or sets the element associated with the node.
            /// </summary>
            public TElement Element { get; set; }

            /// <summary>
            /// Gets or sets the type of the node.
            /// </summary>
            public NodeType Type { get; set; }

            /// <summary>
            /// Gets or sets a element indicating whether the node has been logically inserted.
            /// </summary>
            public bool IsInserted { get => _isInserted; set => _isInserted = value; }

            /// <summary>
            /// Gets or sets a element indicating whether the node has been logically deleted.
            /// </summary>
            public bool IsDeleted { get => _isDeleted; set => _isDeleted = value; }

            /// <summary>
            /// Get the direct reference of IsDeleted.
            /// </summary>
            public ref bool GetIsDeletedRef() => ref _isDeleted;

            /// <summary>
            /// Gets the top level (highest index) of the node in the SkipList.
            /// </summary>
            public int TopLevel => _nextNodeArray.Length - 1;

            /// <summary>
            /// Gets the next node at the specified height (level).
            /// </summary>
            /// <param name="height">The height (level) at which to get the next node.</param>
            /// <returns>The next node at the specified height.</returns>
            public SkipListNode GetNextNode(int height) => _nextNodeArray[height];

            /// <summary>
            /// Gets the next node at the specified height (level).
            /// </summary>
            /// <param name="height">The height (level) at which to get the next node.</param>
            /// <returns>The next node at the specified height.</returns>
            public ref SkipListNode GetNextNodeRef(int height) => ref _nextNodeArray[height];

            /// <summary>
            /// Sets the next node at the specified height (level).
            /// </summary>
            /// <param name="height">The height (level) at which to set the next node.</param>
            /// <param name="next">The next node to set.</param>
            public void SetNextNode(int height, SkipListNode next) => _nextNodeArray[height] = next;

            /// <summary>
            /// Acquires the lock associated with the node.
            /// </summary>
            public void Lock() => _nodeLock.Enter();

            /// <summary>
            /// Releases the lock associated with the node.
            /// </summary>
            public void Unlock() => _nodeLock.Exit();
        }

        /// <summary>
        /// Represents the result of a search operation in the SkipList.
        /// </summary>
        /// <param name="LevelFound">The level at which the priority was found (or <see cref="NotFoundLevel"/> if not found).</param>
        /// <param name="PredecessorArray">An array of predecessor nodes at each level.</param>
        /// <param name="SuccessorArray">An array of successor nodes at each level.</param>
        public sealed record SearchResult(int LevelFound, SkipListNode[] PredecessorArray, SkipListNode[] SuccessorArray)
        {
            /// <summary>
            /// Represents the level element when a priority is not found in the SkipList.
            /// </summary>
            public const int NotFoundLevel = -1;

            /// <summary>
            /// Gets a element indicating whether the priority was found in the SkipList.
            /// </summary>
            public bool IsFound => LevelFound != NotFoundLevel;

            /// <summary>
            /// Gets the predecessor node at the specified level.
            /// </summary>
            /// <param name="level">The level at which to get the predecessor node.</param>
            /// <returns>The predecessor node at the specified level.</returns>
            /// <exception cref="ArgumentNullException">Thrown if <see cref="PredecessorArray"/> is null.</exception>
            public SkipListNode GetPredecessor(int level)
            {
                ArgumentNullException.ThrowIfNull(PredecessorArray, nameof(PredecessorArray));
                return PredecessorArray[level];
            }

            /// <summary>
            /// Gets the successor node at the specified level.
            /// </summary>
            /// <param name="level">The level at which to get the successor node.</param>
            /// <returns>The successor node at the specified level.</returns>
            /// <exception cref="ArgumentNullException">Thrown if <see cref="SuccessorArray"/> is null.</exception>
            public SkipListNode GetSuccessor(int level)
            {
                ArgumentNullException.ThrowIfNull(SuccessorArray, nameof(SuccessorArray));
                return SuccessorArray[level];
            }

            /// <summary>
            /// Gets the node that was found during the search.
            /// </summary>
            /// <returns>The node that was found.</returns>
            /// <exception cref="InvalidOperationException">Thrown if the priority was not found (<see cref="IsFound"/> is false).</exception>
            public SkipListNode GetNodeFound()
            {
                if (!IsFound) throw new InvalidOperationException("Cannot get node found when the priority was not found.");

                return SuccessorArray[LevelFound];
            }
        }
        #endregion
    }
}