namespace CacheObject.Providers
{
    /// <summary>
    /// An interface for a real provider.
    /// </summary>
    /// <typeparam name="T"></typeparam>
    public interface IRealProvider<T>
    {
        /// <summary>
        /// Get object from data source
        /// </summary>
        Task<T> GetObjectAsync(T obj);
    }
}