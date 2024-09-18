namespace DataFerry.Algorithms
{
    /// <summary>
    /// Provides extension methods for sorting lists using the BucketSort algorithm.
    /// </summary>
    public static class BucketSorter
    {
        /// <summary>
        /// Sorts the entire list of integers using the Bucket Sort algorithm.
        /// </summary>
        /// <param name="collection">The list of integers to sort.</param>
        /// <param name="ascending">Specifies whether to sort in ascending (true) or descending (false) order. Defaults to true.</param>
        public static void BucketSort(this IList<int> collection, bool ascending = true)
        {
            collection.BucketSortInternal(ascending 
                ? Comparer<int>.Default 
                : Comparer<int>.Create((a, b) => b.CompareTo(a)));
        }

        /// <summary>
        /// Performs the internal Bucket Sort logic on a list of integers.
        /// </summary>
        /// <param name="collection">The list of integers to sort.</param>
        /// <param name="comparer">The comparer used to determine the order of elements.</param>
        private static void BucketSortInternal(this IList<int> collection, Comparer<int> comparer)
        {
            int maxValue = collection.Max();
            int minValue = collection.Min();

            var buckets = new List<int>[maxValue - minValue + 1];
            for (int i = 0; i < buckets.Length; i++)
            {
                buckets[i] = [];
            }

            foreach (int item in collection)
            {
                buckets[item - minValue].Add(item);
            }

            // Use LINQ to flatten and sort the buckets
            var sortedItems = buckets.SelectMany(bucket => bucket).OrderBy(x => x, comparer);

            // Copy the sorted items back to the original collection
            int k = 0;
            foreach (var item in sortedItems)
            {
                collection[k++] = item;
            }
        }
    }
}