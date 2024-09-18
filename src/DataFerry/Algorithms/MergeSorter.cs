namespace DataFerry.Algorithms
{
    /// <summary>
    /// Provides extension methods for sorting lists using the MergeSort algorithm.
    /// </summary>
    public static class MergeSorter
    {
        /// <summary>
        /// Sorts the entire list in-place using the Merge Sort algorithm.
        /// </summary>
        /// <typeparam name="T">The type of elements in the list.</typeparam>
        /// <param name="collection">The list to sort.</param>
        /// <param name="comparer">The comparer used to determine the order of elements. 
        /// If not provided, the default comparer for the element type is used.</param>
        public static void MergeSort<T>(this IList<T> collection, Comparer<T> comparer = default!)
        {
            comparer ??= Comparer<T>.Default;
            InternalMergeSort(collection, 0, collection.Count - 1, comparer);
        }

        /// <summary>
        /// Recursively sorts the specified portion of a list using the Merge Sort algorithm.
        /// </summary>
        /// <typeparam name="T">The type of elements in the list.</typeparam>
        /// <param name="collection">The list to sort.</param>
        /// <param name="left">The index of the leftmost element in the portion to sort.</param>
        /// <param name="right">The index of the rightmost element in the portion to sort.</param>
        /// <param name="comparer">The comparer used to determine the order of elements.</param>
        private static void InternalMergeSort<T>(IList<T> collection, int left, int right, Comparer<T> comparer)
        {
            if (left < right)
            {
                int mid = left + (right - left) / 2;

                InternalMergeSort(collection, left, mid, comparer);
                InternalMergeSort(collection, mid + 1, right, comparer);

                InternalMerge(collection, left, mid, right, comparer);
            }
        }

        /// <summary>
        /// Merges two sorted sublists within the specified list.
        /// </summary>
        /// <typeparam name="T">The type of elements in the list.</typeparam>
        /// <param name="collection">The list containing the sublists to merge.</param>
        /// <param name="left">The index of the leftmost element in the first sublist.</param>
        /// <param name="mid">The index of the rightmost element in the first sublist.</param>
        /// <param name="right">The index of the rightmost element in the second sublist.</param>
        /// <param name="comparer">The comparer used to determine the order of elements.</param>
        private static void InternalMerge<T>(IList<T> collection, int left, int mid, int right, Comparer<T> comparer)
        {
            int leftSize = mid - left + 1;
            int rightSize = right - mid;

            // Manually copy elements from the collection to the temporary arrays
            T[] leftArray = collection.Skip(left).Take(leftSize).ToArray();
            T[] rightArray = collection.Skip(mid + 1).Take(rightSize).ToArray();

            int i = 0, j = 0, k = left;

            while (i < leftSize && j < rightSize)
            {
                if (comparer.Compare(leftArray[i], rightArray[j]) <= 0)
                {
                    collection[k++] = leftArray[i++];
                }
                else
                {
                    collection[k++] = rightArray[j++];
                }
            }

            while (i < leftSize)
            {
                collection[k++] = leftArray[i++];
            }

            while (j < rightSize)
            {
                collection[k++] = rightArray[j++];
            }
        }
    }
}
