namespace lvlup.DataFerry.Utilities;

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
        if (collection is null) return;

        for (int i = 0; i < collection.Count; i++)
        {
            collection[i] = value;
        }
    }
}