using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
    }
}
