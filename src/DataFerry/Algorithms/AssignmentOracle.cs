// TODO:
//   - Verify functionality (unit tests)
//   - Benchmark operations
//   - Introduce optimizations (data structures, LINQ)
namespace DataFerry.Algorithms
{
    /// <summary>
    /// A generic implementation of the Hungarian algorithm (also known as the Kuhn-Munkres algorithm) for solving the assignment problem. 
    /// This class finds the optimal assignment of agents to tasks in a way that minimizes the total cost, where the cost
    /// of assigning an agent to a task is given by a cost matrix. This is also known as combinatorial optimization.
    /// </summary>
    /// <typeparam name="T">The type of the elements in the cost matrix. Must implement <see cref="IComparable{T}"/>.</typeparam>
    public class AssignmentOracle<T> where T : IComparable<T>
    {
        private readonly T[,] _costMatrix;
        private readonly Func<T, T, T> _subtractFunc;
        private readonly Func<T, T, T> _addFunc;
        private readonly Func<T, T, T> _minFunc;
        private bool[]? _rowsCovered;
        private bool[]? _colsCovered;
        private HashSet<Location>? _starredZeros;
        private readonly StackArrayPool<bool>? _boolArrayPool;

        /// <summary>
        /// Initializes a new instance of the <see cref="AssignmentOracle{T}"/> class.
        /// </summary>
        /// <param name="costMatrix">The cost matrix representing the costs of assigning agents to tasks.</param>
        /// <param name="subtractFunc">A function that performs subtraction on two values of type <typeparamref name="T"/>.</param>
        /// <param name="addFunc">A function that performs addition on two values of type <typeparamref name="T"/>.</param>
        /// <param name="minFunc">A function that returns the minimum of two values of type <typeparamref name="T"/>.</param>
        /// <param name="boolArrayPool">An optional <see cref="StackArrayPool{T}"/> instance for renting boolean arrays. 
        /// If not provided, arrays will be allocated directly.</param>
        /// <exception cref="ArgumentNullException">Thrown if any of the input parameters are null or if the cost matrix is not square.</exception>
        public AssignmentOracle(
            T[,] costMatrix,
            Func<T, T, T> subtractFunc,
            Func<T, T, T> addFunc,
            Func<T, T, T> minFunc,
            StackArrayPool<bool>? boolArrayPool = null)
        {
            ArgumentNullException.ThrowIfNull(costMatrix);
            ArgumentNullException.ThrowIfNull(subtractFunc);
            ArgumentNullException.ThrowIfNull(addFunc);
            ArgumentNullException.ThrowIfNull(minFunc);
            ThrowArgumentExceptionIfMatrixNotSquare(costMatrix);

            _costMatrix = costMatrix;
            _subtractFunc = subtractFunc;
            _addFunc = addFunc;
            _minFunc = minFunc;
            _boolArrayPool = boolArrayPool;
        }

        /// <summary>
        /// Represents a location within the cost matrix, specified by its row and column indices.
        /// </summary>
        /// <param name="row">The row index of the location.</param>
        /// <param name="col">The column index of the location.</param>
        private struct Location(int row, int col)
        {
            public int row = row;
            public int column = col;
        }

        /// <summary>
        /// Finds the optimal assignments of agents to tasks to minimize the total cost.
        /// </summary>
        /// <returns>An array where each element represents the task assigned to the corresponding agent. 
        /// A value of -1 indicates that the agent is not assigned to any task. 
        /// Returns an empty array if an error occurs during the assignment process.</returns>
        public int[] FindAssignments()
        {
            try
            {
                // 1. Initialization
                Initialize();

                // 2. Row reduction and Column Reduction
                ReduceRowsAndColumns();

                // 3. Augmenting path algorithm
                FindAugmentingPaths();

                // 4.  Prepare the result
                return ExtractAssignments();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in FindAssignments: {ex.GetBaseException().Message}");
                return Array.Empty<int>();
            }
            finally
            {
                // Return the rented buffers
                if (_boolArrayPool is not null)
                {
                    _boolArrayPool.Return(_rowsCovered!);
                    _boolArrayPool.Return(_colsCovered!);
                }
            }
        }

        private void Initialize()
        {
            int rows = _costMatrix.GetLength(0);
            int cols = _costMatrix.GetLength(1);

            _rowsCovered = _boolArrayPool is not null
                ? _boolArrayPool.Rent(rows)
                : new bool[rows];

            _colsCovered = _boolArrayPool is not null
                ? _boolArrayPool.Rent(cols)
                : new bool[cols];

            _starredZeros = [];

            // Initialize other arrays to default values (false for bool arrays)
            Array.Fill(_colsCovered, false);
        }

        private void ReduceRowsAndColumns()
        {
            int rows = _costMatrix.GetLength(0);
            int cols = _costMatrix.GetLength(1);

            // Row reduction
            for (int i = 0; i < rows; i++)
            {
                // Find the minimum element in row i
                T rowMin = _costMatrix[i, 0];
                for (int j = 1; j < cols; j++)
                {
                    rowMin = _minFunc(rowMin, _costMatrix[i, j]);
                }

                // Subtract the minimum from all elements in row i
                for (int j = 0; j < cols; j++)
                {
                    _costMatrix[i, j] = _subtractFunc(_costMatrix[i, j], rowMin);
                }
            }

            // Column reduction
            for (int j = 0; j < cols; j++)
            {
                // Find the minimum element in column j
                T colMin = _costMatrix[0, j];
                for (int i = 1; i < rows; i++)
                {
                    colMin = _minFunc(colMin, _costMatrix[i, j]);
                }

                // Subtract the minimum from all elements in column j
                for (int i = 0; i < rows; i++)
                {
                    _costMatrix[i, j] = _subtractFunc(_costMatrix[i, j], colMin);
                }
            }
        }

        private void FindAugmentingPaths()
        {
            int rows = _costMatrix.GetLength(0);
            int cols = _costMatrix.GetLength(1);

            var uncoveredZeros = new PriorityQueue<Location, T>();

            while (true)
            {
                CoverColumnsWithStars();

                if (AreAllColumnsCovered(cols))
                {
                    break;
                }

                uncoveredZeros.Clear();
                for (int i = 0; i < rows; i++)
                {
                    for (int j = 0; j < cols; j++)
                    {
                        if (_costMatrix[i, j].CompareTo(default!) == 0 && !_rowsCovered![i] && !_colsCovered![j])
                        {
                            uncoveredZeros.Enqueue(new Location(i, j), _costMatrix[i, j]);
                        }
                    }
                }

                while (uncoveredZeros.Count > 0)
                {
                    Location uncoveredZero = uncoveredZeros.Dequeue();
                    int starredCol = FindStarInRow(uncoveredZero.row);

                    if (starredCol != -1)
                    {
                        _rowsCovered![uncoveredZero.row] = true;
                        _colsCovered![starredCol] = false;
                        uncoveredZeros = new PriorityQueue<Location, T>(uncoveredZeros.UnorderedItems.Where(
                            item => item.Element.row != uncoveredZero.row));
                    }
                    else
                    {
                        AugmentPath(uncoveredZero);
                        if (AreAllRowsAssigned(rows))
                        {
                            return;
                        }
                        break;
                    }
                }

                AdjustCostMatrix(rows, cols);
            }
        }

        private int[] ExtractAssignments()
        {
            int rows = _costMatrix.GetLength(0);
            int cols = _costMatrix.GetLength(1);
            int[] result = new int[rows];

            for (int i = 0; i < rows; i++)
            {
                result[i] = -1; // Initialize with -1 to indicate no assignment
                for (int j = 0; j < cols; j++)
                {
                    if (_costMatrix[i, j].CompareTo(default!) == 0 && _starredZeros!.Contains(new Location(i, j)))
                    {
                        result[i] = j;
                        break;
                    }
                }
            }

            return result;
        }

        // Helper methods
        private static void ThrowArgumentExceptionIfMatrixNotSquare(T[,] costMatrix)
        {
            if (costMatrix.GetLength(0) != costMatrix.GetLength(1))
            {
                throw new ArgumentException("Cost matrix must be square.", nameof(costMatrix));
            }
        }

        private bool AreAllRowsAssigned(int rows)
        {
            for (int i = 0; i < rows; i++)
            {
                bool assigned = false;
                for (int j = 0; j < _costMatrix.GetLength(1); j++)
                {
                    if (_costMatrix[i, j].CompareTo(default!) == 0 && _starredZeros!.Contains(new Location(i, j)))
                    {
                        assigned = true;
                        break;
                    }
                }
                if (!assigned)
                {
                    return false;
                }
            }
            return true;
        }

        private void CoverColumnsWithStars()
        {
            int rows = _costMatrix.GetLength(0);
            int cols = _costMatrix.GetLength(1);

            for (int i = 0; i < rows; i++)
            {
                for (int j = 0; j < cols; j++)
                {
                    if (_costMatrix[i, j].CompareTo(default!) == 0 && _starredZeros!.Contains(new Location(i, j)))
                    {
                        _colsCovered![j] = true;
                        break;
                    }
                }
            }
        }

        private bool AreAllColumnsCovered(int cols)
        {
            for (int j = 0; j < cols; j++)
            {
                if (!_colsCovered![j])
                {
                    return false;
                }
            }
            return true;
        }

        private int FindStarInRow(int row)
        {
            int cols = _costMatrix.GetLength(1);
            for (int j = 0; j < cols; j++)
            {
                if (_costMatrix[row, j].CompareTo(default!) == 0 && _starredZeros!.Contains(new Location(row, j)))
                {
                    return j;
                }
            }
            return -1;
        }

        private void AugmentPath(Location loc)
        {
            int row = loc.row;
            int col = loc.column;
            Location[] path = new Location[_costMatrix.GetLength(0) * _costMatrix.GetLength(1)];
            int pathLength = 0;
            path[pathLength++] = loc;

            while (true)
            {
                row = FindStarInColumn(col);
                if (row == -1)
                {
                    break;
                }
                path[pathLength++] = new Location(row, col);
                col = FindZeroInRow(row);
                path[pathLength++] = new Location(row, col);
            }

            // Invert the path (toggle stars)
            for (int i = 0; i < pathLength; i++)
            {
                Location pathLoc = path[i];

                // Toggle starred status in the HashSet
                if (!_starredZeros!.Remove(pathLoc))
                {
                    _starredZeros.Add(pathLoc);
                }
            }
        }

        private void AdjustCostMatrix(int rows, int cols)
        {
            // Find the smallest uncovered value
            T minValue = FindMinimumUncoveredValue(rows, cols);

            // Add minValue to every covered row
            for (int i = 0; i < rows; i++)
            {
                if (_rowsCovered![i])
                {
                    for (int j = 0; j < cols; j++)
                    {
                        _costMatrix[i, j] = _addFunc(_costMatrix[i, j], minValue);
                    }
                }
            }

            // Subtract minValue from every uncovered column
            for (int j = 0; j < cols; j++)
            {
                if (!_colsCovered![j])
                {
                    for (int i = 0; i < rows; i++)
                    {
                        _costMatrix[i, j] = _subtractFunc(_costMatrix[i, j], minValue);
                    }
                }
            }
        }

        private int FindStarInColumn(int col)
        {
            int rows = _costMatrix.GetLength(0);
            for (int i = 0; i < rows; i++)
            {
                if (_costMatrix[i, col].CompareTo(default!) == 0 && _starredZeros!.Contains(new Location(i, col)))
                {
                    return i;
                }
            }
            return -1;
        }

        private int FindZeroInRow(int row)
        {
            int cols = _costMatrix.GetLength(1);
            for (int j = 0; j < cols; j++)
            {
                if (_costMatrix[row, j].CompareTo(default!) == 0)
                {
                    return j;
                }
            }
            return -1;
        }

        private T FindMinimumUncoveredValue(int rows, int cols)
        {
            T minValue = default!;
            bool minValueSet = false;

            for (int i = 0; i < rows; i++)
            {
                for (int j = 0; j < cols; j++)
                {
                    if (!_rowsCovered![i] && !_colsCovered![j])
                    {
                        if (!minValueSet)
                        {
                            minValue = _costMatrix[i, j];
                            minValueSet = true;
                        }
                        else
                        {
                            minValue = _minFunc(minValue, _costMatrix[i, j]);
                        }
                    }
                }
            }

            return minValue;
        }
    }
}
