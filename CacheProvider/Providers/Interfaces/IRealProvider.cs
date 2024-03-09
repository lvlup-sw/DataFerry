namespace CacheProvider.Providers.Interfaces
{
    /// <summary>
    /// An interface for the real provider.
    /// </summary>
    public interface IRealProvider<T>
    {
        /// <summary>
        /// Asynchronousy get data from data source
        /// </summary>
        Task<T> GetAsync(T data);

        /// <summary>
        /// Get data from data source
        /// </summary>
        T Get(T data);
    }
}