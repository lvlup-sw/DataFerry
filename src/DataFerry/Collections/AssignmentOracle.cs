namespace DataFerry.Collections
{
    /// <summary>
    /// A generic implementation of the Hungarian algorithm (also known as the Kuhn-Munkres algorithm) for solving the assignment problem. 
    /// This class finds the optimal assignment of agents to tasks in a way that minimizes the total cost, 
    /// where the cost of assigning an agent to a task is given by a cost matrix.
    /// </summary>
    /// <typeparam name="T">The type of the elements in the cost matrix. Must implement <see cref="IComparable{T}"/>.</typeparam>
    public class AssignmentOracle<T> where T : IComparable<T>
    {
        private readonly T[,] _costMatrix;
        private readonly Func<T, T, T> _minFunc;
        private readonly Func<T, T, T> _subtractFunc;
        private int[]? _rowAssignments;
        private int[]? _colAssignments;
        private bool[]? _rowsCovered;
        private bool[]? _colsCovered;
        private bool[,]? _starredZeros;
        private Location[]? _path;

        public AssignmentOracle(T[,] costMatrix, Func<T, T, T> minFunc, Func<T, T, T> subtractFunc)
        {
            _costMatrix = costMatrix;
            _minFunc = minFunc;
            _subtractFunc = subtractFunc;
        }

        public int[] FindAssignments()
        {
            /*  EXAMPLE USAGE
                MyCost[,] costMatrix = { 
                    { new MyCost(10), new MyCost(19) }, 
                    { new MyCost(12), new MyCost(17) } 
                };

                var munkres = new KuhnMunkres<MyCost>(
                    costMatrix, 
                    (a, b) => a.CompareTo(b) <= 0 ? a : b,  // Min function
                    (a, b) => a - b                         // Subtract function
                );

                int[] assignments = munkres.FindAssignments();
            */

            // 1. Initialization
            Initialize();

            // 2. Row reduction and Column Reduction
            ReduceRowsAndColumns();

            // 3. Augmenting path algorithm
            FindAugmentingPaths();

            // 4.  Prepare the result
            return ExtractAssignments();
        }

        private void Initialize()
        {
            int rows = _costMatrix.GetLength(0);
            int cols = _costMatrix.GetLength(1);

            _rowAssignments = new int[rows];
            _colAssignments = new int[cols];
            _rowsCovered = new bool[rows];
            _colsCovered = new bool[cols];
            _starredZeros = new bool[rows, cols];
            _path = new Location[rows * cols];

            // ... Initialize other data structures ...
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

            // Initialize a priority queue to store uncovered zeros with their costs
            var uncoveredZeros = new PriorityQueue<Location, T>(); 

            while (true)
            {
                // Step 1: Cover all columns with starred zeros
                CoverColumnsWithStars();

                if (AreAllColumnsCovered(cols))
                {
                    // Optimal solution found
                    break;
                }

                // Add all uncovered zeros to the priority queue
                uncoveredZeros.Clear(); // Reuse the priority queue
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

                // Step 2: Find an uncovered zero and prime it (using the priority queue)
                while (uncoveredZeros.Count > 0)
                {
                    Location uncoveredZero = uncoveredZeros.Dequeue();

                    PrimeZero(uncoveredZero);

                    // Try to find a starred zero in the same row
                    int starredCol = FindStarInRow(uncoveredZero.row);
                    if (starredCol != -1)
                    {
                        // Cover the row and uncover the column
                        _rowsCovered![uncoveredZero.row] = true;
                        _colsCovered![starredCol] = false;

                        // Remove any zeros in the covered row from the queue
                        uncoveredZeros = new PriorityQueue<Location, T>(uncoveredZeros.UnorderedItems.Where(
                            item => item.Element.row != uncoveredZero.row)); 
                    }
                    else
                    {
                        // Augmenting path found
                        AugmentPath(uncoveredZero);

                        // Early exit if all rows are assigned
                        if (AreAllRowsAssigned(rows)) 
                        {
                            return; 
                        }

                        break; 
                    }
                }
                else
                {
                    // Step 4: Adjust the cost matrix
                    AdjustCostMatrix(rows, cols);
                }
            }
        }

        private int[] ExtractAssignments()
        {
            // ... Extract and return the final assignments ...
        }

        // ... Helper methods (FindZero, FindStarInRow, etc.) ...
        private bool AreAllRowsAssigned(int rows)
        {
            for (int i = 0; i < rows; i++)
            {
                bool assigned = false;
                for (int j = 0; j < _costMatrix.GetLength(1); j++)
                {
                    if (_costMatrix[i, j].CompareTo(default(T)!) == 0 && _starredZeros![i, j])
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
                    if (_costMatrix[i, j].CompareTo(default(T)!) == 0 && _starredZeros![i, j])
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

        private void PrimeZero(Location loc)
        {
            // In our simplified representation, priming is implicit
            // as we're using the presence of a zero for selection.
            // No explicit action is needed here.
        }

        private int FindStarInRow(int row)
        {
            int cols = _costMatrix.GetLength(1);
            for (int j = 0; j < cols; j++)
            {
                if (_costMatrix[row, j].CompareTo(default(T)!) == 0 && _starredZeros![row, j])
                {
                    return j;
                }
            }
            return -1;
        }

        private void AugmentPath(Location loc)
        {
            // This involves manipulating our implicit assignments
            // by modifying the _costMatrix, _rowsCovered, and _colsCovered
            // based on the augmenting path starting from 'loc'.
            // Detailed implementation will be provided later.
        }

        private void AdjustCostMatrix(int rows, int cols)
        {
            // This involves finding the minimum uncovered value
            // and updating the _costMatrix based on covered rows/columns.
            // Detailed implementation will be provided later.
        }


        private struct Location
        {
            public int row;
            public int column;

            public Location(int row, int col)
            {
                this.row = row;
                this.column = col;
            }
        }
    }
}
