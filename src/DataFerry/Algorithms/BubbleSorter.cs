namespace DataFerry.Algorithms
{
    /// <summary>
    /// Provides extension methods for sorting lists using the BubbleSort algorithm.
    /// </summary>
    public static class BubbleSorter
    {
        /// <summary>
        /// Sorts the entire list using the Bubble Sort algorithm.
        /// </summary>
        /// <typeparam name="T">The type of elements in the list.</typeparam>
        /// <param name="collection">The list to sort.</param>
        /// <param name="comparer">The comparer used to determine the order of elements. 
        /// If not provided, the default comparer for the element type is used.</param>
        /// <param name="ascending">Specifies whether to sort in ascending (true) or descending (false) order. Defaults to true.</param>
        public static void BubbleSort<T>(this IList<T> collection, Comparer<T> comparer = default!, bool ascending = true)
        {
            comparer ??= Comparer<T>.Default;
            collection.BubbleSortInternal(comparer, ascending 
                ? (a, b) => comparer.Compare(a, b) > 0
                : (a, b) => comparer.Compare(a, b) < 0);
        }

        /// <summary>
        /// Performs the internal Bubble Sort logic on a list.
        /// </summary>
        /// <typeparam name="T">The type of elements in the list.</typeparam>
        /// <param name="collection">The list to sort.</param>
        /// <param name="comparer">The comparer used to determine the order of elements.</param>
        /// <param name="shouldSwap">A function that determines whether two elements should be swapped.</param>
        private static void BubbleSortInternal<T>(this IList<T> collection, Comparer<T> comparer, Func<T, T, bool> shouldSwap)
        {
            for (int i = 0; i < collection.Count - 1; i++)
            {
                bool swapped = false;

                for (int j = 0; j < collection.Count - i - 1; j++)
                {
                    if (shouldSwap(collection[j], collection[j + 1]))
                    {
                        collection.Swap(j, j + 1);
                        swapped = true;
                    }
                }

                if (!swapped)
                    break;
            }
        }
    }
}
