# CacheProvider

The `CacheProvider` class is a generic class that implements the `ICacheProvider<T>` interface. It is designed to provide caching functionality for any type of object. The class uses two types of caches: `LocalCache` and `DistributedCache`. It also uses the `IRealProvider<T>` interface to retrieve items from the real provider when they are not found in the cache. The type of cache to use is determined by the CacheType parameter passed to the CacheProvider constructor.

## How to Instantiate CacheProvider

To instantiate a `CacheProvider`, you need to provide the following:

1. An instance of a class that implements the `IRealProvider<T>` interface. This is the "real provider" that the `CacheProvider` will use to retrieve items if they are not found in the cache.
2. A `CacheType` value to specify the type of cache to use.
3. An instance of `CacheSettings` to configure the cache.
4. An instance of `ILogger` for logging.
5. An optional `IConnectionMultiplexer` instance for connecting to a Redis server (only required if using `CacheType.Distributed`).

Here's an example of how to instantiate a `CacheProvider`:


```
// Using the Dependency Injection pattern
var provider    = _serviceProvider.GetService<IRealProvider<YourPayload>>();
var appsettings = _serviceProvider.GetService<IOptions<AppSettings>>();
var settings   = _serviceProvider.GetService<IOptions<CacheSettings>>();
var logger      = _serviceProvider.GetService<ILogger<YourClass>>();
var connection  = _serviceProvider.GetService<IConnectionMultiplexer>() ?? null;

// Try to create the cache provider
CacheProvider<YourPayload> cacheProvider;
try
{
    cacheProvider = new(provider, cache, settings, logger, connection);
}
```

Note that you'll need to inject an `IConnectionMultiplexer` into your service collection if you plan on using `DistributedCache`. EX:

```
services.AddSingleton<IConnectionMultiplexer>(serviceProvider =>
{
	return ConnectionMultiplexer.Connect(YourConnectionString);
});
```

## Cache Implementations

### LocalCache

`LocalCache` is an in-memory caching implementation of `ILocalCache` that uses a `ConcurrentDictionary<string, (object value, DateTime timeStamp)>` to store cached items. Each item in the cache is associated with a timestamp, which is used in conjunction with the `AbsoluteExpiration` setting to manage cache invalidation. This class is designed as a singleton to prevent multiple instances from being created per process. It provides synchronous operations for retrieving, adding, and removing items from the cache.

### DistributedCache

`DistributedCache` is an implementation of `IDistributedCache` that uses the `StackExchange.Redis` library as its foundation. It leverages the `Polly` library to handle exceptions and retries, making it robust and resilient. This implementation is compatible with various distributed cache providers, including Redis, AWS ElastiCache, and Azure Blob Storage.

The `DistributedCache` class requires a `CacheSettings` object for configuration and an `IConnectionMultiplexer` for connecting to the cache provider. It also uses an `AsyncPolicyWrap<object>` to define a policy for handling exceptions when accessing the cache. This policy includes a retry policy, which retries a specified number of times with a delay, and a fallback policy, which executes a fallback action if an exception is thrown. The class provides asynchronous operations for retrieving, adding, and removing items from the cache.

## Usage

Once you have an instance of the `CacheProvider` class, you can use the `CheckCacheAsync` or `CheckCache` method to check the cache for an item with a specified key. If the item is found in the cache, it is returned. If not, the item is retrieved from the real provider and then cached before being returned. Here is an example:

```
object item = new();
var key = "myKey";
var cachedItem = await cacheProvider.CheckCacheAsync(item, key);
```

## Prerequisities

- .NET 8.0 SDK

## Dependencies

The project uses the following NuGet packages:
- Polly 8.3.0
- StackExchange.Redis 2.7.23
- Microsoft.EntityFrameworkCore 8.0.2
- Microsoft.Extensions.Caching.StackExchangeRedis 8.0.2
- Microsoft.Extensions.DependencyInjection 8.0.0
- Microsoft.Extensions.Options 8.0.2

## License

Cache-Provider is licensed under the [MIT License](https://opensource.org/licenses/MIT).
