using System.Diagnostics.CodeAnalysis;
using lvlup.DataFerry.Caching.Abstractions;
using lvlup.DataFerry.Caching.Results;

namespace lvlup.DataFerry.Caching.Lfu;

/// <summary>
/// Example concrete LFU cache implementation using Redis as backing store via IRedisClient.
/// Manages LFU metadata in memory, stores values (as bytes) in Redis.
/// </summary>
public class DistributedLfuCache<TKey, TValue> : DistributedReplacementPolicyCache<TKey, TValue>
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

    public override ValueTask<TryGetResult<TValue>> TryGetValueAsync(TKey key, CancellationToken token = default)
    {
        throw new NotImplementedException();
    }

    public override ValueTask SetAsync(TKey key, TValue value, CancellationToken token = default)
    {
        throw new NotImplementedException();
    }

    public override ValueTask SetAsync(TKey key, TValue value, TimeSpan timeToExpire, CancellationToken token = default)
    {
        throw new NotImplementedException();
    }

    public override ValueTask<TValue> GetOrSetAsync<TState>(TKey key, TState state, Func<TKey, TState, CancellationToken, Task<TValue>> factory, CancellationToken token = default)
    {
        throw new NotImplementedException();
    }

    public override ValueTask<TValue> GetOrSetAsync<TState>(TKey key, TState state, Func<TKey, TState, CancellationToken, Task<(TValue value, TimeSpan timeToExpire)>> factory, CancellationToken token = default)
    {
        throw new NotImplementedException();
    }

    public override ValueTask<TValue> GetOrSetAsync(TKey key, Func<TKey, CancellationToken, Task<TValue>> factory, CancellationToken token = default)
    {
        throw new NotImplementedException();
    }

    public override ValueTask<TValue> GetOrSetAsync(TKey key, Func<TKey, CancellationToken, Task<(TValue value, TimeSpan timeToExpire)>> factory, CancellationToken token = default)
    {
        throw new NotImplementedException();
    }

    public override ValueTask<bool> RemoveAsync(TKey key, CancellationToken token = default)
    {
        throw new NotImplementedException();
    }

    public override ValueTask<bool> RemoveAsync(TKey key, TValue value, CancellationToken token = default)
    {
        throw new NotImplementedException();
    }

    public override Task RemoveExpiredEntriesAsync(CancellationToken token = default)
    {
        throw new NotImplementedException();
    }
}