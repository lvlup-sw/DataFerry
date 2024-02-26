namespace MockCachingOperation.Process
{
    /// <summary>
    /// Example payload object
    /// </summary>
    public class Payload
    {
        /// <summary>
        /// Unique identifier of payload
        /// </summary>
        public required string Identifier { get; set; }

        /// <summary>
        /// The payload: an arbitrary data object
        /// </summary>
        public required List<string> Data { get; set; }

        /// <summary>
        /// Arbitrary property of payload
        /// </summary>
        public bool Property { get; set; }

        /// <summary>
        /// Arbitrary version number of payload
        /// </summary>
        public decimal Version { get; set; }
    }
}
