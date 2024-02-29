namespace CacheProvider.Providers
{
    /// <summary>
    /// An interface for the real provider.
    /// </summary>
    public interface IRealProvider<T>
    {
        /// <summary>
        /// Asynchronousy get item from data source
        /// </summary>
        Task<T> GetItemAsync(T item);

        /// <summary>
        /// Get item from data source
        /// </summary>
        T GetItem(T item);
    }
}