namespace CacheProvider.Providers
{
    /// <summary>
    /// An interface for the real provider.
    /// </summary>
    public interface IRealProvider<T>
    {
        /// <summary>
        /// Get item from data source
        /// </summary>
        Task<T> GetItemAsync(T item);
    }
}