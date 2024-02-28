# CacheProvider

The `CacheProvider` class is a generic class that implements the `ICacheProvider<T>` interface. It is designed to provide caching functionality for any type of object. The class uses two types of caches: `LocalCache` and `DistributedCache`. It also uses the `IRealProvider<T>` interface to retrieve items from the real provider when they are not found in the cache.

## How to Instantiate

To instantiate the `CacheProvider` class, you need to provide three parameters:

1. An instance of a class that implements the `IRealProvider<T>` interface. This is the real provider from which items will be retrieved when they are not found in the cache.
2. A `CacheType` value. This determines the type of cache to be used. The `CacheType` is an enum with two values: `Local` and `Distributed`.
3. An instance of `IOptions<CacheSettings>`. This provides the settings for the cache.

Here is an example of how to instantiate the `CacheProvider` class:

```
// Using the Dependency Injection pattern
var provider = _serviceProvider.GetService<IRealProvider<Payload>>();
var settings = _serviceProvider.GetService<IOptions<CacheSettings>>();
var cache = CacheType.Distributed;

// Try to create the cache provider
CacheProvider<Payload> cacheProvider;
try
{
    cacheProvider = new(provider, cache, settings);
}
```

## Cache Implementations

### LocalCache

`LocalCache` is an in-memory caching implementation with asynchronous operations. It uses a `ConcurrentDictionary<string, (object value, DateTime timeStamp)>` object to store the cached items. Each item in the cache has a timestamp associated with it, and the cache invalidation is implemented using this timestamp and the `AbsoluteExpiration` setting.

### DistributedCache

`DistributedCache` is an implementation of `IDistributedCache` which uses the `ICache` interface as a base. It uses the Polly library to handle exceptions and retries. This implementation can be used with numerous distributed cache providers such as Redis, AWS ElastiCache, or Azure Blob Storage.

The `DistributedCache` class uses a `CacheSettings` object to configure the cache, and a `ConnectionMultiplexer` to connect to the cache provider. It also uses an `AsyncPolicyWrap<object>` to define a policy for handling exceptions when accessing the cache. This policy includes a retry policy and a fallback policy.

## Usage

Once you have an instance of the `CacheProvider` class, you can use the `CheckCacheAsync` method to check the cache for an item with a specified key. If the item is found in the cache, it is returned. If not, the item is retrieved from the real provider and then cached before being returned. Here is an example:

```
object item = new();
var key = "myKey";
var cachedItem = await cacheProvider.CheckCacheAsync(item, key);
```

## License

Cache-Provider is licensed under the [MIT License](https://opensource.org/licenses/MIT).
