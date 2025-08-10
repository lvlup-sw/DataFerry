// ===================================================================
// <copyright file="ListExtensions.cs" company="Level Up Software">
// Copyright (c) Level Up Software. All rights reserved.
// </copyright>
// ===================================================================

namespace lvlup.DataFerry.Extensions;

/// <summary>
/// A collection of extension methods for <see cref="IList{T}"/>.
/// </summary>
internal static class ListExtensions
{
    /// <summary>
    /// Swaps the elements at the specified indexes within the list.
    /// </summary>
    /// <typeparam name="T">The type of elements in the list.</typeparam>
    /// <param name="list">The list in which to swap elements.</param>
    /// <param name="firstIndex">The index of the first element to swap.</param>
    /// <param name="secondIndex">The index of the second element to swap.</param>
    public static void Swap<T>(this IList<T> list, int firstIndex, int secondIndex)
    {
        if (list.Count < 2 || firstIndex == secondIndex) return;

        (list[secondIndex], list[firstIndex]) = (list[firstIndex], list[secondIndex]);
    }

    /// <summary>
    /// Fills the entire collection with the specified value.
    /// </summary>
    /// <typeparam name="T">The type of elements in the collection.</typeparam>
    /// <param name="collection">The collection to populate.</param>
    /// <param name="value">The value to fill the collection with.</param>
    public static void Populate<T>(this IList<T> collection, T value)
    {
        for (int i = 0; i < collection.Count; i++)
        {
            collection[i] = value;
        }
    }

    /// <summary>
    /// Executes an action on an object if the object is not null.
    /// </summary>
    /// <typeparam name="T">The type of the object.</typeparam>
    /// <param name="obj">The object to execute the action on. Can be null.</param>
    /// <param name="action">The action to execute if the object is not null.</param>
    public static void Pipe<T>(this T? obj, Action<T> action)
        where T : class
    {
        if (obj is not null) action(obj);
    }
}