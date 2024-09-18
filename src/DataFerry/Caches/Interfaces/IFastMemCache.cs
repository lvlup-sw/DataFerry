namespace DataFerry.Caches.Interfaces
{
    public interface IFastMemCache<TKey, TValue> : IEnumerable<KeyValuePair<TKey, TValue>>, IDisposable where TKey : notnull
    {
        int Count { get; }
        void Clear();
        void AddOrUpdate(TKey key, TValue value, TimeSpan ttl);
        bool TryGet(TKey key, out TValue value);
        TValue GetOrAdd(TKey key, Func<TKey, TValue> valueFactory, TimeSpan ttl);
        TValue GetOrAdd<TArg>(TKey key, Func<TKey, TArg, TValue> valueFactory, TimeSpan ttl, TArg factoryArgument);
        TValue GetOrAdd(TKey key, TValue value, TimeSpan ttl);
        void Remove(TKey key);
        void EvictExpired();
    }
}
