# CacheProvider

The `CacheProvider` class is a generic class that implements the `ICacheProvider<T>` interface. It is designed to provide caching functionality for any type of object. The class uses `DistributedCache` for caching and the `IRealProvider<T>` interface to retrieve data from the real provider when they are not found in the cache.

## How to Instantiate CacheProvider

To instantiate a `CacheProvider`, you need to provide the following:

1. An instance of `IConnectionMultiplexer` for connecting to the Redis server.
2. An instance of a class that implements the `IRealProvider<T>` interface. This is the "real provider" that the `CacheProvider` will use to retrieve s if they are not found in the cache.
3. An instance of `CacheSettings` to configure the cache.
4. An instance of `ILogger` for logging.

Here's an example of how to instantiate a `CacheProvider`:
```
// Get configuration
var connection  = _serviceProvider.GetRequiredService<IConnectionMultiplexer>();
var provider    = _serviceProvider.GetRequiredService<IRealProvider<Payload>>();
var settings    = _serviceProvider.GetRequiredService<IOptions<CacheSettings>>().Value;
var logger      = _serviceProvider.GetRequiredService<ILogger<YourClass>>();

// Create the cache provider
CacheProvider<Payload> cacheProvider = new(connection, provider, settings, logger);
...
```

Note that you'll need to inject an `IConnectionMultiplexer` into your service collection if you plan on using `DistributedCache`. EX:

```
services.AddSingleton<IConnectionMultiplexer>(serviceProvider =>
{
	return ConnectionMultiplexer.Connect(YourConnectionString);
});
```

## Cache Implementations

### DistributedCache

`DistributedCache` is an implementation that uses the `StackExchange.Redis` library as its foundation. It leverages the `Polly` library to handle exceptions and retries, making it robust and resilient. This implementation is compatible with various distributed Redis cache providers, including AWS ElastiCache and Azure Blob Storage.

The `DistributedCache` class requires a `CacheSettings` object for configuration and an `IConnectionMultiplexer` for connecting to the cache provider. It also uses an `AsyncPolicyWrap<object>` to define a policy for handling exceptions when accessing the cache. This policy includes a retry policy, which retries a specified number of times with a delay, and a fallback policy, which executes a fallback action if an exception is thrown. The class provides asynchronous operations for retrieving, adding, and removing data from the cache.

## Usage

Once you have an instance of the `CacheProvider` class, you can use the `GetFromCacheAsync`, `SetInCacheAsync`, `RemoveFromCacheAsync`, `GetBatchFromCacheAsync`, `SetBatchInCacheAsync`, or `RemoveBatchFromCacheAsync` methods to interact with the cache. Here is an example:

```
Payload data = new();
string key = "myKey";
Payload response = await cacheProvider.GetFromCacheAsync(data, key);
```

## Prerequisities

- .NET 8.0 SDK

## Dependencies

The project uses the following NuGet packages:
- Polly ~> 8.3.0
- StackExchange.Redis ~> 2.7.23
- Microsoft.EntityFrameworkCore ~> 8.0.2
- Microsoft.Extensions.Caching.StackExchangeRedis ~> 8.0.2
- Microsoft.Extensions.DependencyInjection ~> 8.0.0
- Microsoft.Extensions.Options ~> 8.0.2

## License

Cache-Provider is licensed under the [MIT License](https://opensource.org/licenses/MIT).
