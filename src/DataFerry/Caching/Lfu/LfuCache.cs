using System.Diagnostics.CodeAnalysis;
using lvlup.DataFerry.Caching.Abstractions;

namespace lvlup.DataFerry.Caching.Lfu;

/// <summary>
/// Example concrete LFU cache implementation using Redis as backing store via IRedisClient.
/// Manages LFU metadata in memory, stores values (as bytes) in Redis.
/// </summary>
public class LfuCache<TKey, TValue> : ReplacementPolicyCache<TKey, TValue>
    where TKey : notnull
{
    public override string Name { get; }

    public override int Capacity { get; }

    public override bool TryGet(TKey key, [MaybeNullWhen(false)] out TValue value)
    {
        throw new NotImplementedException();
    }

    public override void Set(TKey key, TValue value)
    {
        throw new NotImplementedException();
    }

    public override void Set(TKey key, TValue value, TimeSpan timeToExpire)
    {
        throw new NotImplementedException();
    }

    public override TValue GetOrSet<TState>(TKey key, TState state, Func<TKey, TState, TValue> factory)
    {
        throw new NotImplementedException();
    }

    public override TValue GetOrSet<TState>(TKey key, TState state, Func<TKey, TState, (TValue value, TimeSpan timeToExpire)> factory)
    {
        throw new NotImplementedException();
    }

    public override TValue GetOrSet(TKey key, Func<TKey, TValue> factory)
    {
        throw new NotImplementedException();
    }

    public override TValue GetOrSet(TKey key, Func<TKey, (TValue value, TimeSpan timeToExpire)> factory)
    {
        throw new NotImplementedException();
    }

    public override int GetCount()
    {
        throw new NotImplementedException();
    }

    public override bool Remove(TKey key)
    {
        throw new NotImplementedException();
    }

    public override bool Remove(TKey key, TValue value)
    {
        throw new NotImplementedException();
    }

    public override void RemoveExpiredEntries()
    {
        throw new NotImplementedException();
    }

    public override IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
    {
        throw new NotImplementedException();
    }
}