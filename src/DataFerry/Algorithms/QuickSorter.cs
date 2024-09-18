namespace DataFerry.Algorithms
{
    /// <summary>
    /// Provides extension methods for sorting lists using the QuickSort algorithm.
    /// </summary>
    public static class QuickSorter
    {
        /// <summary>
        /// Sorts the entire list using the QuickSort algorithm 
        /// with a cutoff to Insertion Sort for small subarrays.
        /// </summary>
        /// <typeparam name="T">The type of elements in the list.</typeparam>
        /// <param name="collection">The list to sort.</param>
        /// <param name="comparer">The comparer used to determine the order of elements. 
        /// If not provided, the default comparer for the element type is used.</param>
        public static void QuickSort<T>(this IList<T> collection, Comparer<T> comparer = default!)
        {
            int startIndex = 0;
            int endIndex = collection.Count - 1;

            comparer ??= Comparer<T>.Default;
            collection.InternalQuickSort(startIndex, endIndex, comparer);
        }

        /// <summary>
        /// Sorts the specified portion of a list using the QuickSort algorithm 
        /// with a cutoff to Insertion Sort for small subarrays.
        /// </summary>
        /// <typeparam name="T">The type of elements in the list.</typeparam>
        /// <param name="collection">The list to sort.</param>
        /// <param name="left">The index of the leftmost element in the portion to sort.</param>
        /// <param name="right">The index of the rightmost element in the portion to sort.</param>
        /// <param name="comparer">The comparer used to determine the order of elements.</param>
        private static void InternalQuickSort<T>(this IList<T> collection, int left, int right, Comparer<T> comparer)
        {
            const int insertionSortThreshold = 10;

            while (left < right)
            {
                // Cutoff to Insertion Sort for small subarrays
                if (right - left < insertionSortThreshold)
                {
                    collection.InsertionSort(left, right, comparer);
                    return;
                }

                int pivotIndex = collection.Partition(left, right, comparer);

                // Recurse on the smaller partition first to limit stack depth
                if (pivotIndex - left < right - pivotIndex)
                {
                    collection.InternalQuickSort(left, pivotIndex - 1, comparer);
                    left = pivotIndex + 1; // Tail recursion optimization
                }
                else
                {
                    collection.InternalQuickSort(pivotIndex + 1, right, comparer);
                    right = pivotIndex - 1; // Tail recursion optimization
                }
            }
        }

        /// <summary>
        /// Partitions the specified portion of a list using the QuickSort algorithm with Median-of-Three pivot selection.
        /// </summary>
        /// <typeparam name="T">The type of elements in the list.</typeparam>
        /// <param name="collection">The list to partition.</param>
        /// <param name="left">The index of the leftmost element in the partition.</param>
        /// <param name="right">The index of the rightmost element in the partition.</param>
        /// <param name="comparer">The comparer used to determine the order of elements.</param>
        /// <returns>The final index of the pivot element after partitioning.</returns>
        private static int Partition<T>(this IList<T> collection, int left, int right, Comparer<T> comparer)
        {
            // Median-of-Three pivot selection
            int mid = left + (right - left) / 2;
            if (comparer.Compare(collection[mid], collection[left]) < 0)
                collection.Swap(left, mid);
            if (comparer.Compare(collection[right], collection[left]) < 0)
                collection.Swap(left, right);
            if (comparer.Compare(collection[right], collection[mid]) < 0)
                collection.Swap(mid, right);

            T pivot = collection[mid];
            int i = left + 1;
            int j = right - 1;

            while (i <= j)
            {
                while (i <= j && comparer.Compare(collection[i], pivot) <= 0)
                    i++;
                while (i <= j && comparer.Compare(collection[j], pivot) > 0)
                    j--;

                if (i < j)
                    collection.Swap(i, j);
            }

            // Place the pivot in its final position. 
            // Since we used median-of-three, the pivot might be at 'left'
            collection.Swap(mid, j);
            return j;
        }

        /// <summary>
        /// Sorts the specified portion of a list using the Insertion Sort algorithm.
        /// </summary>
        /// <typeparam name="T">The type of elements in the list.</typeparam>
        /// <param name="collection">The list to sort.</param>
        /// <param name="left">The index of the leftmost element in the portion to sort.</param>
        /// <param name="right">The index of the rightmost element in the portion to sort.</param>
        /// <param name="comparer">The comparer used to determine the order of elements.</param>
        private static void InsertionSort<T>(this IList<T> collection, int left, int right, Comparer<T> comparer)
        {
            for (int i = left + 1; i <= right; i++)
            {
                T key = collection[i];
                int j = i - 1;

                while (j >= left && comparer.Compare(collection[j], key) > 0)
                {
                    collection[j + 1] = collection[j];
                    j--;
                }

                collection[j + 1] = key;
            }
        }
    }
}