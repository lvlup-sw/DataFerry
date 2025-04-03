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

        public int CompareTo(Payload other)
        {
            if (other == null) return 1; // Consider current object greater if other is null

            // Compare Identifier
            int identifierComparison = Identifier.CompareTo(other.Identifier);
            if (identifierComparison != 0) return identifierComparison;

            // Compare Property
            int propertyComparison = Property.CompareTo(other.Property);
            if (propertyComparison != 0) return propertyComparison;

            // Compare Version
            int versionComparison = Version.CompareTo(other.Version);
            if (versionComparison != 0) return versionComparison;

            // Compare Data lengths
            int dataLengthComparison = Data.Length.CompareTo(other.Data.Length);
            if (dataLengthComparison != 0) return dataLengthComparison;

            // Compare Data elements (assuming Data is an array of comparable elements)
            for (int i = 0; i < Data.Length; i++)
            {
                int dataComparison = Data[i].CompareTo(other.Data[i]);
                if (dataComparison != 0) return dataComparison;
            }

            // If all properties are equal
            return 0;
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
