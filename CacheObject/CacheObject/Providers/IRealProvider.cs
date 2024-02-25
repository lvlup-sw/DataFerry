namespace CacheObject.Providers
{
    public interface IRealProvider<T>
    {
        /// <summary>
        /// Get object from data source
        /// </summary>
        Task<T> GetObjectAsync(T obj);
    }
}