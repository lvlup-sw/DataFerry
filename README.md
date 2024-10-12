# DataFerry — Seamless Data Synchronization for .NET Applications

**Simplify data access and boost resiliency with DataFerry, a generic caching solution designed to tightly couple your cache and database.**

## Features

* **Generic Caching:** Works with any data type and data provider through the `IDataSource<T>` interface.
* **Redis Integration:** Leverages the high-performance and robustness of the [StackExchange.Redis](https://stackexchange.github.io/StackExchange.Redis/) library.
* **Built-in Resilience:** Implements [Polly](https://www.pollydocs.org) policies for automatic retry, circuit breakers, and other resiliency patterns, enhancing the reliability of your transactions.
* **Easy Configuration:** Inject the `IConnectionMultiplexer` and your `IDataSource<T>` implementation, and you're ready to go.

## Overview

DataFerry's `DataFerry` class acts as a bridge between your application and your data sources. When you request data:

1. **Cache Check:** It first checks your Redis cache for the data.
2. **Database Fetch (if needed):** If the data is not found in the cache, it fetches it from your database using your `IDataSource<T>` implementation.
3. **Cache Update:** The fetched data is then stored in the cache for future requests.

This approach ensures that:

* **Your cache and database remain synchronized, eliminating inconsistencies.**
* **You avoid redundant database calls, improving performance.**
* **Your application handles transient errors gracefully, increasing reliability.**

`DataFerry` offers a complete set of tools for managing your cached data. It supports all fundamental CRUD operations, as well as optimized batch versions for handling multiple records efficiently. Create and update functionality is combined into a single upsert via the `SetData` and `SetDataBatch` methods.

## Getting Started

1. **Install the NuGet package:**

   ```bash
   Install-Package lvlup.DataFerry
   ```

2. **Implement `IDataSource<T>`**

   ```csharp
   public interface IMyDataProvider : IDataSource<MyDataModel>
   ```

3. **Inject your Dependencies**

   ```csharp
   // Configure your cache settings
   services.Configure<CacheSettings>(builder.Configuration.GetSection("CacheSettings"));

   // Register IConnectionMultiplexer 
   services.AddSingleton<IConnectionMultiplexer>(ConnectionMultiplexer.Connect(connectionString));

   // Register your IDataSource
   builder.Services.AddTransient<IMyDataProvider, MyDataProvider>();

   // Register DataFerry
   builder.Services.AddTransient<IDataFerry<MyDataModel>>(serviceProvider =>
      new DataFerry<MyDataModel>(
         serviceProvider.GetRequiredService<IConnectionMultiplexer>(),
         serviceProvider.GetRequiredService<IMyDataProvider>(),
         serviceProvider.GetRequiredService<IOptions<CacheSettings>>(),
         serviceProvider.GetRequiredService<ILogger<DataFerry<MyDataModel>>>()
      ));
   ```

4. **Use DataFerry in your Service**

   ```csharp
   MyDataModel? myData = await _DataFerry.GetDataAsync(cacheKey);
   ```

### Designing your Cache Key

When designing your cache key, it's important to consider how they'll be used both in the cache and when interacting with your database. Since your cache key serves as the database query key as well, you have two main options for handling the information encoded within it:

1. **Extract the information directly from the key.** This works well if your key has a clear structure that your `IDataSource<T>` implementation can easily parse.
2. **Deserialize the key if it's a hash.** If you use hashing to generate your cache keys, you'll need to deserialize them within your `IDataSource<T>` implementation to extract the necessary lookup information.

A common and effective pattern is to include versioning information and the actual lookup key as a prefix to the hash. This approach gives you built-in version control for your cached data and a straightforward way to access the primary lookup value for database queries. EX:

```csharp
private string ConstructCacheKey(MyDataRequest request, string version)
{
	string prefix = $"{version}:{request.Key}";
	// Hashes request object and attaches prefix
	return CacheKeyGenerator.GenerateCacheKey(request, prefix);
}
```

### Configuring your Resiliency Pattern

DataFerry leverages Polly policies to enhance the reliability of your data access. You can tailor the resilience behavior using the `DesiredPolicy` property in `CacheSettings`. Currently supported options include:

1. **Default:** A basic timeout and fallback policy.
2. **Basic:** Adds automatic retries with configurable intervals and retry counts.
3. **Advanced:** Includes bulkhead isolation and circuit breaker patterns for advanced resiliency.

Simply set the `DesiredPolicy` property in your configuration to the desired `ResiliencyPatterns` value to apply the corresponding pattern.

## Additional Features
* **Sorting Algorithm Extensions:** Benefit from QuickSort, BubbleSort, MergeSort, and BucketSort implemented as extensions of `IList`.
* **ArrayPool<T> Implementation:** Utilize our custom ArrayPool<T> implementation with a tiered caching scheme for superior memory management and reduced allocation overhead in your applications.
* **MurmurHash3 Implementation:** Leverage the included high-performance, non-cryptographic hash function for a variety of needs within your projects.
* **In-Memory Cache with TTL:** Enjoy efficient data storage and retrieval with our sophisticated concurrent in-memory caching solution featuring Time-To-Live (TTL) support for automatic resource eviction.

# Contributing
Contributions are welcome.

## License

DataFerry is licensed under the [MIT License](https://opensource.org/licenses/MIT).