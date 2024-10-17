using System.Collections.Concurrent;

namespace lvlup.DataFerry.Algorithms
{
    /// <summary>
    /// Represents a Count-Min Sketch data structure.
    /// </summary>
    /// <remarks>This is a probabilistic data structure which measures the frequency of events in a stream.</remarks>
    /// <typeparam name="T">The type of item to count. Must be a non-nullable type.</typeparam>
    public class CountMinSketch<T> : IDisposable where T : notnull
    {
        /// <summary>
        /// The 2D array representing the sketch.
        /// </summary>
        private ConcurrentDictionary<int, int>[] _sketch;

        /// <summary>
        /// The number of hash functions used.
        /// </summary>
        private readonly int _numHashes;

        /// <summary>
        /// The width of the backing arrays.
        /// </summary>
        private readonly int _width;

        /// <summary>
        /// The default percentage for error rate and error probability.
        /// </summary>
        private const double DefaultPercentage = 0.01;

        /// <summary>
        /// Gets or sets the error rate for the sketch.
        /// </summary>
        public double ErrorRate { get; set; }

        /// <summary>
        /// Gets or sets the error probability for the sketch.
        /// </summary>
        public double ErrorProbability { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="CountMinSketch{T}"/> class.
        /// </summary>
        /// <param name="maxSize">The maximum expected number of distinct items.</param>
        /// <param name="errorRate">The desired error rate (optional). Defaults to 0.01.</param>
        /// <param name="errorProbability">The desired error probability (optional). Defaults to 0.01.</param>
        public CountMinSketch(int maxSize, double? errorRate = default, double? errorProbability = default)
        {
            // Setup bounds
            ErrorRate = errorRate ?? DefaultPercentage;
            ErrorProbability = errorProbability ?? DefaultPercentage;
            var (width, numHashes) = CalculateOptimalDimensions(maxSize);

            // Init backing fields
            _sketch = new ConcurrentDictionary<int, int>[numHashes];
            _sketch = Enumerable.Range(0, numHashes)
                .Select(i => new ConcurrentDictionary<int, int>())
                .ToArray();
            _width = width;
            _numHashes = numHashes;
        }

        /// <summary>
        /// Inserts an item into the Count-Min Sketch.
        /// </summary>
        /// <param name="item">The item to insert.</param>
        public void Insert(T item)
        {
            var initialHash = HashGenerator.GenerateHash(item);
            for (int i = 0; i < _numHashes; i++)
            {
                var slot = GetSlot(i, initialHash);
                _sketch[i].AddOrUpdate(slot, 1, (k, existingCount) => existingCount + 1);
            }
        }

        /// <summary>
        /// Queries the Count-Min Sketch for the approximate count of an item.
        /// </summary>
        /// <param name="item">The item to query.</param>
        /// <returns>The approximate count of the item.</returns>
        public int Query(T item)
        {
            var initialHash = HashGenerator.GenerateHash(item);

            return Enumerable.Range(0, _numHashes)
                .Select(i =>
                {
                    var slot = GetSlot(i, initialHash);
                    return _sketch[i].TryGetValue(slot, out int count) ? count : 0;
                })
                .DefaultIfEmpty(0)
                .Min();
        }

        /// <summary>
        /// Calculates the slot index for a given hash function and initial hash value.
        /// </summary>
        /// <param name="i">The index of the hash function.</param>
        /// <param name="initialHash">The initial hash value of the item.</param>
        /// <returns>The slot index.</returns>
        private int GetSlot(int i, uint initialHash)
        {
            unchecked
            {
                long slot = ((i + 1) * initialHash) % _width;
                return (int) slot;
            }
        }

        /// <summary>
        /// Calculates the optimal dimensions (width and number of hash functions) for the Count-Min Sketch.
        /// </summary>
        /// <remarks>Calculation is based on the max size, <see cref="ErrorRate"/>, and <see cref="ErrorProbability"/> tolerances.</remarks>
        /// <param name="maxSize">The maximum expected number of distinct items.</param>
        /// <returns>A tuple containing the optimal width and number of hash functions.</returns>
        internal (int width, int numHashes) CalculateOptimalDimensions(int maxSize)
        {
            int width = (int)Math.Ceiling(Math.E * maxSize / ErrorRate);
            int numHashes = (int)Math.Ceiling(Math.Log(1.0 / ErrorProbability));
            return (width, numHashes);
        }

        /// <summary>
        /// IDisposable member.
        /// </summary>
        private bool _disposedValue;

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposedValue)
            {
                if (disposing)

                {
                    // Dispose managed state (managed objects)
                    foreach (var dictionary in _sketch)
                    {
                        dictionary.Clear();
                    }
                }

                _sketch = default!;

                _disposedValue = true;
            }
        }

        // Override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
        ~CountMinSketch()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: false);
        }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
    }
}
