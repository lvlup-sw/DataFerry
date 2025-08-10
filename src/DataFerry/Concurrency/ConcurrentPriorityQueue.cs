// ===========================================================================
// <copyright file="ConcurrentPriorityQueue.cs" company="Level Up Software">
// Copyright (c) Level Up Software. All rights reserved.
// </copyright>
// ===========================================================================

using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Metrics;
using lvlup.DataFerry.Concurrency.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ObjectPool;
using Microsoft.Extensions.Options;

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
/// <item>Expected O(log n) time complexity for <c>TryAdd</c>, <c>Update</c>, <c>TryRemove</c>, <c>TryDeleteMin</c>, and <c>TryDeleteAbsoluteMin</c> operations.</item>
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
    public const int DefaultMaxSize = ConcurrentPriorityQueueOptions.DefaultMaxSize;

    /// <summary>
    /// Default promotion chance for each level. [0, 1).
    /// </summary>
    public const double DefaultPromotionProbability = ConcurrentPriorityQueueOptions.DefaultPromotionProbability;

    /// <summary>
    /// The default tuning constant (K) added to the height used in the calculation during the Spray operation within <see cref="TryDeleteMin"/>.
    /// </summary>
    /// <remarks>
    /// Increasing this value starts the spray slightly higher, potentially giving it more room to spread. This does not typically need to be adjusted.
    /// </remarks>
    public const int DefaultSprayOffsetK = ConcurrentPriorityQueueOptions.DefaultSprayOffsetK;

    /// <summary>
    /// The default tuning constant (M) multiplied by the jump length used in the calculation during the Spray operation within <see cref="TryDeleteMin"/>.
    /// </summary>
    /// <remarks>
    /// This value directly scales the maximum jump length. This should be adjusted in the case where reducing contention is more desirable than deleting a node with a strictly higher priority.
    /// </remarks>
    public const int DefaultSprayOffsetM = ConcurrentPriorityQueueOptions.DefaultSprayOffsetM;

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
    private readonly SkipListNode<TPriority, TElement> _head;

    /// <summary>
    /// Tail of the skip list.
    /// </summary>
    private readonly SkipListNode<TPriority, TElement> _tail;

    /// <summary>
    /// Random number generator.
    /// </summary>
    private readonly Random _randomGenerator = Random.Shared;

    /// <summary>
    /// Priority comparer used to order the priorities.
    /// </summary>
    private readonly IComparer<TPriority> _comparer;

    /// <summary>
    /// Background task processor which handles node removal.
    /// </summary>
    private readonly ITaskOrchestrator _taskOrchestrator;

        /// <summary>
    /// Observability helper class which handles instrumentation.
    /// </summary>
    private readonly ConcurrentPriorityQueueObservabilityHelper<TPriority, TElement> _observability;

    /// <summary>
    /// Object pool for reusing SkipListNode instances.
    /// </summary>
    private readonly ObjectPool<SkipListNode<TPriority, TElement>> _nodePool;

    /// <summary>
    /// Array pool for reusing arrays in SearchResult.
    /// </summary>
    private readonly ArrayPool<SkipListNode<TPriority, TElement>> _arrayPool = ArrayPool<SkipListNode<TPriority, TElement>>.Shared;

    #endregion
    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="ConcurrentPriorityQueue{TPriority, TElement}"/> class.
    /// </summary>
    /// <param name="taskOrchestrator">The scheduler used to orchestrate background tasks processing the physical deletion of nodes.</param>
    /// <param name="comparer">The comparer used to compare priorities. This will be used for all priority comparisons within the queue.</param>
    /// <param name="loggerFactory">The logger factory for creating loggers.</param>
    /// <param name="meterFactory">The meter factory for creating metrics.</param>
    /// <param name="nodePool">The object pool for reusing SkipListNode instances.</param>
    /// <param name="options">The configuration options for the queue.</param>
    /// <exception cref="ArgumentNullException">Thrown if any required parameter is null.</exception>
    /// <remarks>
    /// This constructor sets up the essential components of the concurrent priority queue,
    /// including the head/tail sentinel nodes, level calculation, and tuning parameters.
    /// </remarks>
    internal ConcurrentPriorityQueue(
        ITaskOrchestrator taskOrchestrator,
        IComparer<TPriority> comparer,
        ILoggerFactory loggerFactory,
        IMeterFactory meterFactory,
        ObjectPool<SkipListNode<TPriority, TElement>> nodePool,
        IOptions<ConcurrentPriorityQueueOptions> options)
    {
        ArgumentNullException.ThrowIfNull(taskOrchestrator, nameof(taskOrchestrator));
        ArgumentNullException.ThrowIfNull(comparer, nameof(comparer));
        ArgumentNullException.ThrowIfNull(loggerFactory, nameof(loggerFactory));
        ArgumentNullException.ThrowIfNull(meterFactory, nameof(meterFactory));
        ArgumentNullException.ThrowIfNull(nodePool, nameof(nodePool));
        ArgumentNullException.ThrowIfNull(options, nameof(options));

        var opts = options.Value;
        _taskOrchestrator = taskOrchestrator;
        _comparer = comparer;
        _nodePool = nodePool;
        _maxSize = opts.MaxSize;
        _offsetK = opts.SprayOffsetK;
        _offsetM = opts.SprayOffsetM;
        _promotionProbability = opts.PromotionProbability;
        _numberOfLevels = CalculateOptimalLevels(_maxSize, _promotionProbability);
        _topLevel = _numberOfLevels - 1;
        _count = 0;

        _observability = new ConcurrentPriorityQueueObservabilityHelper<TPriority, TElement>(
            loggerFactory,
            meterFactory,
            _maxSize,
            GetCount);

        _head = new SkipListNode<TPriority, TElement>(SkipListNode<TPriority, TElement>.NodeType.Head, _topLevel);
        _tail = new SkipListNode<TPriority, TElement>(SkipListNode<TPriority, TElement>.NodeType.Tail, _topLevel);

        // Link head to tail at all levels
        for (int level = BottomLevel; level <= _topLevel; level++)
        {
            _head.SetNextNode(level, _tail);
        }

        _head.IsInserted = true;
        _tail.IsInserted = true;

        _observability.LogQueueInitialized(_maxSize, _numberOfLevels, _promotionProbability, _offsetK, _offsetM);
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

    /// <summary>
    /// Initializes a new instance of the <see cref="ConcurrentPriorityQueue{TPriority, TElement}"/> class with a default ObjectPool.
    /// </summary>
    /// <param name="taskOrchestrator">The scheduler used to orchestrate background tasks processing the physical deletion of nodes.</param>
    /// <param name="comparer">The comparer used to compare priorities. This will be used for all priority comparisons within the queue.</param>
    /// <param name="loggerFactory">The logger factory for creating loggers.</param>
    /// <param name="meterFactory">The meter factory for creating metrics.</param>
    /// <param name="options">The configuration options for the queue.</param>
    public ConcurrentPriorityQueue(
        ITaskOrchestrator taskOrchestrator,
        IComparer<TPriority> comparer,
        ILoggerFactory loggerFactory,
        IMeterFactory meterFactory,
        IOptions<ConcurrentPriorityQueueOptions> options)
        : this(taskOrchestrator, comparer, loggerFactory, meterFactory,
               new DefaultObjectPool<SkipListNode<TPriority, TElement>>(
                   new SkipListNodePooledObjectPolicy<TPriority, TElement>()),
               options)
    {
    }

    #endregion
    #region Core Operations

    /// <inheritdoc/>
    public bool TryAdd(TPriority priority, TElement element)
    {
        ArgumentNullException.ThrowIfNull(priority, nameof(priority));
        ArgumentNullException.ThrowIfNull(element, nameof(element));

        (Activity? activity, Stopwatch stopwatch) = _observability.StartOperation(nameof(TryAdd), priority: priority);

        using (activity)
        {
            try
            {
                // Generate a new level and get a node from the pool
                int insertLevel = GenerateLevel();
                var newNode = _nodePool.Get();
                newNode.Reinitialize(priority, element, insertLevel, _comparer);

                while (true)
                {
                    // Identify the links the new level and node will have
                    using var searchResult = StructuralSearch(newNode);
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

                        int newCount = Interlocked.Increment(ref _count);

                        // If we exceed the size bound, delete the minimal node
                        if (newCount > _maxSize && _maxSize is not int.MaxValue)
                        {
                            _observability.LogMaxSizeExceeded(newCount);

                            // We use our SprayList algorithm to avoid contention
                            if (!TryDeleteMin(out _))
                            {
                                // If the compensatory delete failed
                                // we need to decrement the count
                                Interlocked.Decrement(ref _count);
                            }
                        }

                        _observability.RecordAddSuccess(priority, newCount, stopwatch.ElapsedMilliseconds);
                        activity?.SetStatus(ActivityStatusCode.Ok);
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
            catch (Exception ex)
            {
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                _observability.RecordAddFailure(priority, stopwatch.ElapsedMilliseconds);
                throw;
            }
        }
    }

    /// <inheritdoc/>
    public bool TryPeekMin([MaybeNullWhen(false)] out TPriority priority, [MaybeNullWhen(false)] out TElement element)
    {
        (Activity? activity, _) = _observability.StartOperation(nameof(TryPeekMin));

        using (activity)
        {
            try
            {
                SkipListNode<TPriority, TElement> curr = _head.GetNextNode(BottomLevel);
                priority = default!;
                element = default!;

                // Traverse the bottom level to find the first valid (not logically deleted) node
                while (curr.Type is not SkipListNode<TPriority, TElement>.NodeType.Tail)
                {
                    // Check if the node is valid (inserted and not deleted)
                    if (curr is { IsInserted: true, IsDeleted: false })
                    {
                        // Found a valid node - this is the minimum since we're traversing in order
                        priority = curr.Priority;
                        element = curr.Element;
                        activity?.SetStatus(ActivityStatusCode.Ok);
                        return true;
                    }

                    curr = curr.GetNextNode(BottomLevel);
                }

                return false;
            }
            catch (Exception ex)
            {
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                throw;
            }
        }
    }

    /// <inheritdoc/>
    public bool TryRemove(TPriority priority, [MaybeNullWhen(false)] out TElement element)
    {
        ArgumentNullException.ThrowIfNull(priority, nameof(priority));
        element = default!;

        (Activity? activity, Stopwatch stopwatch) = _observability.StartOperation(nameof(TryRemove), priority: priority);

        using (activity)
        {
            try
            {
                // Locate the node to delete
                var nodeToDelete = InlineSearch(priority);

                // Check if a candidate was found
                if (nodeToDelete is null)
                {
                    _observability.RecordDeleteFailure("specific", stopwatch.ElapsedMilliseconds);
                    return false;
                }

                while (true)
                {
                    using var searchResult = StructuralSearch(nodeToDelete);

                    // Ensure validity before deletion
                    if (NodeIsInvalidOrDeleted(nodeToDelete))
                    {
                        _observability.RecordDeleteFailure("specific", stopwatch.ElapsedMilliseconds);
                        return false;
                    }

                    // Attempt logical delete
                    if (!LogicallyDeleteNode(nodeToDelete)) return false;

                    // Validate that the logical deletion succeeded
                    // and that the state of predecessors is correct
                    bool isValid = TryValidateAfterLogicalDelete(nodeToDelete, searchResult);

                    // If this is not the case, retry
                    if (!isValid) continue;

                    // Success path; extract element & schedule node removal
                    try
                    {
                        element = nodeToDelete.Element;
                        SchedulePhysicalNodeRemoval(nodeToDelete, nodeToDelete.TopLevel);
                        int newCount = Interlocked.Decrement(ref _count);
                        _observability.RecordDeleteSuccess("specific", newCount, stopwatch.ElapsedMilliseconds);
                        activity?.SetStatus(ActivityStatusCode.Ok);
                        return true;
                    }
                    finally
                    {
                        nodeToDelete.Unlock();
                    }
                }
            }
            catch (Exception ex)
            {
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                _observability.RecordDeleteFailure("specific", stopwatch.ElapsedMilliseconds);
                throw;
            }
        }
    }

    /// <inheritdoc/>
    public bool TryDeleteAbsoluteMin(out TElement element)
    {
        (Activity? activity, Stopwatch stopwatch) = _observability.StartOperation(nameof(TryDeleteAbsoluteMin));

        using (activity)
        {
            try
            {
                SkipListNode<TPriority, TElement> curr = _head.GetNextNode(BottomLevel);
                element = default!;

                while (curr.Type is not SkipListNode<TPriority, TElement>.NodeType.Tail)
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
                            int newCount = Interlocked.Decrement(ref _count);

                            _observability.RecordDeleteSuccess("absolute_min", newCount, stopwatch.ElapsedMilliseconds);
                            activity?.SetStatus(ActivityStatusCode.Ok);
                            return true;
                        }
                        finally
                        {
                            curr.Unlock();
                        }
                    }

                    curr = curr.GetNextNode(BottomLevel);
                }

                _observability.RecordDeleteFailure("absolute_min", stopwatch.ElapsedMilliseconds);
                return false;
            }
            catch (Exception ex)
            {
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                _observability.RecordDeleteFailure("absolute_min", stopwatch.ElapsedMilliseconds);
                throw;
            }
        }
    }

    /// <inheritdoc/>
    public bool TryDeleteMin(out TElement element, double retryProbability = 1.0)
    {
        (Activity? activity, Stopwatch stopwatch) = _observability.StartOperation(nameof(TryDeleteMin));

        using (activity)
        {
            try
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
                    bool shouldRetry = _randomGenerator.NextDouble() < retryProbability &&
                        TryDeleteMin(out element, retryProbability * 0.5);

                    if (!shouldRetry)
                    {
                        _observability.RecordDeleteFailure("min", stopwatch.ElapsedMilliseconds);
                    }

                    return shouldRetry;
                }

                try
                {
                    // Success path
                    SchedulePhysicalNodeRemoval(curr);
                    element = curr.Element;
                    int newCount = Interlocked.Decrement(ref _count);

                    _observability.RecordDeleteSuccess("min", newCount, stopwatch.ElapsedMilliseconds);
                    activity?.SetStatus(ActivityStatusCode.Ok);
                    return true;
                }
                finally
                {
                    curr.Unlock();
                }
            }
            catch (Exception ex)
            {
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                _observability.RecordDeleteFailure("min", stopwatch.ElapsedMilliseconds);
                throw;
            }
        }
    }

    /// <inheritdoc/>
    public IEnumerable<(TPriority priority, TElement element)> SampleNearMin(int sampleSize, int maxAttemptsMultiplier = 3)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sampleSize, nameof(sampleSize));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxAttemptsMultiplier, nameof(maxAttemptsMultiplier));

        (Activity? activity, _) = _observability.StartOperation(nameof(SampleNearMin), tags:
        [
            new KeyValuePair<string, object?>("sampleSize", sampleSize),
            new KeyValuePair<string, object?>("maxAttemptsMultiplier", maxAttemptsMultiplier)
        ]);

        using (activity)
        {
            try
            {
                // If count is too small, return empty list
                int currCount = GetCount();
                if (currCount <= sampleSize)
                {
                    activity?.SetStatus(ActivityStatusCode.Ok);
                    return [];
                }

                // Use PLINQ to perform the sprays in parallel
                var result = Enumerable.Range(0, sampleSize * maxAttemptsMultiplier)
                    .AsParallel()
                    .Select(_ => SpraySearch(currCount))
                    .Where(node => !NodeIsInvalidOrDeleted(node))
                    .Distinct()
                    .Take(sampleSize)
                    .Select(node => (node.Priority, node.Element))
                    .ToList();

                activity?.SetStatus(ActivityStatusCode.Ok);
                activity?.SetTag("resultCount", result.Count);
                return result;
            }
            catch (Exception ex)
            {
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                throw;
            }
        }
    }

    /// <inheritdoc/>
    public bool Update(TPriority priority, TElement element)
    {
        ArgumentNullException.ThrowIfNull(priority, nameof(priority));
        ArgumentNullException.ThrowIfNull(element, nameof(element));

        (Activity? activity, Stopwatch stopwatch) = _observability.StartOperation(nameof(Update), priority: priority);

        using (activity)
        {
            try
            {
                // Locate the node to update
                var nodeToUpdate = InlineSearch(priority);

                // Check if a candidate was found
                if (nodeToUpdate is null)
                {
                    _observability.RecordUpdateFailure(stopwatch.ElapsedMilliseconds);
                    return false;
                }

                // Try to perform the update
                nodeToUpdate.Lock();
                try
                {
                    // Check to see if the node was deleted
                    // between the search and acquiring the lock
                    if (nodeToUpdate.IsDeleted)
                    {
                        // Candidate node was deleted while trying to update
                        _observability.RecordUpdateFailure(stopwatch.ElapsedMilliseconds);
                        return false;
                    }

                    // Perform the update
                    nodeToUpdate.Element = element;
                    _observability.RecordUpdateSuccess(GetCount(), stopwatch.ElapsedMilliseconds);
                    activity?.SetStatus(ActivityStatusCode.Ok);
                    return true;
                }
                finally
                {
                    nodeToUpdate.Unlock();
                }
            }
            catch (Exception ex)
            {
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                _observability.RecordUpdateFailure(stopwatch.ElapsedMilliseconds);
                throw;
            }
        }
    }

    /// <inheritdoc/>
    public bool Update(TPriority priority, Func<TPriority, TElement, TElement> updateFunction)
    {
        ArgumentNullException.ThrowIfNull(priority, nameof(priority));
        ArgumentNullException.ThrowIfNull(updateFunction, nameof(updateFunction));

        (Activity? activity, Stopwatch stopwatch) = _observability.StartOperation(nameof(Update) + "WithFunction", priority: priority);

        using (activity)
        {
            try
            {
                // Locate the node to update
                var nodeToUpdate = InlineSearch(priority);

                // Check if a candidate was found
                if (nodeToUpdate is null)
                {
                    _observability.RecordUpdateFailure(stopwatch.ElapsedMilliseconds);
                    return false;
                }

                // Try to perform the update
                nodeToUpdate.Lock();
                try
                {
                    // Check to see if the node was deleted
                    // between the search and acquiring the lock
                    if (nodeToUpdate.IsDeleted)
                    {
                        // Candidate node was deleted while trying to update
                        _observability.RecordUpdateFailure(stopwatch.ElapsedMilliseconds);
                        return false;
                    }

                    // Perform the update using the function
                    nodeToUpdate.Element = updateFunction(priority, nodeToUpdate.Element);
                    _observability.RecordUpdateSuccess(GetCount(), stopwatch.ElapsedMilliseconds);
                    activity?.SetStatus(ActivityStatusCode.Ok);
                    return true;
                }
                finally
                {
                    nodeToUpdate.Unlock();
                }
            }
            catch (Exception ex)
            {
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                _observability.RecordUpdateFailure(stopwatch.ElapsedMilliseconds);
                throw;
            }
        }
    }

    /// <inheritdoc/>
    public bool TryPeek(TPriority priority, [MaybeNullWhen(false)] out TElement element)
    {
        ArgumentNullException.ThrowIfNull(priority, nameof(priority));
        element = default!;

        (Activity? activity, Stopwatch stopwatch) = _observability.StartOperation(nameof(TryPeek), priority: priority);

        using (activity)
        {
            try
            {
                // Use InlineSearch for lock-free read
                var node = InlineSearch(priority);

                if (node is null)
                {
                    _observability.RecordPeekFailure(stopwatch.ElapsedMilliseconds);
                    activity?.SetStatus(ActivityStatusCode.Ok);
                    activity?.SetTag("found", false);
                    return false;
                }

                element = node.Element;
                _observability.RecordPeekSuccess(stopwatch.ElapsedMilliseconds);
                activity?.SetStatus(ActivityStatusCode.Ok);
                activity?.SetTag("found", true);
                return true;
            }
            catch (Exception ex)
            {
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                _observability.RecordPeekFailure(stopwatch.ElapsedMilliseconds);
                throw;
            }
        }
    }

    /// <inheritdoc/>
    public bool ContainsPriority(TPriority priority)
    {
        ArgumentNullException.ThrowIfNull(priority, nameof(priority));

        (Activity? activity, _) = _observability.StartOperation(nameof(ContainsPriority), priority: priority);

        using (activity)
        {
            try
            {
                // Locate the node with the desired priority
                var foundNode = InlineSearch(priority);
                bool found = foundNode is not null;
                activity?.SetStatus(ActivityStatusCode.Ok);
                activity?.SetTag("found", found);
                return found;
            }
            catch (Exception ex)
            {
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                throw;
            }
        }
    }

    /// <inheritdoc/>
    public int GetCount() => Interlocked.CompareExchange(ref _count, 0, 0);

    /// <inheritdoc/>
    public IEnumerator<TPriority> GetEnumerator()
    {
        SkipListNode<TPriority, TElement> curr = _head;
        while (true)
        {
            curr = curr.GetNextNode(BottomLevel);

            // If current is tail, this must be the end of the list.
            if (curr.Type is SkipListNode<TPriority, TElement>.NodeType.Tail) yield break;

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
    /// <c>true</c> if the node is null, a Tail node, not yet fully inserted (<see cref="SkipListNode{TPriority, TElement}.IsInserted"/> is false),
    /// or already marked as deleted (<see cref="SkipListNode{TPriority, TElement}.IsDeleted"/> is true); otherwise, <c>false</c>.
    /// </returns>
    /// <remarks>
    /// This is typically used as a preliminary check before attempting an operation like locking or deletion on a node.
    /// It performs lock-free reads of the node's properties (including volatile fields).
    /// </remarks>
    internal static bool NodeIsInvalidOrDeleted(SkipListNode<TPriority, TElement>? curr)
    {
        return curr?.Type is not SkipListNode<TPriority, TElement>.NodeType.Data
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
    /// This method acquires the node's lock (<see cref="SkipListNode{TPriority, TElement}.Lock"/>).
    /// If successful (returns <c>true</c>), the node's <see cref="SkipListNode{TPriority, TElement}.IsDeleted"/> flag is set,
    /// and the caller is responsible for eventually unlocking the node after subsequent operations
    /// (e.g., validation, scheduling physical removal).
    /// If it fails (returns <c>false</c>), the node is unlocked internally before returning.
    /// The write to the volatile <c>IsDeleted</c> field acts as the linearization point for the logical deletion.
    /// </remarks>
    internal static bool LogicallyDeleteNode(SkipListNode<TPriority, TElement> curr)
    {
        if (curr.Type is not SkipListNode<TPriority, TElement>.NodeType.Data) return false;

        // If we failed to acquire the lock immediately, there is likely contention
        // We return false to signal retry is needed by caller
        if (!curr.TryEnter()) return false;

        // Lock was acquired successfully
        bool markedDeleted = false;
        try
        {
            if (curr.IsDeleted) return false;

            // Linearization point: IsDeleted is volatile.
            curr.IsDeleted = true;
            markedDeleted = true;

            // Return true; Lock remains held for the caller until finally block
            return true;
        }
        finally
        {
            // If the lock was acquired (we passed the TryEnter check)
            // AND we are exiting this scope *without* having successfully marked
            // the node deleted, then we must release the lock
            if (!markedDeleted) curr.Unlock();
        }
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
    internal static bool ValidateInsertion(SearchResult<TPriority, TElement> searchResult, int insertLevel, ref int highestLevelLocked)
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
            isValid = !predecessor.IsDeleted
                && !successor.IsDeleted
                && predecessor.GetNextNode(level) == successor;

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
    internal static void InsertNode(SkipListNode<TPriority, TElement> newNode, SearchResult<TPriority, TElement> searchResult, int insertLevel)
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
    #region TryRemove Helper Methods

    /// <summary>
    /// Validates that predecessors still correctly point to the candidate node after it has been logically deleted (and locked).
    /// Manages the lock state of the candidate node based on validation outcome and ensures predecessors are unlocked.
    /// </summary>
    /// <param name="candidateNode">The node that has been logically deleted and is currently locked. Its lock will be released by this method ONLY if validation fails.</param>
    /// <param name="preciseSearchResult">The structural search result obtained by searching for the <paramref name="candidateNode"/>, containing its predecessors.</param>
    /// <returns><c>true</c> if validation succeeds (predecessors are unlocked, <paramref name="candidateNode"/> remains locked); <c>false</c> if validation fails (<paramref name="candidateNode"/> and predecessors are unlocked).</returns>
    /// <remarks>
    /// This method is called during deletion operations (like TryRemove) after <c>LogicallyDeleteNode</c> succeeds.
    /// It coordinates the validation check and manages the lock state of the <paramref name="candidateNode"/> to signal whether the calling operation should proceed (return true) or retry (return false).
    /// </remarks>
    internal static bool TryValidateAfterLogicalDelete(SkipListNode<TPriority, TElement> candidateNode, SearchResult<TPriority, TElement> preciseSearchResult)
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
    internal static bool ValidateDeletion(SkipListNode<TPriority, TElement> curr, SearchResult<TPriority, TElement> searchResult, int topLevel, ref int highestLevelUnlocked)
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
    /// The first <see cref="SkipListNode{TPriority, TElement}"/> found at Level 0 that matches the <paramref name="priority"/>,
    /// is marked as <see cref="SkipListNode{TPriority, TElement}.IsInserted"/>, and is not marked as <see cref="SkipListNode{TPriority, TElement}.IsDeleted"/>.
    /// Returns a <c>null</c> object if no such node is found before reaching the tail node.
    /// </returns>
    /// <remarks>
    /// This search only operates on the bottom level (Level 0) and performs lock-free reads.
    /// It's used internally for operations like Update and ContainsPriority that need to find an existing,
    /// valid instance of a priority *without* requiring multi-level structural information.
    /// </remarks>
    internal SkipListNode<TPriority, TElement>? InlineSearch(TPriority priority)
    {
        ArgumentNullException.ThrowIfNull(priority, nameof(priority));

        SkipListNode<TPriority, TElement>? nodeToUpdate = null;
        SkipListNode<TPriority, TElement> current = _head.GetNextNode(BottomLevel);

        // Inline search for *first* valid node with desired priority
        while (current.Type is not SkipListNode<TPriority, TElement>.NodeType.Tail)
        {
            int comparison = current.CompareToPriority(priority);

            // Current priority is less than target, move forward
            if (comparison < 0)
            {
                current = current.GetNextNode(BottomLevel);
                continue;
            }

            // Current priority is greater than target, node not found
            if (comparison > 0)
            {
                break;
            }

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
    /// The <see cref="SkipListNode{TPriority, TElement}"/> where the spray walk landed. This node is typically near the minimum priority
    /// but is chosen probabilistically. It might be the <see cref="SkipListNode{TPriority, TElement}.NodeType.Head"/> node if the spray did not move significantly horizontally.
    /// Returns the actual head node reference if the walk does not move, does not return null in this version.
    /// </returns>
    /// <remarks>
    /// This method implements the core random walk logic of the SprayList algorithm, including parameter calculation,
    /// random horizontal jumps, and vertical descent. It performs lock-free reads during traversal.
    /// The returned node requires validation by the caller (e.g., check if it's a Data node, inserted, and not deleted)
    /// before being used in operations like deletion or sampling.
    /// </remarks>
    internal SkipListNode<TPriority, TElement> SpraySearch(int currCount)
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
                if (next.Type is SkipListNode<TPriority, TElement>.NodeType.Tail) break;

                curr = next;
            }

            // Descend D levels
            if (currHeight < sprayParams.DescentLength) break;
            currHeight -= sprayParams.DescentLength;
        }

        return curr;
    }

    /// <summary>
    /// Performs a lock-free search for the node with the specified priority and sequence number.
    /// </summary>
    /// <param name="nodeToPosition">The node instance (with specific priority and sequence number) to search for.</param>
    /// <returns>A <see cref="SearchResult{TPriority, TElement}"/> instance containing the results of the search.</returns>
    /// <remarks>
    /// <para>
    /// If the exact node instance (based on priority and sequence number) is found,
    /// the <see cref="SearchResult{TPriority, TElement}.IsFound"/> property will be true, <see cref="SearchResult{TPriority, TElement}.LevelFound"/>
    /// will indicate the highest level it was found at, and the predecessor and successor arrays
    /// will contain the nodes immediately before and after it
    /// at each level.
    /// </para>
    /// <para>
    /// If the exact node instance is not found, the <see cref="SearchResult{TPriority, TElement}.IsFound"/> property will be false
    /// (<see cref="SearchResult{TPriority, TElement}.LevelFound"/> will be -1) and the arrays will contain the nodes that
    /// bracket the position where the node *would* be inserted based on its priority and sequence number.
    /// </para>
    /// </remarks>
    internal SearchResult<TPriority, TElement> StructuralSearch(SkipListNode<TPriority, TElement> nodeToPosition)
    {
        // Set up the values used for the search
        int levelFound = InvalidLevel;
        SkipListNode<TPriority, TElement>[] predecessorArray = _arrayPool.Rent(_numberOfLevels);
        SkipListNode<TPriority, TElement>[] successorArray = _arrayPool.Rent(_numberOfLevels);
        SkipListNode<TPriority, TElement> predecessor = _head;
        SkipListNode<TPriority, TElement>? nodeFound = null;

        for (int level = _topLevel; level >= 0; level--)
        {
            SkipListNode<TPriority, TElement> current = predecessor.GetNextNode(level);

            // Loop while current node is strictly less than the node we're positioning
            while (current.Type is not SkipListNode<TPriority, TElement>.NodeType.Tail &&
                   current.CompareTo(nodeToPosition) < 0)
            {
                predecessor = current;
                current = predecessor.GetNextNode(level);
            }

            // At this point, current >= nodeToPosition
            predecessorArray[level] = predecessor;

            // Determine the correct successor for this level
            if (current.Type is not SkipListNode<TPriority, TElement>.NodeType.Tail &&
                current.CompareTo(nodeToPosition) == 0)
            {
                // The actual successor is the node *after* current
                successorArray[level] = current.GetNextNode(level);
                nodeFound ??= current;

                // Update levelFound only if we found the exact node and haven't found it before
                if (levelFound == InvalidLevel) levelFound = level;
            }
            else
            {
                // We did not land exactly on the nodeToPosition.
                // This means 'current' is the first node *greater* than nodeToPosition,
                // so 'current' IS the correct successor
                successorArray[level] = current;
            }
        }

        // The SearchResult contains predecessors/successors relative to the
        // exact position of nodeToPosition based on (Priority, SequenceNumber)
        return new SearchResult<TPriority, TElement>(levelFound, predecessorArray, successorArray, nodeFound, _arrayPool, _numberOfLevels);
    }

    #endregion
    #region Background Tasks

    /// <summary>
    /// Schedules the asynchronous physical removal (unlinking) of a logically deleted node from the SkipList.
    /// </summary>
    /// <param name="node">The node to be physically removed. Must already be marked as logically deleted (<see cref="SkipListNode{TPriority, TElement}.IsDeleted"/> must be true).</param>
    /// <param name="topLevel">Optional. The highest level index from which to start unlinking the node. If null, defaults to the node's actual <see cref="SkipListNode{TPriority, TElement}.TopLevel"/>.</param>
    /// <param name="cancellationToken">Optional. A cancellation token to observe while performing the removal.</param>
    /// <remarks>
    /// Submits a task to physically unlink the node from the top level down. Uses locking on predecessors for thread-safe pointer updates.
    /// </remarks>
    internal void SchedulePhysicalNodeRemoval(SkipListNode<TPriority, TElement> node, int? topLevel = null, CancellationToken cancellationToken = default)
    {
        // We only remove logically deleted nodes
        if (!node.IsDeleted) return;

        _observability.LogPhysicalRemovalScheduled(node.Priority, node.SequenceNumber);
        _observability.RecordPhysicalRemovalScheduled();

        try
        {
            _taskOrchestrator.Run(() =>
            {
                int startingLevel = topLevel ?? node.TopLevel;

                // Unlink top-to-bottom to preserve invariants
                for (int level = startingLevel; level >= BottomLevel; level--)
                {
                    // Check cancellation at each level
                    if (cancellationToken.IsCancellationRequested)
                    {
                        _observability.LogPhysicalRemovalCancelled(node.Priority, node.SequenceNumber);
                        return Task.CompletedTask;
                    }

                    SkipListNode<TPriority, TElement> predecessor = _head;

                    // Loop to find the correct predecessor at this level
                    while (true)
                    {
                        SkipListNode<TPriority, TElement> current = predecessor.GetNextNode(level);

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

                // Return the node to the pool after it's been unlinked
                _nodePool.Return(node);

                return Task.CompletedTask;
            });

            // Determine if we should stop searching based on the following criteria:
            // 1. We found the node to remove
            // 2. We reached the Tail node
            // 3. We passed the node (current node's priority/sequence is greater)
            static bool ShouldStopSearch(SkipListNode<TPriority, TElement> currentNode, SkipListNode<TPriority, TElement> nodeToRemove)
            {
                return currentNode == nodeToRemove
                   || currentNode.Type is SkipListNode<TPriority, TElement>.NodeType.Tail
                   || currentNode.CompareTo(nodeToRemove) > 0;
            }
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("queue full"))
        {
            _observability.RecordPhysicalRemovalDropped(node.Priority, node.SequenceNumber);
        }
    }

    #endregion
}