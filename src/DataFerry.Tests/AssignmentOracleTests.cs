using DataFerry.Algorithms;
using DataFerry.Collections;
using DataFerry.Tests.TestModels;
using Moq;

namespace DataFerry.Tests
{
    [TestClass]
    public class AssignmentOracleTests
    {
        [TestInitialize]
        public void Setup()
        {

        }

        [TestCleanup]
        public void Cleanup()
        {

        }

        [TestMethod]
        public void Test_FindAssignments_Int1()
        {
            int[,] costMatrix = {
                { 10, 19, 8, 15 },
                { 10, 18, 7, 17 },
                { 13, 16, 9, 14 },
                { 12, 19, 8, 18 }
            };

            var munkres = new AssignmentOracle<int>(
                costMatrix,
                (a, b) => Math.Min(a, b),
                (a, b) => a - b,
                (a, b) => a + b
            );

            int[] assignments = munkres.FindAssignments();

            // Assert the expected assignments (can be derived manually or from a known solution)
            Assert.AreEqual(assignments, [2, 1, 3, 0]);
        }

        [TestMethod]
        public void Test_FindAssignments_Int2()
        {
            int[,] costMatrix = {
                { 8, 4, 7 },
                { 5, 2, 3 },
                { 9, 4, 8 }
            };

            var munkres = new AssignmentOracle<int>(
                costMatrix,
                (a, b) => Math.Min(a, b),
                (a, b) => a - b,
                (a, b) => a + b
            );

            int[] assignments = munkres.FindAssignments();

            // Assert the expected assignments (can be derived manually or from a known solution)
            Assert.AreEqual(assignments, [8, 4, 3]);
        }

        [TestMethod]
        public void Test_FindAssignments_MyCost()
        {
            MyCost[,] costMatrix = {
                { new MyCost(10), new MyCost(19), new MyCost(8), new MyCost(15) },
                { new MyCost(10), new MyCost(18), new MyCost(7), new MyCost(17) },
                { new MyCost(13), new MyCost(16), new MyCost(9), new MyCost(14) },
                { new MyCost(12), new MyCost(19), new MyCost(8), new MyCost(18) }
            };

            var munkres = new AssignmentOracle<MyCost>(
                costMatrix,
                (a, b) => a.CompareTo(b) <= 0 ? a : b,
                (a, b) => a - b,
                (a, b) => a + b
            );

            int[] assignments = munkres.FindAssignments();

            Assert.AreEqual(assignments, [2, 1, 3, 0]);
        }

        [TestMethod]
        public void TestFindAssignmentsEmptyMatrix()
        {
            int[,] costMatrix = { };

            var munkres = new AssignmentOracle<int>(
                costMatrix,
                (a, b) => Math.Min(a, b),
                (a, b) => a - b,
                (a, b) => a + b
            );

            int[] assignments = munkres.FindAssignments();

            Assert.AreEqual(assignments, Array.Empty<int>());
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))] // Use ExpectedException attribute
        public void TestFindAssignmentsNonSquareMatrix()
        {
            int[,] costMatrix = {
                { 10, 19, 8 },
                { 10, 18, 7 },
                { 13, 16, 9 },
                { 12, 19, 8 }
            };

            Assert.ThrowsException<ArgumentException>(() =>
            {
                new AssignmentOracle<int>(
                    costMatrix,
                    (a, b) => Math.Min(a, b),
                    (a, b) => a - b,
                    (a, b) => a + b
                );
            });
        }
    }
}
