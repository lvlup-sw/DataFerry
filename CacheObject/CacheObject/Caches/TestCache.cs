namespace CacheObject.Caches
{
    public class TestCache<T> : ICache<T>
    {
        private readonly Dictionary<string, T> _data;

        public TestCache() => _data = [];

        public virtual async Task<T?> GetItemAsync(string key)
        {
            lock (_data)
            {
                return _data.TryGetValue(key, out T? value) ? value : default;
            }
        }

        public virtual async Task RemoveItemAsync(string key)
        {
            lock (_data)
            {
                _data.Remove(key);
            }
        }

        public virtual async Task SetItemAsync(string key, T item)
        {
            lock (_data)
            {
                _data.Add(key, item);
            }
        }

        public virtual List<Tuple<string, T>> GetItems()
        {
            lock (_data)
            {
                List<Tuple<string, T>> items = [];
                foreach (var item in _data)
                {
                    items.Add(new Tuple<string, T>(item.Key, item.Value));
                }
                return items;
            }
        }

        public virtual int GetItemCount() => _data.Count;

        public virtual T GetData(string key) => _data[key];
    }
}
