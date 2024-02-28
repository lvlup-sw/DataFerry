namespace CacheProvider.Providers
{
    /// <summary>
    /// An interface for an arbitrary payload.
    /// </summary>
    public interface IPayload
    {
        /// <summary>
        /// Unique identifier of payload
        /// </summary>
        string Identifier { get; set; }

        /// <summary>
        /// Arbitrary data object: in this case, a list of strings
        /// </summary>
        List<string> Data { get; set; }

        /// <summary>
        /// Arbitrary property of payload
        /// </summary>
        bool Property { get; set; }

        /// <summary>
        /// Arbitrary version number of payload
        /// </summary>
        decimal Version { get; set; }
    }
}

