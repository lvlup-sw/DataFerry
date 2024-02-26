namespace CacheObject.Caches
{
    public interface ICache<T>
    {
        Task<T> GetItemAsync(string key);

        Task SetItemAsync(string key, T item);

        Task RemoveItemAsync(string key);

        int GetItemCount();

        T GetData(string key);

        List<Tuple<string, T>> GetItems();
    }
}
