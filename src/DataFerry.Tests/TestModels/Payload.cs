namespace lvlup.DataFerry.Tests.TestModels
{
    /// <summary>
    /// Class of the arbitrary payload.
    /// </summary>
    public class Payload
    {
        /// <summary>
        /// Unique identifier of payload
        /// </summary>
        public required string Identifier { get; set; }

        /// <summary>
        /// Arbitrary data object: in this case, a list of strings
        /// </summary>
        public required string Data { get; set; }

        /// <summary>
        /// Arbitrary property of payload
        /// </summary>
        public bool Property { get; set; }

        /// <summary>
        /// Arbitrary version number of payload
        /// </summary>
        public decimal Version { get; set; }

        // Comparison for tests
        public bool IsEquivalentTo(Payload other)
        {
            if (other is null) return false;

            // Compare primitive properties
            if (Identifier != other.Identifier ||
                Property != other.Property ||
                Version != other.Version)
            {
                return false;
            }

            // Compare the Data lists (ensuring same elements, regardless of order)
            if (Data.Length != other.Data.Length) return false;
            return Data.Equals(other.Data);
        }

        public static bool AreEquivalent(List<Payload> firstList, List<Payload> secondList)
        {
            if (firstList == null || secondList == null) return firstList == secondList;
            if (firstList.Count != secondList.Count) return false;

            bool identical = false;
            foreach (var item in firstList)
            {
                var matched = secondList.FirstOrDefault(item2 => item2.IsEquivalentTo(item));
                identical = matched is not null;
            }

            return identical;
        }
    }
}
