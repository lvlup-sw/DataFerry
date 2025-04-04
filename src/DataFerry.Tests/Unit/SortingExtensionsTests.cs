using lvlup.DataFerry.Extensions;
using lvlup.DataFerry.Utilities;

namespace lvlup.DataFerry.Tests.Algorithms
{
    [TestClass]
    public class SortingExtensionsTests
    {
        [TestMethod]
        public void TestBubbleSort()
        {
            List<int> list = [23, 42, 4, 16, 8, 15, 3, 9, 55, 0, 34, 12, 2, 46, 25];
            list.BubbleSort();
            Assert.IsTrue(list.SequenceEqual(list.OrderBy(x => x)), "Wrong BubbleSort ascending");

            list.BubbleSort(ascending: false);
            Assert.IsTrue(list.SequenceEqual(list.OrderByDescending(x => x)), "Wrong BubbleSort descending");
        }

        [TestMethod]
        public void TestQuickSort()
        {
            List<long> list = [23, 42, 4, 16, 8, 15, 3, 9, 55, 0, 34, 12, 2, 46, 25];
            list.QuickSort();
            long[] sortedList = [0, 2, 3, 4, 8, 9, 12, 15, 16, 23, 25, 34, 42, 46, 55];
            Assert.IsTrue(list.SequenceEqual(sortedList));
        }

        [TestMethod]
        public void TestMergeSort()
        {
            List<int> numbersList = [23, 42, 4, 16, 8, 15, 3, 9, 55, 0, 34, 12, 2, 46, 25];
            numbersList.MergeSort();
            int[] expectedSortedList = [0, 2, 3, 4, 8, 9, 12, 15, 16, 23, 25, 34, 42, 46, 55];
            Assert.IsTrue(numbersList.SequenceEqual(expectedSortedList));
        }

        [TestMethod]
        public void TestBucketSort()
        {
            List<int> numbersList = [23, 42, 4, 16, 8, 15, 3, 9, 55, 0, 34, 12, 2, 46, 25];
            numbersList.BucketSort();
            int[] expectedSortedList = [0, 2, 3, 4, 8, 9, 12, 15, 16, 23, 25, 34, 42, 46, 55];
            Assert.IsTrue(numbersList.SequenceEqual(expectedSortedList));

            // Additional test for descending order
            numbersList.BucketSort(ascending: false);
            Array.Reverse(expectedSortedList);
            Assert.IsTrue(numbersList.SequenceEqual(expectedSortedList));
        }
    }
}