# CacheProvider — Seamless Caching for .NET Applications

**Simplify data access and boost performance with CacheProvider, a generic caching solution designed for tight coupling between your cache and database.**

## Features

* **Generic Caching:** Works with any data type and data provider through the `IRealProvider<T>` interface.
* **Redis Integration:** Leverages the high-performance and robustness of the StackExchange.Redis library.
* **Built-in Resilience:** Implements Polly policies for automatic retry and circuit breaker patterns, enhancing application stability.
* **Easy Configuration:** Inject the `IConnectionMultiplexer` and your `IRealProvider<T>` implementation, and you're ready to go.

## How it Works

CacheProvider acts as a bridge between your application and your data sources. When you request data:

1. **Cache Check:** It first checks the Redis cache for the data.
2. **Database Fetch (if needed):** If the data is not found in the cache, it fetches it from your database using your `IRealProvider<T>` implementation.
3. **Cache Update:** The fetched data is then stored in the cache for future requests.

This approach ensures that:

* **Your cache and database remain synchronized.**
* **You avoid redundant database calls, improving performance.**
* **Your application handles transient errors gracefully.**

## Getting Started

1. **Install the NuGet package:**

   ```bash
   Install-Package CacheProvider
   ```

2. **Implement `IRealProvider<T>`**

   ```csharp
   public interface IMyDataProvider : IRealProvider<MyDataModel>
   ```

3. **Inject your Dependencies**

   ```csharp
   // Configure your cache settings
   services.Configure<CacheSettings>(builder.Configuration.GetSection("CacheSettings"));

   // Register IConnectionMultiplexer 
   services.AddSingleton<IConnectionMultiplexer>(ConnectionMultiplexer.Connect(connectionString));

   // Register your IRealProvider
   builder.Services.AddTransient<IMyDataProvider, MyDataProvider>();

   // Register CacheProvider
   builder.Services.AddTransient<ICacheProvider<MyDataModel>>(serviceProvider =>
      new CacheProvider<MyDataModel>(
         serviceProvider.GetRequiredService<IConnectionMultiplexer>(),
         serviceProvider.GetRequiredService<IMyDataProvider>(),
         serviceProvider.GetRequiredService<IOptions<CacheSettings>>(),
         serviceProvider.GetRequiredService<ILogger<CacheProvider<MyDataModel>>>()
      ));
   ```

4. **Use CacheProvider in your Service**

   ```csharp
   MyDataModel? myData = await _cacheProvider.GetFromCacheAsync(cacheKey);
   ```

### Designing your Cache Key

When designing your cache key, it's important to consider how they'll be used both in the cache and when interacting with your database. Since your cache key serves as the database query key as well, you have two main options for handling the information encoded within it:

1. **Extract the information directly from the key.** This works well if your key has a clear structure that your `IRealProvider<T>` implementation can easily parse.
2. **Deserialize the key if it's a hash.** If you use hashing to generate your cache keys, you'll need to deserialize them within your `IRealProvider<T>` implementation to extract the necessary lookup information.

A common and effective pattern is to include versioning information and the actual lookup key as a prefix to the hash. This approach gives you built-in version control for your cached data and a straightforward way to access the primary lookup value for database queries. EX:

```csharp
private string ConstructCacheKey(MyDataRequest request, string version)
{
	string prefix = $"{version}:{request.Key}";
	// Hashes request object and attaches prefix
	return CacheKeyGenerator.GenerateCacheKey(request, prefix);
}
```

## License

Cache-Provider is licensed under the [MIT License](https://opensource.org/licenses/MIT).
