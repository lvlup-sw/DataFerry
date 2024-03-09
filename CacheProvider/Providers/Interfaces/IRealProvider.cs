namespace CacheProvider.Providers.Interfaces
{
    /// <summary>
    /// An interface for the real provider.
    /// </summary>
    public interface IRealProvider<T>
    {
        /// <summary>
        /// Asynchronousy get  from data source
        /// </summary>
        Task<T> GetAsync(T );

        /// <summary>
        /// Get  from data source
        /// </summary>
        T Get(T );
    }
}