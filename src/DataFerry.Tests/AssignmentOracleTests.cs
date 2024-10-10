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
        public void ReduceRowsAndColumns_IntMatrix_ReducesCorrectly()
        {
            // Arrange
            int[,] costMatrix = new int[,] {
                { 10, 2, 15 },
                { 4, 8, 1 },
                { 7, 6, 9 }
            };
            var oracle = new AssignmentOracle<int>(
                costMatrix,
                (a, b) => a - b,
                (a, b) => a + b,
                Math.Min
            );

            // Act
            oracle.ReduceRowsAndColumns();

            // Assert
            int[,] expectedMatrix = new int[,] {
                { 8, 0, 13 },
                { 3, 7, 0 },
                { 1, 0, 3 }
            };

            // Flatten the arrays
            var actualFlattened = oracle.GetMatrix().Cast<int>().ToArray();
            var expectedFlattened = expectedMatrix.Cast<int>().ToArray();

            // Assert equivalence of flattened arrays
            CollectionAssert.AreEquivalent(expectedFlattened, actualFlattened);
        }

        [TestMethod]
        public void ReduceRowsAndColumns_MyCostMatrix_ReducesCorrectly()
        {
            // Arrange
            MyCost[,] costMatrix = {
                { new MyCost(10), new MyCost(19), new MyCost(8), new MyCost(15) },
                { new MyCost(10), new MyCost(18), new MyCost(7), new MyCost(17) },
                { new MyCost(13), new MyCost(16), new MyCost(9), new MyCost(14) },
                { new MyCost(12), new MyCost(19), new MyCost(8), new MyCost(18) }
            };

            var oracle = new AssignmentOracle<MyCost>(
                costMatrix,
                (a, b) => a.CompareTo(b) <= 0 ? a : b,
                (a, b) => a - b,
                (a, b) => a + b
            );

            // Act
            oracle.ReduceRowsAndColumns();

            // Assert
            MyCost[,] expectedMatrix = {
                { new MyCost(2), new MyCost(11), new MyCost(0), new MyCost(7) },
                { new MyCost(3), new MyCost(11), new MyCost(0), new MyCost(10) },
                { new MyCost(4), new MyCost(7), new MyCost(0), new MyCost(5) },
                { new MyCost(4), new MyCost(11), new MyCost(0), new MyCost(10) }
            };

            // Flatten the arrays
            var actualFlattened = oracle.GetMatrix().Cast<MyCost>().Select(c => c.Value).ToArray();
            var expectedFlattened = expectedMatrix.Cast<MyCost>().Select(c => c.Value).ToArray();

            // Assert equivalence of flattened arrays
            CollectionAssert.AreEquivalent(expectedFlattened, actualFlattened);
        }

        [TestMethod]
        public void ReduceRowsAndColumns_DoubleMatrix_ReducesCorrectly()
        {
            // Arrange
            double[,] costMatrix = new double[,] {
                { 2.5, 1.2, 3.1 },
                { 0.8, 2.0, 1.5 },
                { 1.7, 0.9, 2.8 }
            };
            var oracle = new AssignmentOracle<double>(
                costMatrix,
                (a, b) => a - b,
                (a, b) => a + b,
                Math.Min
            );

            // Act
            oracle.ReduceRowsAndColumns();

            // Assert
            double[,] expectedMatrix = new double[,] {
                { 1.3, 0, 1.9 },
                { 0, 1.2, 0.7 },
                { 0.8, 0, 1.9 }
            };

            // Flatten the arrays
            var actualFlattened = oracle.GetMatrix().Cast<double>().ToArray();
            var expectedFlattened = expectedMatrix.Cast<double>().ToArray();

            // Assert equivalence of flattened arrays
            double tolerance = 1e-10;
            for (int i = 0; i < actualFlattened.Length; i++)
            {
                Assert.AreEqual(expectedFlattened[i], actualFlattened[i], tolerance);
            }
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
                (a, b) => a - b,
                (a, b) => a + b,
                Math.Min
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
