using System.Diagnostics.CodeAnalysis;

namespace lvlup.DataFerry.Caching.Results;

/// <summary>
/// Represents the result of an asynchronous TryGet operation.
/// </summary>
/// <typeparam name="TValue">The type of the value retrieved from the cache.</typeparam>
public readonly record struct TryGetResult<TValue>
{
    /// <summary>
    /// Indicates whether the value was found in the cache.
    /// </summary>
    public bool Success { get; }

    /// <summary>
    /// The value retrieved from the cache, or default if not found.
    /// </summary>
    [MaybeNull]
    public TValue Value { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="TryGetResult{TValue}"/> class.
    /// </summary>
    /// <param name="success">Whether the operation was successful.</param>
    /// <param name="value">The value retrieved from the cache, or default if not successful.</param>
    public TryGetResult(bool success, [AllowNull] TValue value)
    {
        Success = success;
        Value = value!;
    }

    /// <summary> Deconstructs the result into its constituent parts. </summary>
    /// <param name="success">When this method returns, contains the value of the <see cref="Success"/> property.</param>
    /// <param name="value">When this method returns, contains the value of the <see cref="Value"/> property.</param>
    public void Deconstruct(out bool success, [MaybeNull] out TValue value)
    {
        success = Success;
        value = Value;
    }
}