namespace lvlup.DataFerry.Algorithms
{
    public class CountMinSketch<T> where T : notnull
    {
        private readonly int[][] sketch;
        private readonly int numHashes;
        private const double DefaultPercentage = 0.01;

        public double ErrorRate { get; set; }
        public double ErrorProbability { get; set; }

        public CountMinSketch(int maxSize, double? errorRate = default, double? errorProbability = default)
        {
            // Setup bounds
            ErrorRate = errorRate ?? DefaultPercentage;
            ErrorProbability = errorProbability ?? DefaultPercentage;
            var (width, numHashes) = CalculateOptimalDimensions(maxSize);

            // Init backing fields
            sketch = new int[numHashes][];
            for (var i = 0; i < numHashes; i++)
            {
                sketch[i] = new int[width];
            }
            this.numHashes = numHashes;
        }

        public void Insert(T item)
        {
            var initialHash = HashGenerator.GenerateHash(item);
            for (int i = 0; i < numHashes; i++)
            {
                var slot = GetSlot(i, initialHash);
                sketch[i][slot]++;
            }
        }

        public int Query(T item)
        {
            var initialHash = HashGenerator.GenerateHash(item);
            var min = int.MaxValue;
            for (int i = 0; i < numHashes; i++)
            {
                var slot = GetSlot(i, initialHash);
                min = Math.Min(sketch[i][slot], min);
            }
            return min;
        }

        private int GetSlot(int i, uint initialHash)
        {
            return (int)Math.Abs((i + 1) * initialHash) % sketch[0].Length;
        }

        private (int width, int numHashes) CalculateOptimalDimensions(int maxSize)
        {
            int width = (int)Math.Ceiling(Math.E * maxSize / ErrorRate);
            int numHashes = (int)Math.Ceiling(Math.Log(1.0 / ErrorProbability));
            return (width, numHashes);
        }
    }
}
