// ===========================================================================
// <copyright file="EnumeratorExtensions.cs" company="Level Up Software">
// Copyright (c) Level Up Software. All rights reserved.
// </copyright>
// ===========================================================================

using System.Collections.Immutable;
using System.Runtime.CompilerServices;

using lvlup.DataFerry.Concurrency.Enumerators;

namespace lvlup.DataFerry.Concurrency.Extensions;

/// <summary>
/// Provides extension methods for enumerators to enhance their functionality.
/// </summary>
/// <remarks>
/// <para>
/// These extensions add convenience methods to enumerators, enabling features like
/// buffering, batching, and conversion between different enumerator types.
/// </para>
/// <para>
/// The methods are designed to work efficiently with concurrent enumerators while
/// maintaining thread-safety and minimizing memory allocations where possible.
/// </para>
/// </remarks>
public static class EnumeratorExtensions
{
    #region Buffering Extensions

    /// <summary>
    /// Buffers elements from an enumerator into chunks of a specified size.
    /// </summary>
    /// <typeparam name="T">The type of elements in the enumerator.</typeparam>
    /// <param name="source">The source enumerator.</param>
    /// <param name="bufferSize">The size of each buffer.</param>
    /// <returns>An enumerable sequence of buffers containing elements from the source.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="source"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if <paramref name="bufferSize"/> is less than or equal to zero.</exception>
    /// <remarks>
    /// This method is useful for processing elements in batches, which can improve
    /// performance when dealing with I/O operations or when reducing the number of
    /// method calls is beneficial.
    /// </remarks>
    public static IEnumerable<IReadOnlyList<T>> Buffer<T>(
        this IEnumerator<T> source,
        int bufferSize)
    {
        ArgumentNullException.ThrowIfNull(source, nameof(source));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bufferSize, nameof(bufferSize));

        var buffer = new List<T>(bufferSize);
        
        while (source.MoveNext())
        {
            buffer.Add(source.Current);
            
            if (buffer.Count >= bufferSize)
            {
                yield return buffer.ToArray();
                buffer.Clear();
            }
        }
        
        if (buffer.Count > 0)
        {
            yield return buffer.ToArray();
        }
    }

    /// <summary>
    /// Buffers elements from an async enumerator into chunks of a specified size.
    /// </summary>
    /// <typeparam name="T">The type of elements in the enumerator.</typeparam>
    /// <param name="source">The source async enumerator.</param>
    /// <param name="bufferSize">The size of each buffer.</param>
    /// <param name="cancellationToken">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>An async enumerable sequence of buffers containing elements from the source.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="source"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if <paramref name="bufferSize"/> is less than or equal to zero.</exception>
    public static async IAsyncEnumerable<IReadOnlyList<T>> BufferAsync<T>(
        this IAsyncEnumerator<T> source,
        int bufferSize,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source, nameof(source));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(bufferSize, nameof(bufferSize));

        var buffer = new List<T>(bufferSize);
        
        while (await source.MoveNextAsync().ConfigureAwait(false))
        {
            cancellationToken.ThrowIfCancellationRequested();
            
            buffer.Add(source.Current);
            
            if (buffer.Count >= bufferSize)
            {
                yield return buffer.ToArray();
                buffer.Clear();
            }
        }
        
        if (buffer.Count > 0)
        {
            yield return buffer.ToArray();
        }
    }

    #endregion

    #region Conversion Extensions

    /// <summary>
    /// Converts an enumerator to a snapshot enumerator by materializing all elements.
    /// </summary>
    /// <typeparam name="TPriority">The type used for priority values.</typeparam>
    /// <typeparam name="TElement">The type of the elements stored in the queue.</typeparam>
    /// <param name="source">The source enumerator.</param>
    /// <returns>A <see cref="SnapshotEnumerator{TPriority, TElement}"/> containing all elements from the source.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="source"/> is null.</exception>
    /// <remarks>
    /// This method fully materializes the enumerator, which requires O(n) memory.
    /// Use this when you need a stable, reusable view of the enumerator's contents.
    /// </remarks>
    public static SnapshotEnumerator<TPriority, TElement> ToSnapshot<TPriority, TElement>(
        this IEnumerator<(TPriority Priority, TElement Element)> source)
    {
        ArgumentNullException.ThrowIfNull(source, nameof(source));

        var items = new List<(TPriority Priority, TElement Element)>();
        while (source.MoveNext())
        {
            items.Add(source.Current);
        }
        
        return new SnapshotEnumerator<TPriority, TElement>(items.ToImmutableList());
    }

    /// <summary>
    /// Converts an enumerable to a snapshot enumerator.
    /// </summary>
    /// <typeparam name="TPriority">The type used for priority values.</typeparam>
    /// <typeparam name="TElement">The type of the elements stored in the queue.</typeparam>
    /// <param name="source">The source enumerable.</param>
    /// <returns>A <see cref="SnapshotEnumerator{TPriority, TElement}"/> containing all elements from the source.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="source"/> is null.</exception>
    public static SnapshotEnumerator<TPriority, TElement> ToSnapshot<TPriority, TElement>(
        this IEnumerable<(TPriority Priority, TElement Element)> source)
    {
        ArgumentNullException.ThrowIfNull(source, nameof(source));
        return new SnapshotEnumerator<TPriority, TElement>(source);
    }

    #endregion

    #region Paging Extensions

    /// <summary>
    /// Pages through an enumerator, returning elements in pages of a specified size.
    /// </summary>
    /// <typeparam name="T">The type of elements in the enumerator.</typeparam>
    /// <param name="source">The source enumerable.</param>
    /// <param name="pageSize">The number of elements per page.</param>
    /// <returns>An enumerable sequence of pages, where each page is a list of elements.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="source"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if <paramref name="pageSize"/> is less than or equal to zero.</exception>
    public static IEnumerable<IReadOnlyList<T>> Page<T>(
        this IEnumerable<T> source,
        int pageSize)
    {
        ArgumentNullException.ThrowIfNull(source, nameof(source));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(pageSize, nameof(pageSize));

        using var enumerator = source.GetEnumerator();
        var page = new List<T>(pageSize);
        
        while (enumerator.MoveNext())
        {
            page.Add(enumerator.Current);
            
            if (page.Count >= pageSize)
            {
                yield return page.ToArray();
                page = new List<T>(pageSize);
            }
        }
        
        if (page.Count > 0)
        {
            yield return page.ToArray();
        }
    }

    #endregion

    #region Windowing Extensions

    /// <summary>
    /// Creates a sliding window over an enumerable sequence.
    /// </summary>
    /// <typeparam name="T">The type of elements in the sequence.</typeparam>
    /// <param name="source">The source enumerable.</param>
    /// <param name="windowSize">The size of the sliding window.</param>
    /// <returns>An enumerable sequence of windows, where each window is an array of elements.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="source"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if <paramref name="windowSize"/> is less than or equal to zero.</exception>
    /// <remarks>
    /// Each window contains <paramref name="windowSize"/> elements (except possibly the initial windows
    /// if the source has fewer elements). As the enumeration progresses, each new window drops the
    /// oldest element and adds the newest one.
    /// </remarks>
    public static IEnumerable<T[]> Window<T>(
        this IEnumerable<T> source,
        int windowSize)
    {
        ArgumentNullException.ThrowIfNull(source, nameof(source));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(windowSize, nameof(windowSize));

        var window = new Queue<T>(windowSize);
        
        foreach (var item in source)
        {
            window.Enqueue(item);
            
            if (window.Count > windowSize)
            {
                window.Dequeue();
            }
            
            if (window.Count == windowSize)
            {
                yield return window.ToArray();
            }
        }
    }

    #endregion

    #region Sampling Extensions

    /// <summary>
    /// Samples every nth element from an enumerable sequence.
    /// </summary>
    /// <typeparam name="T">The type of elements in the sequence.</typeparam>
    /// <param name="source">The source enumerable.</param>
    /// <param name="interval">The sampling interval (e.g., 3 means take every 3rd element).</param>
    /// <returns>An enumerable sequence containing sampled elements.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="source"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if <paramref name="interval"/> is less than or equal to zero.</exception>
    public static IEnumerable<T> Sample<T>(
        this IEnumerable<T> source,
        int interval)
    {
        ArgumentNullException.ThrowIfNull(source, nameof(source));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(interval, nameof(interval));

        var index = 0;
        foreach (var item in source)
        {
            if (index % interval == 0)
            {
                yield return item;
            }
            index++;
        }
    }

    /// <summary>
    /// Randomly samples elements from an enumerable sequence.
    /// </summary>
    /// <typeparam name="T">The type of elements in the sequence.</typeparam>
    /// <param name="source">The source enumerable.</param>
    /// <param name="probability">The probability of selecting each element (0.0 to 1.0).</param>
    /// <param name="seed">Optional seed for the random number generator.</param>
    /// <returns>An enumerable sequence containing randomly sampled elements.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="source"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if <paramref name="probability"/> is not between 0.0 and 1.0.</exception>
    public static IEnumerable<T> RandomSample<T>(
        this IEnumerable<T> source,
        double probability,
        int? seed = null)
    {
        ArgumentNullException.ThrowIfNull(source, nameof(source));
        
        if (probability < 0.0 || probability > 1.0)
        {
            throw new ArgumentOutOfRangeException(nameof(probability), "Probability must be between 0.0 and 1.0");
        }

        var random = seed.HasValue ? new Random(seed.Value) : Random.Shared;
        
        foreach (var item in source)
        {
            if (random.NextDouble() < probability)
            {
                yield return item;
            }
        }
    }

    #endregion

    #region Statistics Extensions

    /// <summary>
    /// Counts elements while enumerating, providing a running count with each element.
    /// </summary>
    /// <typeparam name="T">The type of elements in the sequence.</typeparam>
    /// <param name="source">The source enumerable.</param>
    /// <returns>An enumerable sequence of tuples containing the element and its index.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="source"/> is null.</exception>
    public static IEnumerable<(T Item, int Index)> WithIndex<T>(
        this IEnumerable<T> source)
    {
        ArgumentNullException.ThrowIfNull(source, nameof(source));

        var index = 0;
        foreach (var item in source)
        {
            yield return (item, index++);
        }
    }

    /// <summary>
    /// Adds timing information to each element in the sequence.
    /// </summary>
    /// <typeparam name="T">The type of elements in the sequence.</typeparam>
    /// <param name="source">The source enumerable.</param>
    /// <returns>An enumerable sequence of tuples containing the element and elapsed time since enumeration started.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="source"/> is null.</exception>
    public static IEnumerable<(T Item, TimeSpan Elapsed)> WithTiming<T>(
        this IEnumerable<T> source)
    {
        ArgumentNullException.ThrowIfNull(source, nameof(source));

        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        
        foreach (var item in source)
        {
            yield return (item, stopwatch.Elapsed);
        }
    }

    #endregion
}