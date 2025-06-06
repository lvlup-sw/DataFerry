// ===========================================================================
// <copyright file="AdaptiveSprayTracker.cs" company="Level Up Software">
// Copyright (c) Level Up Software. All rights reserved.
// </copyright>
// ===========================================================================

using System.Collections.Concurrent;

namespace lvlup.DataFerry.Concurrency.Algorithms;

/// <summary>
/// Tracks and adaptively adjusts parameters for the SprayList algorithm based on observed performance.
/// </summary>
/// <remarks>
/// <para>
/// This class implements an adaptive feedback mechanism that monitors the success rate,
/// contention levels, and operation duration of spray operations to dynamically adjust
/// the spray parameters (OffsetK and OffsetM) for optimal performance.
/// </para>
/// <para>
/// The adaptation algorithm uses a rolling window of recent results to make informed
/// adjustments, increasing spread when contention is high and reducing it when operations
/// are performing well with low contention.
/// </para>
/// </remarks>
internal sealed class AdaptiveSprayTracker
{
    #region Fields

    /// <summary>
    /// The base OffsetK value as configured.
    /// </summary>
    private readonly int _baseOffsetK;

    /// <summary>
    /// The base OffsetM value as configured.
    /// </summary>
    private readonly int _baseOffsetM;

    /// <summary>
    /// Rolling window of recent spray operation results.
    /// </summary>
    private readonly RollingWindow<SprayResult> _results;

    /// <summary>
    /// Current adaptive OffsetK value.
    /// </summary>
    private int _currentOffsetK;

    /// <summary>
    /// Current adaptive OffsetM value.
    /// </summary>
    private int _currentOffsetM;

    /// <summary>
    /// Lock for coordinating parameter adjustments.
    /// </summary>
    private readonly object _adjustmentLock = new();

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="AdaptiveSprayTracker"/> class.
    /// </summary>
    /// <param name="offsetK">The base OffsetK parameter for spray height calculation.</param>
    /// <param name="offsetM">The base OffsetM parameter for jump length calculation.</param>
    /// <param name="windowSize">The size of the rolling window for tracking results.</param>
    public AdaptiveSprayTracker(int offsetK, int offsetM, int windowSize = 100)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(offsetK, nameof(offsetK));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(offsetM, nameof(offsetM));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(windowSize, nameof(windowSize));

        _baseOffsetK = _currentOffsetK = offsetK;
        _baseOffsetM = _currentOffsetM = offsetM;
        _results = new RollingWindow<SprayResult>(windowSize);
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Gets the current adaptive offset parameters.
    /// </summary>
    /// <returns>A tuple containing the current OffsetK and OffsetM values.</returns>
    public (int OffsetK, int OffsetM) GetCurrentOffsets()
    {
        lock (_adjustmentLock)
        {
            return (_currentOffsetK, _currentOffsetM);
        }
    }

    /// <summary>
    /// Records the result of a spray operation for adaptive parameter adjustment.
    /// </summary>
    /// <param name="success">Whether the operation was successful.</param>
    /// <param name="jumpLength">The jump length used in the operation.</param>
    /// <param name="duration">The duration of the operation.</param>
    /// <param name="contentionLevel">The estimated contention level during the operation.</param>
    public void RecordResult(bool success, int jumpLength, TimeSpan duration, int contentionLevel)
    {
        var result = new SprayResult
        {
            Success = success,
            JumpLength = jumpLength,
            Duration = duration,
            ContentionLevel = contentionLevel,
            Timestamp = DateTime.UtcNow
        };

        _results.Add(result);

        // Adjust parameters if we have enough data
        if (_results.Count >= _results.Capacity / 2)
        {
            AdjustParameters();
        }
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Adjusts the spray parameters based on recent operation results.
    /// </summary>
    private void AdjustParameters()
    {
        lock (_adjustmentLock)
        {
            var recentResults = _results.GetAll();
            if (recentResults.Count == 0) return;

            var successRate = recentResults.Count(r => r.Success) / (double)recentResults.Count;
            var avgContentionLevel = recentResults.Average(r => r.ContentionLevel);
            var avgDuration = recentResults.Average(r => r.Duration.TotalMilliseconds);

            // Adjust OffsetM based on success rate and contention
            if (successRate < 0.6 && avgContentionLevel > 5)
            {
                // High contention, low success - increase spread
                _currentOffsetM = Math.Min(_currentOffsetM + 1, _baseOffsetM * 3);
            }
            else if (successRate > 0.9 && avgContentionLevel < 2)
            {
                // Low contention, high success - can be more aggressive
                _currentOffsetM = Math.Max(_currentOffsetM - 1, 1);
            }

            // Adjust OffsetK based on operation duration
            if (avgDuration > 10) // milliseconds
            {
                // Operations taking too long - start higher to reduce traversal
                _currentOffsetK = Math.Min(_currentOffsetK + 1, _baseOffsetK * 2);
            }
            else if (avgDuration < 1)
            {
                // Very fast operations - can start lower for better accuracy
                _currentOffsetK = Math.Max(_currentOffsetK - 1, 1);
            }
        }
    }

    #endregion

    #region Nested Types

    /// <summary>
    /// Represents the result of a single spray operation.
    /// </summary>
    private class SprayResult
    {
        /// <summary>
        /// Gets or sets a value indicating whether the operation was successful.
        /// </summary>
        public bool Success { get; init; }

        /// <summary>
        /// Gets or sets the jump length used in the operation.
        /// </summary>
        public int JumpLength { get; init; }

        /// <summary>
        /// Gets or sets the duration of the operation.
        /// </summary>
        public TimeSpan Duration { get; init; }

        /// <summary>
        /// Gets or sets the estimated contention level.
        /// </summary>
        public int ContentionLevel { get; init; }

        /// <summary>
        /// Gets or sets the timestamp of the operation.
        /// </summary>
        public DateTime Timestamp { get; init; }
    }

    /// <summary>
    /// Implements a thread-safe rolling window for storing recent results.
    /// </summary>
    /// <typeparam name="T">The type of elements in the window.</typeparam>
    private class RollingWindow<T>
    {
        private readonly Queue<T> _items = new();
        private readonly object _lock = new();
        private readonly int _capacity;

        /// <summary>
        /// Initializes a new instance of the <see cref="RollingWindow{T}"/> class.
        /// </summary>
        /// <param name="capacity">The maximum number of items to store.</param>
        public RollingWindow(int capacity)
        {
            _capacity = capacity;
        }

        /// <summary>
        /// Gets the current number of items in the window.
        /// </summary>
        public int Count
        {
            get
            {
                lock (_lock)
                {
                    return _items.Count;
                }
            }
        }

        /// <summary>
        /// Gets the capacity of the window.
        /// </summary>
        public int Capacity => _capacity;

        /// <summary>
        /// Adds an item to the window, removing the oldest if at capacity.
        /// </summary>
        /// <param name="item">The item to add.</param>
        public void Add(T item)
        {
            lock (_lock)
            {
                if (_items.Count >= _capacity)
                {
                    _items.Dequeue();
                }
                _items.Enqueue(item);
            }
        }

        /// <summary>
        /// Gets all items currently in the window.
        /// </summary>
        /// <returns>A list containing all items in the window.</returns>
        public List<T> GetAll()
        {
            lock (_lock)
            {
                return new List<T>(_items);
            }
        }
    }

    #endregion
}