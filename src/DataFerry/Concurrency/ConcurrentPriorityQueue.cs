// ===========================================================================
// <copyright file="ConcurrentPriorityQueue.cs" company="Level Up Software">
// Copyright (c) Level Up Software. All rights reserved.
// </copyright>
// ===========================================================================

using System.Diagnostics.CodeAnalysis;
using lvlup.DataFerry.Concurrency.Contracts;
using lvlup.DataFerry.Orchestrators.Contracts;

namespace lvlup.DataFerry.Concurrency;

/// <summary>
/// A concurrent PriorityQueue implemented as a lock-based SkipList with relaxed semantics.
/// </summary>
/// <typeparam name="TPriority">The type used for priority values.</typeparam>
/// <typeparam name="TElement">The type of the elements stored in the queue.</typeparam>
/// <remarks>
/// <para>
/// A SkipList is a probabilistic data structure providing efficient search, insertion, and deletion
/// operations with an expected logarithmic time complexity. Its probabilistic balancing is well-suited
/// for concurrent scenarios. This implementation uses locks on individual nodes during mutation
/// operations and supports sequence numbers internally to handle duplicate priorities correctly.
/// </para>
/// <para>
/// This implementation offers:
/// </para>
/// <list type="bullet">
/// <item>Expected O(log n) time complexity for <c>TryAdd</c>, <c>Update</c>, <c>TryDelete</c>, <c>TryDeleteMin</c>, and <c>TryDeleteMin</c> operations.</item>
/// <item>Lock-free reads for <c>ContainsPriority</c> and <c>GetCount</c>.</item>
/// <item>Lock-free priority enumeration via <c>GetEnumerator</c>.</item>
/// <item>Support for custom priority comparison via injected <see cref="IComparer{TPriority}"/>.</item>
/// <item>Support for bounding the queue size or allowing effectively unbounded queue size.</item>
/// </list>
/// <para>
/// <b>Implementation Details:</b>
/// </para>
/// <para>
/// This implementation employs logical deletion (<c>IsDeleted</c> flag) and insertion (<c>IsInserted</c> flag)
/// to optimize performance and ensure thread safety. Nodes are marked as deleted logically before being physically removed, and
/// they are inserted level by level to maintain structural consistency. Physical node removal is scheduled to run asynchronously via the
/// provided <see cref="ITaskOrchestrator"/>. Internal sequence numbers ensure unique node identity even
/// with duplicate priorities. This implementation is based on the Lotan-Shavit algorithm,
/// with an alternative <see cref="TryDeleteMin"/> method utilizing the SprayList
/// algorithm (with tunable parameters <c>OffsetK</c> and <c>OffsetM</c>) for potentially
/// lower-contention removal of near-minimum elements, which is especially useful when the queue is at capacity or being utilized in high-throughput scenarios.
/// </para>
/// <para>
/// <b>Invariants:</b>
/// </para>
/// <para>
/// The list at any level <c>L</c> is a subset of the list at level <c>L-1</c>. This invariant ensures
/// structural consistency, facilitated by adding nodes bottom-up and removing them (physically) top-down.
/// </para>
/// <para>
/// <b>Locking:</b>
/// </para>
/// <para>
/// Fine-grained locking is used on individual nodes during mutation operations (add, delete, update).
/// Locks are generally acquired bottom-up during validation phases (e.g., <c>ValidateInsertion</c>)
/// to help prevent deadlocks. Typically, the order of lock release is not critical.
/// </para>
/// </remarks>
public class ConcurrentPriorityQueue<TPriority, TElement> : IConcurrentPriorityQueue<TPriority, TElement>
{
    #region Global Variables

    /// <summary>
    /// Default size limit.
    /// </summary>
    public const int DefaultMaxSize = 10000;

    /// <summary>
    /// Default promotion chance for each level. [0, 1).
    /// </summary>
    public const double DefaultPromotionProbability = 0.5;

    /// <summary>
    /// The default tuning constant (K) added to the height used in the calculation during the Spray operation within <see cref="TryDeleteMin"/>.
    /// </summary>
    /// <remarks>
    /// Increasing this value starts the spray slightly higher, potentially giving it more room to spread. This does not typically need to be adjusted.
    /// </remarks>
    public const int DefaultSprayOffsetK = 1;

    /// <summary>
    /// The default tuning constant (M) multiplied by the jump length used in the calculation during the Spray operation within <see cref="TryDeleteMin"/>.
    /// </summary>
    /// <remarks>
    /// This value directly scales the maximum jump length. This should be adjusted in the case where reducing contention is more desirable than deleting a node with a strictly higher priority.
    /// </remarks>
    public const int DefaultSprayOffsetM = 1;

    /// <summary>
    /// Invalid level.
    /// </summary>
    private const int InvalidLevel = -1;

    /// <summary>
    /// Bottom level.
    /// </summary>
    private const int BottomLevel = 0;

    /// <summary>
    /// The maximum number of elements allowed in the queue.
    /// </summary>
    private readonly int _maxSize;

    /// <summary>
    /// Number of levels in the skip list.
    /// </summary>
    private readonly int _numberOfLevels;

    /// <summary>
    /// Highest level allowed in the skip list.
    /// </summary>
    private readonly int _topLevel;

    /// <summary>
    /// The configured tuning constant (K) for the current <see cref="ConcurrentPriorityQueue{TPriority,TElement}"/> instance.
    /// </summary>
    private readonly int _offsetK;

    /// <summary>
    /// The configured tuning constant (M) for the current <see cref="ConcurrentPriorityQueue{TPriority,TElement}"/> instance.
    /// </summary>
    private readonly int _offsetM;

    /// <summary>
    /// The promotion chance for each level. [0, 1).
    /// </summary>
    private readonly double _promotionProbability;

    /// <summary>
    /// The number of nodes in the SkipList.
    /// </summary>
    private int _count;

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
    /// Background task processor which handles node removal.
    /// </summary>
    private readonly ITaskOrchestrator _taskOrchestrator;

    /// <summary>
    /// Random number generator.
    /// </summary>
    private readonly Random _randomGenerator = Random.Shared;

    #endregion
    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="ConcurrentPriorityQueue{TPriority, TElement}"/> class.
    /// </summary>
    /// <param name="taskOrchestrator">The scheduler used to orchestrate background tasks processing the physical deletion of nodes.</param>
    /// <param name="comparer">The comparer used to compare priorities. This will be used for all priority comparisons within the queue.</param>
    /// <param name="maxSize">Optional. The maximum number of elements allowed in the queue. Defaults to <see cref="DefaultMaxSize"/>. Pass <c>int.MaxValue</c> to functionally avoid a size bound.</param>
    /// <param name="offsetK">Optional. The tuning constant K (affecting spray height) for the SprayList probabilistic delete-min operation (<see cref="TryDeleteMin"/>). Defaults to <see cref="DefaultSprayOffsetK"/>.</param>
    /// <param name="offsetM">Optional. The tuning constant M (scaling spray jump length) for the SprayList probabilistic delete-min operation (<see cref="TryDeleteMin"/>). Defaults to <see cref="DefaultSprayOffsetM"/>.</param>
    /// <param name="promotionProbability">Optional. The probability (0 to 1, exclusive) of promoting a new node to the next higher level during insertion. Defaults to <see cref="DefaultPromotionProbability"/>.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="taskOrchestrator"/> or <paramref name="comparer"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if <paramref name="maxSize"/> is less than 1.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if <paramref name="offsetK"/> is less than or equal to 0.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if <paramref name="offsetM"/> is less than or equal to 0.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if <paramref name="promotionProbability"/> is less than or equal to 0 or greater than or equal to 1.</exception>
    /// <remarks>
    /// This constructor sets up the essential components of the concurrent priority queue,
    /// including the head/tail sentinel nodes, level calculation (if not provided), and tuning parameters.
    /// The actual number of levels and other parameters might be further adjusted based on internal logic (e.g., capping levels).
    /// </remarks>
    public ConcurrentPriorityQueue(
        ITaskOrchestrator taskOrchestrator,
        IComparer<TPriority> comparer,
        int maxSize = DefaultMaxSize,
        int offsetK = DefaultSprayOffsetK,
        int offsetM = DefaultSprayOffsetM,
        double promotionProbability = DefaultPromotionProbability)
    {
        ArgumentNullException.ThrowIfNull(taskOrchestrator, nameof(taskOrchestrator));
        ArgumentNullException.ThrowIfNull(comparer, nameof(comparer));
        ArgumentOutOfRangeException.ThrowIfLessThan(maxSize, 1, nameof(maxSize));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(offsetK, nameof(offsetK));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(offsetM, nameof(offsetM));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(promotionProbability, nameof(promotionProbability));
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(promotionProbability, 1, nameof(promotionProbability));

        _taskOrchestrator = taskOrchestrator;
        _comparer = comparer;
        _maxSize = maxSize;
        _offsetK = offsetK;
        _offsetM = offsetM;
        _promotionProbability = promotionProbability;
        _numberOfLevels = CalculateOptimalLevels(_maxSize, _promotionProbability);
        _topLevel = _numberOfLevels - 1;
        _count = 0;

        _head = new SkipListNode(SkipListNode.NodeType.Head, _topLevel);
        _tail = new SkipListNode(SkipListNode.NodeType.Tail, _topLevel);

        // Link head to tail at all levels
        for (int level = BottomLevel; level <= _topLevel; level++)
        {
            _head.SetNextNode(level, _tail);
        }

        _head.IsInserted = true;
        _tail.IsInserted = true;
        return;

        // Calculate optimal number of levels for the SkipList
        static int CalculateOptimalLevels(int currentMaxSize, double currentPromotionProbability)
        {
            // Define the absolute maximum cap
            const int maxLevelCap = 32;

            // Handle "unlimited" size case explicitly
            if (currentMaxSize == int.MaxValue)
            {
                return maxLevelCap;
            }

            // Handle edge case for logarithm calculation (N must be >= 2)
            int n = Math.Max(2, currentMaxSize);

            // Calculate the logarithm base (1/p)
            double logBase = 1.0 / currentPromotionProbability;

            // Calculate theoretical levels needed
            double theoreticalLevels = Math.Log(n, logBase);

            // Round up and apply the absolute cap
            int calculatedLevels = (int)Math.Ceiling(theoreticalLevels);
            return Math.Min(calculatedLevels, maxLevelCap);
        }
    }

    #endregion
    #region Core Operations

    /// <inheritdoc/>
    public bool TryAdd(TPriority priority, TElement element)
    {
        ArgumentNullException.ThrowIfNull(priority, nameof(priority));
        ArgumentNullException.ThrowIfNull(element, nameof(element));

        // Generate a new level and node
        int insertLevel = GenerateLevel();
        var newNode = new SkipListNode(priority, element, insertLevel, _comparer);

        while (true)
        {
            // Identify the links the new level and node will have
            var searchResult = StructuralSearch(newNode);
            int highestLevelLocked = InvalidLevel;

            try
            {
                // Ensure we can proceed with physical insertion
                if (!ValidateInsertion(searchResult, insertLevel, ref highestLevelLocked))
                {
                    continue;
                }

                // Physically insert the new node
                InsertNode(newNode, searchResult, insertLevel);

                // Linearization point: MemoryBarrier not required since IsInserted is a volatile member
                newNode.IsInserted = true;

                // If we exceed the size bound, delete the minimal node
                if (Interlocked.Increment(ref _count) > _maxSize && _maxSize is not int.MaxValue)
                {
                    // We use our SprayList algorithm to avoid contention
                    TryDeleteMin(out _);
                }

                return true;
            }
            finally
            {
                // Unlock order is not important
                for (int level = highestLevelLocked; level >= 0; level--)
                {
                    searchResult.GetPredecessor(level)?.Unlock();
                }
            }
        }
    }

    /// <inheritdoc/>
    public bool TryDelete(TPriority priority)
    {
        ArgumentNullException.ThrowIfNull(priority, nameof(priority));

        // Locate the node to delete
        var nodeToDelete = InlineSearch(priority);

        // Check if a candidate was found
        if (nodeToDelete is null) return false;

        while (true)
        {
            var searchResult = StructuralSearch(nodeToDelete);

            // Ensure validity before deletion
            if (NodeIsInvalidOrDeleted(nodeToDelete)) return false;

            // Attempt logical delete
            if (!LogicallyDeleteNode(nodeToDelete)) return false;

            // Validate that the logical deletion succeeded
            // and that the state of predecessors is correct
            bool isValid = TryValidateAfterLogicalDelete(nodeToDelete, searchResult);

            // If this is not the case, retry
            if (!isValid) continue;

            // Success path; schedule node removal
            try
            {
                SchedulePhysicalNodeRemoval(nodeToDelete, nodeToDelete.TopLevel);
                Interlocked.Decrement(ref _count);
                return true;
            }
            finally
            {
                nodeToDelete.Unlock();
            }
        }
    }

    /// <inheritdoc/>
    public bool TryDeleteAbsoluteMin(out TElement element)
    {
        SkipListNode curr = _head.GetNextNode(BottomLevel);
        element = default!;

        while (curr.Type is not SkipListNode.NodeType.Tail)
        {
            // Check if the node is valid before attempting to lock
            if (NodeIsInvalidOrDeleted(curr))
            {
                curr = curr.GetNextNode(BottomLevel);
                continue;
            }

            // Found a potential candidate; attempt to delete
            if (LogicallyDeleteNode(curr))
            {
                try
                {
                    // Schedule removal and return
                    SchedulePhysicalNodeRemoval(curr);
                    element = curr.Element;

                    Interlocked.Decrement(ref _count);
                    return true;
                }
                finally
                {
                    curr.Unlock();
                }
            }

            curr = curr.GetNextNode(BottomLevel);
        }

        return false;
    }

    /// <inheritdoc/>
    public bool TryDeleteMin(out TElement element, double retryProbability = 1.0)
    {
        // If count is too small, perform TryDeleteAbsoluteMin
        int currCount = GetCount();
        if (currCount <= 1) return TryDeleteAbsoluteMin(out element);

        element = default!;

        // Perform the spray operation
        var curr = SpraySearch(currCount);

        // Spray complete; attempt to logically delete candidate
        if (!LogicallyDeleteNode(curr))
        {
            // Failure path; retry probabilistically
            return _randomGenerator.NextDouble() < retryProbability &&
                TryDeleteMin(out element, retryProbability * 0.5);
        }

        try
        {
            // Success path
            SchedulePhysicalNodeRemoval(curr);
            element = curr.Element;
            Interlocked.Decrement(ref _count);
            return true;
        }
        finally
        {
            curr.Unlock();
        }
    }

    /// <inheritdoc/>
    public IEnumerable<(TPriority priority, TElement element)> SampleNearMin(int sampleSize, int maxAttemptsMultiplier = 3)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleSize, nameof(sampleSize));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxAttemptsMultiplier, nameof(maxAttemptsMultiplier));

        // If count is too small, return empty list
        int currCount = GetCount();
        if (currCount <= sampleSize) return [];

        // Use PLINQ to perform the sprays in parallel
        return Enumerable.Range(0, sampleSize * maxAttemptsMultiplier)
            .AsParallel()
            .Select(_ => SpraySearch(currCount))
            .Where(node => !NodeIsInvalidOrDeleted(node))
            .Distinct()
            .Take(sampleSize)
            .Select(node => (node.Priority, node.Element))
            .ToList();
    }

    /// <inheritdoc/>
    public bool Update(TPriority priority, TElement element)
    {
        ArgumentNullException.ThrowIfNull(priority, nameof(priority));
        ArgumentNullException.ThrowIfNull(element, nameof(element));

        // Locate the node to update
        var nodeToUpdate = InlineSearch(priority);

        // Check if a candidate was found
        if (nodeToUpdate is null) return false;

        // Try to perform the update
        nodeToUpdate.Lock();
        try
        {
            // Check to see if the node was deleted
            // between the search and acquiring the lock
            if (nodeToUpdate.IsDeleted)
            {
                // Candidate node was deleted while trying to update
                return false;
            }

            // Perform the update
            nodeToUpdate.Element = element;
            return true;
        }
        finally
        {
            nodeToUpdate.Unlock();
        }
    }

    /// <inheritdoc/>
    public bool Update(TPriority priority, Func<TPriority, TElement, TElement> updateFunction)
    {
        ArgumentNullException.ThrowIfNull(priority, nameof(priority));
        ArgumentNullException.ThrowIfNull(updateFunction, nameof(updateFunction));

        // Locate the node to update
        var nodeToUpdate = InlineSearch(priority);

        // Check if a candidate was found
        if (nodeToUpdate is null) return false;

        // Try to perform the update
        nodeToUpdate.Lock();
        try
        {
            // Check to see if the node was deleted
            // between the search and acquiring the lock
            if (nodeToUpdate.IsDeleted)
            {
                // Candidate node was deleted while trying to update
                return false;
            }

            // Perform the update using the function
            nodeToUpdate.Element = updateFunction(priority, nodeToUpdate.Element);
            return true;
        }
        finally
        {
            nodeToUpdate.Unlock();
        }
    }

    /// <inheritdoc/>
    public bool ContainsPriority(TPriority priority)
    {
        ArgumentNullException.ThrowIfNull(priority, nameof(priority));

        // Locate the node with the desired priority
        var foundNode = InlineSearch(priority);

        return foundNode is not null;
    }

    /// <inheritdoc/>
    public int GetCount() => Interlocked.CompareExchange(ref _count, 0, 0);

    /// <inheritdoc/>
    public IEnumerator<TPriority> GetEnumerator()
    {
        SkipListNode curr = _head;
        while (true)
        {
            curr = curr.GetNextNode(BottomLevel);

            // If current is tail, this must be the end of the list.
            if (curr.Type is SkipListNode.NodeType.Tail) yield break;

            // Takes advantage of the fact that next is set before
            // the node is physically linked.
            if (!curr.IsInserted || curr.IsDeleted) continue;

            yield return curr.Priority;
        }
    }

    #endregion
    #region Shared Helper Methods

    /// <summary>
    /// Checks if a SkipList node is invalid for standard operations (e.g., update, delete).
    /// </summary>
    /// <param name="curr">The node to check. Can be null.</param>
    /// <returns>
    /// <c>true</c> if the node is null, a Tail node, not yet fully inserted (<see cref="SkipListNode.IsInserted"/> is false),
    /// or already marked as deleted (<see cref="SkipListNode.IsDeleted"/> is true); otherwise, <c>false</c>.
    /// </returns>
    /// <remarks>
    /// This is typically used as a preliminary check before attempting an operation like locking or deletion on a node.
    /// It performs lock-free reads of the node's properties (including volatile fields).
    /// </remarks>
    internal static bool NodeIsInvalidOrDeleted(SkipListNode? curr)
    {
        return curr?.Type is not SkipListNode.NodeType.Data
               || !curr.IsInserted
               || curr.IsDeleted;
    }

    /// <summary>
    /// Attempts to atomically mark the specified data node as logically deleted.
    /// </summary>
    /// <param name="curr">The data node to mark as deleted.</param>
    /// <returns>
    /// <c>true</c> if the node was successfully marked as deleted (node remains locked by this method);
    /// <c>false</c> if the node is not a Data node or was already deleted (node is unlocked by this method).
    /// </returns>
    /// <remarks>
    /// This method acquires the node's lock (<see cref="SkipListNode.Lock"/>).
    /// If successful (returns <c>true</c>), the node's <see cref="SkipListNode.IsDeleted"/> flag is set,
    /// and the caller is responsible for eventually unlocking the node after subsequent operations
    /// (e.g., validation, scheduling physical removal).
    /// If it fails (returns <c>false</c>), the node is unlocked internally before returning.
    /// The write to the volatile <c>IsDeleted</c> field acts as the linearization point for the logical deletion.
    /// </remarks>
    internal static bool LogicallyDeleteNode(SkipListNode curr)
    {
        if (curr.Type != SkipListNode.NodeType.Data) return false;

        curr.Lock();
        if (curr.IsDeleted)
        {
            curr.Unlock();
            return false;
        }

        // Linearization point: IsDeleted is volatile.
        curr.IsDeleted = true;
        return true;
    }

    #endregion
    #region TryAdd Helper Methods

    /// <summary>
    /// Generates a random level for a new node based on the promotion probability.
    /// </summary>
    /// <returns>The generated level index, ranging from <see cref="BottomLevel"/> up to <see cref="_topLevel"/>.</returns>
    /// <remarks>
    /// The probability of generating level L is determined by <see cref="_promotionProbability"/>.
    /// A higher level means the node participates in more layers of the SkipList, improving search efficiency.
    /// </remarks>
    internal int GenerateLevel()
    {
        // Start at the bottom level (level 0)
        int level = BottomLevel;

        // Probabilistically increase the level
        // Continue promoting as long as we are below the max level and the random check passes
        while (level < _topLevel && _randomGenerator.NextDouble() <= _promotionProbability)
        {
            level++;
        }

        return level;
    }

    /// <summary>
    /// Validates that the insertion path is clear by locking predecessors and checking links.
    /// Called before <see cref="InsertNode"/> to ensure a safe insertion point.
    /// </summary>
    /// <param name="searchResult">The structural search result containing predecessors and successors for the insertion position.</param>
    /// <param name="insertLevel">The highest level index of the node being inserted.</param>
    /// <param name="highestLevelLocked">A reference parameter updated to track the highest level at which a predecessor lock was acquired during this validation attempt.</param>
    /// <returns><c>true</c> if the insertion path is valid (predecessors point to successors, neither is deleted) up to <paramref name="insertLevel"/>; <c>false</c> otherwise.</returns>
    /// <remarks>
    /// Locks predecessors from <see cref="BottomLevel"/> up to <paramref name="insertLevel"/>.
    /// The caller (typically <c>TryAdd</c>) is responsible for unlocking these predecessors using the final value of <paramref name="highestLevelLocked"/>, usually in a <c>finally</c> block.
    /// </remarks>
    internal static bool ValidateInsertion(SearchResult searchResult, int insertLevel, ref int highestLevelLocked)
    {
        bool isValid = true;

        // Iterate from bottom up to the insertion level
        for (int level = BottomLevel; isValid && level <= insertLevel; level++)
        {
            var predecessor = searchResult.GetPredecessor(level);
            var successor = searchResult.GetSuccessor(level);

            // Lock predecessor before checking
            predecessor.Lock();

            // Record the highest level locked so far
            Interlocked.Exchange(ref highestLevelLocked, level);

            // Check if predecessor is valid and still points to the expected successor
            // Also check if successor is marked as deleted (shouldn't insert before a deleted node)
            isValid = !predecessor.IsDeleted &&
                !successor.IsDeleted &&
                predecessor.GetNextNode(level) == successor;

            // If validation fails at this level, DO NOT unlock here; the loop condition stops
            // Caller's finally block handles unlocks based on highestLevelLocked
        }

        return isValid;
    }

    /// <summary>
    /// Physically links the <paramref name="newNode"/> into the SkipList structure at levels from <see cref="BottomLevel"/> up to <paramref name="insertLevel"/>.
    /// </summary>
    /// <param name="newNode">The new node to insert. Its internal `next` pointers should already be initialized to point to the correct successors.</param>
    /// <param name="searchResult">The structural search result containing the correct predecessors and successors (obtained from structural search).</param>
    /// <param name="insertLevel">The highest level index at which to insert the <paramref name="newNode"/>.</param>
    /// <remarks>
    /// This method assumes the relevant predecessors (up to <paramref name="insertLevel"/> as found in <paramref name="searchResult"/>) have already been locked by the caller (e.g., via <see cref="ValidateInsertion"/>).
    /// It includes <see cref="Thread.MemoryBarrier"/> calls to ensure correct memory ordering, making the node's internal state visible before it's linked and ensuring lower levels are linked before the top level for dependency reasons (e.g., physical removal logic).
    /// The caller is responsible for unlocking the predecessors after this method returns.
    /// </remarks>
    internal static void InsertNode(SkipListNode newNode, SearchResult searchResult, int insertLevel)
    {
        // Initialize all the next pointers of the new node first
        for (int level = BottomLevel; level <= insertLevel; level++)
        {
            newNode.SetNextNode(level, searchResult.GetSuccessor(level));
        }

        // Ensure that the newNode's internal state (its next pointers) is fully written and visible
        // before any external pointers (from predecessors) are set to point to it
        Thread.MemoryBarrier();

        // Link predecessors to the newNode, level by level
        for (int level = BottomLevel; level <= insertLevel; level++)
        {
            // Memory barrier before linking at the highest level ensures all lower-level links
            // are established first, satisfying potential dependencies for other operations like physical removal
            if (level == insertLevel)
            {
                Thread.MemoryBarrier();
            }

            // Update the predecessor's next pointer to link newNode into the list at this level
            searchResult.GetPredecessor(level).SetNextNode(level, newNode);
        }
    }

    #endregion
    #region TryDelete Helper Methods

    /// <summary>
    /// Validates that predecessors still correctly point to the candidate node after it has been logically deleted (and locked).
    /// Manages the lock state of the candidate node based on validation outcome and ensures predecessors are unlocked.
    /// </summary>
    /// <param name="candidateNode">The node that has been logically deleted and is currently locked. Its lock will be released by this method ONLY if validation fails.</param>
    /// <param name="preciseSearchResult">The structural search result obtained by searching for the <paramref name="candidateNode"/>, containing its predecessors.</param>
    /// <returns><c>true</c> if validation succeeds (predecessors are unlocked, <paramref name="candidateNode"/> remains locked); <c>false</c> if validation fails (<paramref name="candidateNode"/> and predecessors are unlocked).</returns>
    /// <remarks>
    /// This method is called during deletion operations (like TryDelete) after <c>LogicallyDeleteNode</c> succeeds.
    /// It coordinates the validation check and manages the lock state of the <paramref name="candidateNode"/> to signal whether the calling operation should proceed (return true) or retry (return false).
    /// </remarks>
    internal static bool TryValidateAfterLogicalDelete(SkipListNode candidateNode, SearchResult preciseSearchResult)
    {
        int highestLevelLocked = InvalidLevel;
        try
        {
            // Validate predecessors point to 'curr'. Locks predecessors.
            // Uses the node's actual top level for the loop bound.
            bool isValid = ValidateDeletion(
                candidateNode,
                preciseSearchResult,
                candidateNode.TopLevel,
                ref highestLevelLocked);

            if (isValid)
            {
                // Signal validation success
                return true;
            }

            // Validation failed. Unlock the candidate node as we need to retry.
            // ValidateDeletion should have unlocked the failing predecessor.
            candidateNode.Unlock();
            return false;
        }
        finally
        {
            // Always unlock predecessors locked during ValidateDeletion,
            // unless they were already unlocked because validation failed mid-way.
            for (int level = highestLevelLocked; level >= 0; level--)
            {
                preciseSearchResult?.GetPredecessor(level)?.Unlock();
            }
        }
    }

    /// <summary>
    /// Checks levels <see cref="BottomLevel"/> through <paramref name="topLevel"/> to ensure each predecessor is valid (not deleted)
    /// and still points to the target node <paramref name="curr"/>. Locks predecessors during the check.
    /// </summary>
    /// <param name="curr">The target node being validated (assumed logically deleted and locked by the caller of the caller).</param>
    /// <param name="searchResult">The structural search result containing the predecessors for <paramref name="curr"/>.</param>
    /// <param name="topLevel">The highest level index of the <paramref name="curr"/> node to validate.</param>
    /// <param name="highestLevelUnlocked">A reference parameter updated to track the highest level at which a predecessor lock was acquired during this validation attempt.</param>
    /// <returns><c>true</c> if all predecessors up to <paramref name="topLevel"/> are valid and point to <paramref name="curr"/>; <c>false</c> otherwise.</returns>
    /// <remarks>
    /// This method locks predecessors as it checks them. It updates <paramref name="highestLevelUnlocked"/> allowing the caller
    /// (typically within a <c>finally</c> block) to unlock these predecessors correctly.
    /// This method does *not* unlock the predecessors itself.
    /// </remarks>
    internal static bool ValidateDeletion(SkipListNode curr, SearchResult searchResult, int topLevel, ref int highestLevelUnlocked)
    {
        bool isValid = true;
        for (int level = BottomLevel; isValid && level <= topLevel; level++)
        {
            var predecessor = searchResult.GetPredecessor(level);
            predecessor.Lock();
            Interlocked.Exchange(ref highestLevelUnlocked, level);
            isValid = predecessor.IsDeleted is false && predecessor.GetNextNode(level) == curr;
        }

        return isValid;
    }

    #endregion
    #region Search Methods

    /// <summary>
    /// Performs a lock-free search restricted to Level 0 to find the first valid node matching the specified priority.
    /// </summary>
    /// <param name="priority">The priority to search for.</param>
    /// <returns>
    /// The first <see cref="SkipListNode"/> found at Level 0 that matches the <paramref name="priority"/>,
    /// is marked as <see cref="SkipListNode.IsInserted"/>, and is not marked as <see cref="SkipListNode.IsDeleted"/>.
    /// Returns a <c>null</c> object if no such node is found before reaching the tail node.
    /// </returns>
    /// <remarks>
    /// This search only operates on the bottom level (Level 0) and performs lock-free reads.
    /// It's used internally for operations like Update and ContainsPriority that need to find an existing,
    /// valid instance of a priority *without* requiring multi-level structural information.
    /// </remarks>
    internal SkipListNode? InlineSearch(TPriority priority)
    {
        ArgumentNullException.ThrowIfNull(priority, nameof(priority));

        SkipListNode? nodeToUpdate = null;
        SkipListNode current = _head.GetNextNode(BottomLevel);

        // Inline search for *first* valid node with desired priority
        while (current.Type is not SkipListNode.NodeType.Tail)
        {
            int comparison = current.CompareToPriority(priority);

            // Current priority is less than target, move forward
            if (comparison < 0)
            {
                current = current.GetNextNode(BottomLevel);
                continue;
            }

            // Current priority is greater than target, node not found
            if (comparison > 0) break;

            // Found a node with the target priority
            // Check if it's valid (inserted and not deleted)
            if (current is { IsInserted: true, IsDeleted: false })
            {
                nodeToUpdate = current;
                break;
            }

            // Node matches priority but is invalid/deleted, keep searching
            current = current.GetNextNode(BottomLevel);
        }

        return nodeToUpdate;
    }

    /// <summary>
    /// Performs a single probabilistic SprayList walk down the SkipList structure
    /// to find a candidate node near the minimum priority.
    /// </summary>
    /// <param name="currCount">The estimated current count of items in the queue (must be > 1), used to calculate spray parameters.</param>
    /// <returns>
    /// The <see cref="SkipListNode"/> where the spray walk landed. This node is typically near the minimum priority
    /// but is chosen probabilistically. It might be the <see cref="SkipListNode.NodeType.Head"/> node if the spray did not move significantly horizontally.
    /// Returns the actual head node reference if the walk does not move, does not return null in this version.
    /// </returns>
    /// <remarks>
    /// This method implements the core random walk logic of the SprayList algorithm, including parameter calculation,
    /// random horizontal jumps, and vertical descent. It performs lock-free reads during traversal.
    /// The returned node requires validation by the caller (e.g., check if it's a Data node, inserted, and not deleted)
    /// before being used in operations like deletion or sampling.
    /// </remarks>
    internal SkipListNode SpraySearch(int currCount)
    {
        // Init our parameters
        var sprayParams = SprayParameters.CalculateParameters(currCount, _topLevel, _offsetK, _offsetM);

        // Prepare spray operation
        int currHeight = sprayParams.StartHeight;
        var curr = _head;

        // Spray operation
        while (currHeight >= 0)
        {
            int currJumpLength = (sprayParams.MaxJumpLength > 0)
                ? _randomGenerator.Next(0, sprayParams.MaxJumpLength + 1)
                : 0;

            // Move forward horizontally
            for (int i = 0; i < currJumpLength; i++)
            {
                if (currHeight > curr.TopLevel) break;

                var next = curr.GetNextNode(currHeight);
                if (next.Type is SkipListNode.NodeType.Tail) break;

                curr = next;
            }

            // Descend D levels
            if (currHeight < sprayParams.DescentLength) break;
            currHeight -= sprayParams.DescentLength;
        }

        return curr;
    }

    /// <summary>
    /// Performs a lock-free search for the node with the specified priority.
    /// </summary>
    /// <param name="nodeToPosition">The node to search for.</param>
    /// <returns>A <see cref="SearchResult"/> instance containing the results of the search.</returns>
    /// <remarks>
    /// <para>
    /// If the priority is found, the <see cref="SearchResult.IsFound"/> property will be true
    /// and the <see cref="SearchResult.PredecessorArray"/> and <see cref="SearchResult.SuccessorArray"/>
    /// will contain the predecessor and successor nodes at each level.
    /// </para>
    /// <para>
    /// If the priority is not found, the <see cref="SearchResult.IsFound"/> property will be false
    /// and the <see cref="SearchResult.PredecessorArray"/> and <see cref="SearchResult.SuccessorArray"/>
    /// will contain the nodes that would have been the predecessor and successor of the node with the
    /// specified priority if it existed.
    /// </para>
    /// </remarks>
    internal SearchResult StructuralSearch(SkipListNode nodeToPosition)
    {
        int levelFound = InvalidLevel;
        SkipListNode[] predecessorArray = new SkipListNode[_numberOfLevels];
        SkipListNode[] successorArray = new SkipListNode[_numberOfLevels];

        SkipListNode predecessor = _head;
        for (int level = _topLevel; level >= 0; level--)
        {
            SkipListNode current = predecessor.GetNextNode(level);

            // Loop while current node is strictly less than the node we're positioning
            while (current.Type is not SkipListNode.NodeType.Tail &&
                   current.CompareTo(nodeToPosition) < 0)
            {
                predecessor = current;
                current = predecessor.GetNextNode(level);
            }

            // At this point, current >= nodeToPosition
            // Now we check if any node with the *same priority* was found at this level
            if (levelFound is InvalidLevel &&
                current.Type is not SkipListNode.NodeType.Tail &&
                current.CompareToPriority(nodeToPosition.Priority) == 0)
            {
                levelFound = level;
            }

            predecessorArray[level] = predecessor;
            successorArray[level] = current;
        }

        // The SearchResult contains predecessors/successors relative to the
        // exact position of nodeToPosition based on (Priority, SequenceNumber)
        return new SearchResult(levelFound, predecessorArray, successorArray);
    }

    #endregion
    #region Background Tasks

    /// <summary>
    /// Schedules the asynchronous physical removal (unlinking) of a logically deleted node from the SkipList.
    /// </summary>
    /// <param name="node">The node to be physically removed. Must already be marked as logically deleted (<see cref="SkipListNode.IsDeleted"/> must be true).</param>
    /// <param name="topLevel">Optional. The highest level index from which to start unlinking the node. If null, defaults to the node's actual <see cref="SkipListNode.TopLevel"/>.</param>
    /// <remarks>
    /// Submits a task to physically unlink the node from the top level down. Uses locking on predecessors for thread-safe pointer updates.
    /// </remarks>
    internal void SchedulePhysicalNodeRemoval(SkipListNode node, int? topLevel = null)
    {
        // We only remove logically deleted nodes
        if (!node.IsDeleted) return;

        try
        {
            _taskOrchestrator.Run(() =>
            {
                int startingLevel = topLevel ?? node.TopLevel;

                // Unlink top-to-bottom
                for (int level = startingLevel; level >= BottomLevel; level--)
                {
                    SkipListNode predecessor = _head;

                    // Loop to find the correct predecessor at this level
                    while (true)
                    {
                        SkipListNode current = predecessor.GetNextNode(level);

                        // Check if we found the potential position, overshot, or hit the end; stop searching this level
                        if (ShouldStopSearch(current, node)) break;

                        predecessor = current;
                    }

                    // Check if the node immediately after predecessor is our target node
                    if (predecessor.GetNextNode(level) != node)
                    {
                        continue;
                    }

                    // Lock the predecessor before checking and updating its pointer
                    predecessor.Lock();
                    try
                    {
                        // Re-verify condition *after* acquiring lock
                        // Also check if predecessor itself was deleted concurrently
                        if (!predecessor.IsDeleted && predecessor.GetNextNode(level) == node)
                        {
                            // Atomically (within lock) perform the pointer swing
                            predecessor.SetNextNode(level, node.GetNextNode(level));
                        }
                    }
                    finally
                    {
                        predecessor.Unlock();
                    }
                }

                return Task.CompletedTask;

                // Determine if we should stop searching based on the following criteria:
                // 1. We found the node to remove
                // 2. We reached the Tail node
                // 3. We passed the node (current node's priority/sequence is greater)
                static bool ShouldStopSearch(SkipListNode currentNode, SkipListNode nodeToRemove)
                {
                    return currentNode == nodeToRemove ||
                           currentNode.Type is SkipListNode.NodeType.Tail ||
                           currentNode.CompareTo(nodeToRemove) > 0;
                }
            });
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("queue full"))
        {
            /* Uncomment after Observability update
            _logger?.LogWarning(
                ex,
                "Physical node removal task dropped for node (Priority: {Priority}, Sequence: {SequenceNumber}) because the background task orchestrator queue is full. This may lead to increased memory usage over time if it occurs frequently.",
                node.Priority,
                node.SequenceNumber);
            */
        }
    }

    #endregion
    #region Internal Data Structures

    /// <summary>
    /// Represents the result of a structural search operation in the SkipList.
    /// </summary>
    /// <param name="LevelFound">The level at which the priority was found (or <see cref="NotFoundLevel"/> if not found).</param>
    /// <param name="PredecessorArray">An array of predecessor nodes at each level.</param>
    /// <param name="SuccessorArray">An array of successor nodes at each level.</param>
    internal sealed record SearchResult(int LevelFound, SkipListNode[] PredecessorArray, SkipListNode[] SuccessorArray)
    {
        /// <summary>
        /// Represents the level element when a priority is not found in the SkipList.
        /// </summary>
        private const int NotFoundLevel = -1;

        /// <summary>
        /// Gets an element indicating whether the priority was found in the SkipList.
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

    /// <summary>
    /// Holds the calculated parameters for a SprayList probabilistic DeleteMin operation.
    /// These parameters guide the random walk down the SkipList structure.
    /// </summary>
    internal readonly struct SprayParameters
    {
        /// <summary>
        /// The calculated starting height (h ≈ log n + K) for the spray walk.
        /// Starting high allows the walk to potentially land anywhere lower down.
        /// Value is clamped to list bounds and adjusted to be divisible by DescentLength.
        /// </summary>
        public readonly int StartHeight;

        /// <summary>
        /// The maximum random horizontal jump length (y ≈ M * (log n)^3) at each level of the spray.
        /// Designed to jump past the absolute minimum but likely land near the beginning of the list.
        /// </summary>
        public readonly int MaxJumpLength;

        /// <summary>
        /// The number of levels to descend (d ≈ log * log n) after each horizontal jump phase.
        /// A gradual descent helps distribute landing points near the list's start.
        /// Value is at least 1.
        /// </summary>
        public readonly int DescentLength;

        /// <summary>
        /// Initializes a new instance of the <see cref="SprayParameters"/> struct.
        /// </summary>
        /// <param name="startHeight">The calculated start height.</param>
        /// <param name="maxJumpLength">The calculated maximum jump length.</param>
        /// <param name="descentLength">The calculated descent length.</param>
        public SprayParameters(int startHeight, int maxJumpLength, int descentLength)
        {
            StartHeight = startHeight;
            MaxJumpLength = maxJumpLength;
            DescentLength = descentLength;
        }

        /// <summary>
        /// Calculates the parameters needed for the SprayList probabilistic DeleteMin operation
        /// based on the current list state and tuning constants.
        /// </summary>
        /// <param name="currentCount">The currently estimated count of items in the queue (must be > 1).</param>
        /// <param name="topLevel">The maximum level index allowed in the SkipList.</param>
        /// <param name="offsetK">A tuning constant (K) added to the height calculation.</param>
        /// <param name="offsetM">A tuning constant (M) multiplied by the jump length calculation.</param>
        /// <returns>A SprayParameters struct containing the calculated height, max jump length, and descent length.</returns>
        public static SprayParameters CalculateParameters(int currentCount, int topLevel, int offsetK, int offsetM)
        {
            // Base calculations (assuming currentCount > 1)
            double logN = Math.Log(currentCount);

            // Ensure inner Log argument is >= 1 for LogLogN calculation
            double logLogN = Math.Log(Math.Max(1.0, logN));

            // Calculate initial parameters
            int h = (int)logN + offsetK;
            int y = offsetM * (int)Math.Pow(Math.Max(1.0, logN), 3);
            int d = Math.Max(1, (int)logLogN);

            // Clamp height to list bounds and ensure non-negative
            int startHeight = Math.Min(topLevel, Math.Max(0, h));

            // Ensure startHeight is divisible by descentLength for clean loop steps
            if (startHeight >= d && startHeight % d != 0)
            {
                startHeight = d * (startHeight / d);
            }
            else if (startHeight < d)
            {
                 startHeight = Math.Max(0, startHeight);
            }

            // Ensure jump length is non-negative
            int maxJumpLength = Math.Max(0, y);

            return new SprayParameters(startHeight, maxJumpLength, d);
        }
    }

    /// <summary>
    /// Represents a node in the SkipList.
    /// </summary>
    [SuppressMessage("ReSharper", "StaticMemberInGenericType", Justification = "Accessed by reference.")]
    internal sealed class SkipListNode
    {
        private readonly Lock _nodeLock = new();
        private readonly SkipListNode[] _nextNodeArray;
        private readonly IComparer<TPriority> _priorityComparer;
        private static long s_sequenceGenerator;

        // Volatile fields have 'release' and 'acquire' semantics
        // which act as one-way memory barriers; combining these properties
        // with locks provides sufficient memory ordering guarantees
        private volatile bool _isInserted;
        private volatile bool _isDeleted;

        /// <summary>
        /// Initializes a new instance of the <see cref="SkipListNode"/> class for Head/Tail nodes.
        /// </summary>
        /// <param name="nodeType">The type of the node.</param>
        /// <param name="height">The height (level) of the node.</param>
        public SkipListNode(NodeType nodeType, int height)
        {
            Priority = default!;
            Element = default!;
            Type = nodeType;
            _nextNodeArray = new SkipListNode[height + 1];
            _priorityComparer = Comparer<TPriority>.Default;
            SequenceNumber = nodeType switch
            {
                NodeType.Head => long.MinValue,
                NodeType.Tail => long.MaxValue,
                _ => -1
            };
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="SkipListNode"/> class for Data nodes.
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
            _nextNodeArray = new SkipListNode[height + 1];
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
        public TPriority Priority { get; }

        /// <summary>
        /// Gets or sets the element associated with the node.
        /// </summary>
        public TElement Element { get; set; }

        /// <summary>
        /// Unique identifier for this node instance. Readonly after construction.
        /// </summary>
        public long SequenceNumber { get; }

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
        public SkipListNode GetNextNode(int height) => _nextNodeArray[height];

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
        public int CompareTo(SkipListNode other)
        {
            // Head is always less, anything is less than Tail
            if (this.Type is NodeType.Head || other.Type is NodeType.Tail) return -1;

            // Tail is always greater, anything is greater than Head
            if (this.Type is NodeType.Tail || other.Type is NodeType.Head) return 1;

            // Compare priorities using the comparer for TPriority
            int priorityComparison = _priorityComparer.Compare(this.Priority, other.Priority);

            // Priorities are equal, compare by sequence number (lower sequence number is considered "smaller")
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

    #endregion
}