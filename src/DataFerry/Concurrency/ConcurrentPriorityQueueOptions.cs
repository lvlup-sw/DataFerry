// ===========================================================================
// <copyright file="ConcurrentPriorityQueueOptions.cs" company="Level Up Software">
// Copyright (c) Level Up Software. All rights reserved.
// </copyright>
// ===========================================================================

using System.ComponentModel.DataAnnotations;

namespace lvlup.DataFerry.Concurrency;

/// <summary>
/// Configuration options for the <see cref="ConcurrentPriorityQueue{TPriority,TElement}"/>.
/// </summary>
public class ConcurrentPriorityQueueOptions : IValidatableObject
{
    /// <summary>
    /// Default size limit for bounded queues.
    /// </summary>
    /// <remarks>
    /// This value provides a good balance for SkipList level calculation while being reasonable
    /// for most applications. Use <see cref="int.MaxValue"/> for effectively unbounded queues.
    /// </remarks>
    public const int DefaultMaxSize = 10000;

    /// <summary>
    /// Default promotion chance for each level. [0, 1).
    /// </summary>
    /// <remarks>
    /// 0.5 is the standard default for SkipList implementations, providing good balance
    /// between search performance and memory overhead.
    /// </remarks>
    public const double DefaultPromotionProbability = 0.5;

    /// <summary>
    /// The default tuning constant (K) added to the height used in the calculation during the Spray operation.
    /// </summary>
    /// <remarks>
    /// Increasing this value starts the spray slightly higher, potentially giving it more room to spread.
    /// This does not typically need to be adjusted.
    /// </remarks>
    public const int DefaultSprayOffsetK = 1;

    /// <summary>
    /// The default tuning constant (M) multiplied by the jump length used in the calculation during the Spray operation.
    /// </summary>
    /// <remarks>
    /// This value directly scales the maximum jump length. This should be adjusted in the case where
    /// reducing contention is more desirable than deleting a node with a strictly higher priority.
    /// </remarks>
    public const int DefaultSprayOffsetM = 1;

    /// <summary>
    /// Gets or sets the maximum number of elements allowed in the queue.
    /// </summary>
    /// <remarks>
    /// Use <see cref="int.MaxValue"/> to effectively have no size limit.
    /// The default value is optimized for bounded queues where SprayList contention reduction is most beneficial.
    /// Must be at least 10 to ensure reasonable SkipList performance.
    /// </remarks>
    [Range(10, int.MaxValue, ErrorMessage = "MaxSize must be at least 10 for reasonable performance.")]
        public int MaxSize { get; init; } = DefaultMaxSize;

    /// <summary>
    /// Gets or sets the tuning constant K (affecting spray height) for the SprayList probabilistic delete-min operation.
    /// </summary>
    /// <remarks>
    /// Increasing this value starts the spray slightly higher, potentially giving it more room to spread.
    /// This does not typically need to be adjusted. Must be greater than 0.
    /// </remarks>
    [Range(1, 20, ErrorMessage = "SprayOffsetK must be between 1 and 20.")]
    public int SprayOffsetK { get; init; } = DefaultSprayOffsetK;

    /// <summary>
    /// Gets or sets the tuning constant M (scaling spray jump length) for the SprayList probabilistic delete-min operation.
    /// </summary>
    /// <remarks>
    /// This value directly scales the maximum jump length. This should be adjusted in the case where
    /// reducing contention is more desirable than deleting a node with a strictly higher priority.
    /// Must be greater than 0.
    /// </remarks>
    [Range(1, 20, ErrorMessage = "SprayOffsetM must be between 1 and 20.")]
    public int SprayOffsetM { get; init; } = DefaultSprayOffsetM;

    /// <summary>
    /// Gets or sets the probability (0 to 1, exclusive) of promoting a new node to the next higher level during insertion.
    /// </summary>
    /// <remarks>
    /// Higher values create taller SkipLists with potentially faster searches but more memory overhead.
    /// Lower values create shorter SkipLists with slower searches but less memory overhead.
    /// Common values are 0.25, 0.5 (default), and 0.75.
    /// </remarks>
    [Range(0.0001, 0.9999, ErrorMessage = "PromotionProbability must be between 0 (exclusive) and 1 (exclusive).")]
    public double PromotionProbability { get; init; } = DefaultPromotionProbability;

    /// <summary>
    /// Validates the configuration options for logical consistency and performance characteristics.
    /// </summary>
    /// <param name="validationContext">The validation context.</param>
    /// <returns>A collection of validation results, or an empty collection if all values are valid.</returns>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        // Validate MaxSize for logical consistency
        if (MaxSize > 1_000_000 && MaxSize != int.MaxValue)
        {
            yield return new ValidationResult(
                $"MaxSize of {MaxSize:N0} is very large but not unbounded. " +
                "Consider using int.MaxValue for unbounded queues or a smaller value for bounded queues.",
                [nameof(MaxSize)]);
        }

        // Validate spray parameters for performance
        if (SprayOffsetK > 5 || SprayOffsetM > 5)
        {
            yield return new ValidationResult(
                "SprayOffsetK and SprayOffsetM values above 5 may lead to suboptimal performance. " +
                "These parameters should typically remain at their default values unless specifically tuned for your workload.",
                [nameof(SprayOffsetK), nameof(SprayOffsetM)]);
        }

        // Validate extreme promotion probabilities
        if (PromotionProbability is < 0.1 or > 0.9)
        {
            yield return new ValidationResult(
                $"PromotionProbability of {PromotionProbability:F3} is extreme and may lead to poor performance. " +
                "Consider values between 0.25 and 0.75 for most use cases.",
                [nameof(PromotionProbability)]);
        }

        // Validate very small bounded queues
        if (MaxSize < 100 && MaxSize != int.MaxValue)
        {
            yield return new ValidationResult(
                $"MaxSize of {MaxSize} is very small and may not benefit from SkipList structure. " +
                "Consider using a larger size or a different data structure for small collections.",
                [nameof(MaxSize)]);
        }
    }

    /// <summary>
    /// Creates options for a bounded queue with the specified maximum size.
    /// </summary>
    /// <param name="maxSize">The maximum number of elements allowed in the queue.</param>
    /// <returns>A new <see cref="ConcurrentPriorityQueueOptions"/> instance configured for a bounded queue.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if maxSize is less than 10.</exception>
    public static ConcurrentPriorityQueueOptions CreateBounded(int maxSize)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxSize, 10, nameof(maxSize));

        return new ConcurrentPriorityQueueOptions
        {
            MaxSize = maxSize,
            SprayOffsetK = DefaultSprayOffsetK,
            SprayOffsetM = DefaultSprayOffsetM,
            PromotionProbability = DefaultPromotionProbability
        };
    }

    /// <summary>
    /// Creates options for an unbounded queue.
    /// </summary>
    /// <returns>A new <see cref="ConcurrentPriorityQueueOptions"/> instance configured for an unbounded queue.</returns>
    /// <remarks>
    /// Unbounded queues use <see cref="int.MaxValue"/> as the size limit and optimize SkipList levels accordingly.
    /// </remarks>
    public static ConcurrentPriorityQueueOptions CreateUnbounded()
    {
        return new ConcurrentPriorityQueueOptions
        {
            MaxSize = int.MaxValue,
            SprayOffsetK = DefaultSprayOffsetK,
            SprayOffsetM = DefaultSprayOffsetM,
            PromotionProbability = DefaultPromotionProbability
        };
    }

    /// <summary>
    /// Creates options optimized for high-throughput scenarios with reduced contention.
    /// </summary>
    /// <param name="maxSize">The maximum number of elements allowed in the queue. Defaults to <see cref="DefaultMaxSize"/>.</param>
    /// <returns>A new <see cref="ConcurrentPriorityQueueOptions"/> instance configured for high throughput.</returns>
    /// <remarks>
    /// This configuration uses slightly higher spray parameters to reduce contention during high-throughput operations.
    /// </remarks>
    public static ConcurrentPriorityQueueOptions CreateHighThroughput(int maxSize = DefaultMaxSize)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxSize, 10, nameof(maxSize));

        return new ConcurrentPriorityQueueOptions
        {
            MaxSize = maxSize,
            SprayOffsetK = 2,  // Slightly higher to reduce contention
            SprayOffsetM = 2,  // Slightly higher to reduce contention
            PromotionProbability = DefaultPromotionProbability
        };
    }

    /// <summary>
    /// Returns a string representation of the current configuration.
    /// </summary>
    /// <returns>A string describing the queue configuration.</returns>
    public override string ToString()
    {
        var sizeDescription = MaxSize == int.MaxValue ? "Unbounded" : $"Bounded({MaxSize:N0})";
        return $"ConcurrentPriorityQueueOptions: {sizeDescription}, " +
               $"Probability={PromotionProbability:F2}, " +
               $"SprayK={SprayOffsetK}, SprayM={SprayOffsetM}";
    }
}