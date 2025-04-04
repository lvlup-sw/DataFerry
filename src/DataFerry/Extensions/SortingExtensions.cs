// =====================================================================
// <copyright file="SortingExtensions.cs" company="Level Up Software">
// Copyright (c) Level Up Software. All rights reserved.
// </copyright>
// =====================================================================

using lvlup.DataFerry.Utilities;

namespace lvlup.DataFerry.Extensions;

/// <summary>
/// Provides extension methods for sorting lists using the BubbleSort, BucketSort, MergeSort, and QuickSort algorithms.
/// </summary>
public static class SortingExtensions
{
    /// <summary>
    /// Sorts the entire list using the Bubble Sort algorithm.
    /// </summary>
    /// <typeparam name="T">The type of elements in the list.</typeparam>
    /// <param name="collection">The list to sort.</param>
    /// <param name="comparer">The comparer used to determine the order of elements.
    /// If not provided, the default comparer for the element type is used.</param>
    /// <param name="ascending">Specifies whether to sort in ascending (true) or descending (false) order. Defaults to true.</param>
    public static void BubbleSort<T>(this IList<T> collection, Comparer<T>? comparer = null, bool ascending = true)
    {
        comparer ??= Comparer<T>.Default;
        collection.BubbleSortInternal(ascending
            ? (a, b) => comparer.Compare(a, b) > 0
            : (a, b) => comparer.Compare(a, b) < 0);
    }

    /// <summary>
    /// Performs the internal Bubble Sort logic on a list.
    /// </summary>
    /// <typeparam name="T">The type of elements in the list.</typeparam>
    /// <param name="collection">The list to sort.</param>
    /// <param name="shouldSwap">A function that determines whether two elements should be swapped.</param>
    private static void BubbleSortInternal<T>(this IList<T> collection, Func<T, T, bool> shouldSwap)
    {
        for (int i = 0; i < collection.Count - 1; i++)
        {
            bool swapped = false;

            for (int j = 0; j < collection.Count - i - 1; j++)
            {
                if (!shouldSwap(collection[j], collection[j + 1]))
                {
                    continue;
                }

                collection.Swap(j, j + 1);
                swapped = true;
            }

            if (!swapped) break;
        }
    }

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

    /// <summary>
    /// Sorts the entire list in-place using the Merge Sort algorithm.
    /// </summary>
    /// <typeparam name="T">The type of elements in the list.</typeparam>
    /// <param name="collection">The list to sort.</param>
    /// <param name="comparer">The comparer used to determine the order of elements.
    /// If not provided, the default comparer for the element type is used.</param>
    public static void MergeSort<T>(this IList<T> collection, Comparer<T>? comparer = null)
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
        if (left >= right) return;

        // Find midpoint
        int mid = left + ((right - left) / 2);

        // Perform the sorting
        InternalMergeSort(collection, left, mid, comparer);
        InternalMergeSort(collection, mid + 1, right, comparer);

        // Merge
        InternalMerge(collection, left, mid, right, comparer);
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

    /// <summary>
    /// Sorts the entire list using the QuickSort algorithm
    /// with a cutoff to Insertion Sort for small subarrays.
    /// </summary>
    /// <typeparam name="T">The type of elements in the list.</typeparam>
    /// <param name="collection">The list to sort.</param>
    /// <param name="comparer">The comparer used to determine the order of elements.
    /// If not provided, the default comparer for the element type is used.</param>
    public static void QuickSort<T>(this IList<T> collection, Comparer<T>? comparer = null)
    {
        const int startIndex = 0;
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
        int mid = left + ((right - left) / 2);
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