using lvlup.DataFerry.Caching.Abstractions;

using Microsoft.Extensions.Caching.Memory;

namespace lvlup.DataFerry.Caching.Events;

public class CacheEventArgs<TKey>(TKey key) : EventArgs where TKey : notnull { public TKey Key { get; } = key; }
public class CacheHitEventArgs<TKey>(TKey key) : CacheEventArgs<TKey>(key) where TKey : notnull { }
public class CacheMissEventArgs<TKey>(TKey key) : CacheEventArgs<TKey>(key) where TKey : notnull { }
public class CacheItemAddedEventArgs<TKey, TValue>(TKey key, TValue value) : CacheEventArgs<TKey>(key) where TKey : notnull { public TValue Value { get; } = value; /* Consider adding Size if tracked */ }
public class CacheItemRemovedEventArgs<TKey>(TKey key) : CacheEventArgs<TKey>(key) where TKey : notnull { }
public class CacheItemEvictedEventArgs<TKey, TValue>(TKey key, ReplacementPolicyCache<TKey, TValue>.ReplacementPolicyEvictionReason reason) where TKey : notnull { public ReplacementPolicyCache<TKey, TValue>.ReplacementPolicyEvictionReason Reason { get; } = reason; }
public class CacheItemExpiredEventArgs<TKey>(TKey key) : CacheEventArgs<TKey>(key) where TKey : notnull { }