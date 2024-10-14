# DistributedCache Get Data Flow
- When the `DistributedCache` needs to retrieve a value from Redis, it uses the `RentedBufferWriter` to obtain a buffer.
- The `RentedBufferWriter` internally uses the `StackArrayPool<byte>` to rent a buffer, avoiding a new allocation if possible.
- The `DistributedCache` retrieves the raw bytes from Redis and copies them into the rented buffer provided by the `RentedBufferWriter`.
- The `DistributedCache` then passes the `RentedBufferWriter` (which encapsulates the rented buffer) to the `DistributedCacheSerializer`.
- The `DistributedCacheSerializer` uses the buffer within the `RentedBufferWriter` to perform the deserialization, potentially renting another buffer from the `StackArrayPool<byte>` if needed for temporary storage during deserialization.
- The deserialized object is returned.
- The `RentedBufferWriter` is disposed of, which returns the rented buffer to the `StackArrayPool<byte>`.

#
```
// DistributedCache.cs

public class DistributedCache : IDistributedCache
{
    // ... other members (constructor, _database, _serializer, etc.) ...

    private readonly RentedBufferWriter<byte> _bufferWriter;

    // ... constructor injecting RentedBufferWriter<byte> and IDistributedCacheSerializer ...

    public async ValueTask<bool> TryGetAsync(string key, IBufferWriter<byte> destination, CancellationToken token = default)
    {
        var value = await _database.StringGetAsync(key, token);
        if (value.HasValue)
        {
            var span = _bufferWriter.GetSpan(value.Length);
            value.CopyTo(span);
            _bufferWriter.Advance(value.Length);
            destination.Write(_bufferWriter.GetMemory(0));
            return true;
        }
        return false;
    }

    // ... other methods (SetAsync, RemoveAsync, etc.) ...
}
```
#
```
// DistributedCacheSerializer.cs

public class DistributedCacheSerializer<T> : IDistributedCacheSerializer<T>
{
    private readonly StackArrayPool<byte> _byteArrayPool;

    public DistributedCacheSerializer(StackArrayPool<byte> byteArrayPool)
    {
        _byteArrayPool = byteArrayPool;
    }

    public async ValueTask<T> DeserializeAsync(ReadOnlySequence<byte> source, CancellationToken token = default)
    {
        var buffer = _byteArrayPool.Rent((int)source.Length);
        try
        {
            source.CopyTo(buffer);
            await using var stream = new MemoryStream(buffer, 0, (int)source.Length);
            var result = await JsonSerializer.DeserializeAsync<T>(stream, cancellationToken: token);
            return result!;
        }
        finally
        {
            _byteArrayPool.Return(buffer);
        }
    }

    public void Serialize(T value, IBufferWriter<byte> target)
    {
        using var jsonWriter = new Utf8JsonWriter(target);
        JsonSerializer.Serialize(jsonWriter, value);
    }
}
```
#
```
public interface IDistributedCacheSerializer<T>
{
    T Deserialize(ReadOnlySequence<byte> source);

    void Serialize(T value, IBufferWriter<byte> target);

    ValueTask<T> DeserializeAsync(ReadOnlySequence<byte> source, CancellationToken token = default);

    ValueTask SerializeAsync(T value, IBufferWriter<byte> target, CancellationToken token = default);
}
```
#
```
public interface ISparseDistributedCache
{
    ValueTask<IDictionary<string, T>> GetBatchFromCacheAsync<T>(IEnumerable<string> keys, CancellationToken token = default);

    ValueTask<IDictionary<string, bool>> SetBatchInCacheAsync<T>(IDictionary<string, T> data, DistributedCacheEntryOptions options, CancellationToken token = default);

    ValueTask<IDictionary<string, bool>> RemoveBatchFromCacheAsync(IEnumerable<string> keys, CancellationToken token = default);
}
```
#
```
public static class DataFerryServiceCollectionExtensions
{
    public static IServiceCollection AddDataFerry(this IServiceCollection services, Action<DataFerryCacheOptions> setupAction)
    {
        if (services == null)
        {
            throw new ArgumentNullException(nameof(services));
        }

        if (setupAction == null)
        {
            throw new ArgumentNullException(nameof(setupAction));
        }

        services.AddOptions();
        services.Configure(setupAction);

        services.AddSingleton<ArrayPool<byte>>(serviceProvider => StackArrayPool<byte>.Shared);

        services.AddScoped<IBufferWriter<byte>, RentedBufferWriter<byte>>();
        services.AddSingleton<IDistributedCacheSerializer, DistributedCacheSerializer>();
        services.AddSingleton<ISparseDistributedCache, DistributedCache>();
        services.AddSingleton<IDataFerry, DataFerry>(); 

        return services;
    }
}
```
#
```
// In your Startup.cs or Program.cs
services.AddSingleton<IConnectionMultiplexer>(GetRedisConnection());

services.AddDataFerry(options =>
{
    // ... RedisCacheOptions configurations (if any) ...
});
```