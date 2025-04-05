using System.Diagnostics.Metrics;
using System.Runtime.CompilerServices;

using lvlup.DataFerry.Caching.Abstractions;
using lvlup.DataFerry.Caching.Adapters;
using lvlup.DataFerry.Caching.Clients.Contracts;
using lvlup.DataFerry.Caching.Lfu;
using lvlup.DataFerry.Caching.Lru;
using lvlup.DataFerry.Caching.Options;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace lvlup.DataFerry.Caching.Extensions;

/// <summary>
/// Provides extension methods for registering replacement policy cache services.
/// </summary>
public static class ReplacementPolicyCacheExtensions
{
    // --- In-Memory Cache Registration ---

    /// <summary>
    /// Adds LFU in-memory cache services (<see cref="ReplacementPolicyCache{TKey, TValue}"/>) to the specified <see cref="IServiceCollection"/>.
    /// </summary>
    /// <typeparam name="TKey">The type of the key.</typeparam>
    /// <typeparam name="TValue">The type of the value.</typeparam>
    /// <param name="services">The <see cref="IServiceCollection"/> to add services to.</param>
    /// <param name="setupAction">An optional <see cref="Action{T}"/> to configure the <see cref="ReplacementPolicyCacheOptions{TKey}"/>.</param>
    /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
    public static IServiceCollection AddLfuCache<TKey, TValue>(
        this IServiceCollection services,
        Action<ReplacementPolicyCacheOptions<TKey>>? setupAction = null)
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddCoreServices<TKey>();
        services.ConfigureOptionsInternal(setupAction);
        // Replace LfuCache<> with your actual concrete implementation class name
        services.TryAddSingleton<LfuCache<TKey, TValue>>();
        // Register the concrete type also as the abstract base type
        services.TryAddSingleton<ReplacementPolicyCache<TKey, TValue>>(sp => sp.GetRequiredService<LfuCache<TKey, TValue>>());
        return services;
    }

    /// <summary>
    /// Adds LRU in-memory cache services (<see cref="ReplacementPolicyCache{TKey, TValue}"/>) to the specified <see cref="IServiceCollection"/>.
    /// </summary>
    /// <typeparam name="TKey">The type of the key.</typeparam>
    /// <typeparam name="TValue">The type of the value.</typeparam>
    /// <param name="services">The <see cref="IServiceCollection"/> to add services to.</param>
    /// <param name="setupAction">An optional <see cref="Action{T}"/> to configure the <see cref="ReplacementPolicyCacheOptions{TKey}"/>.</param>
    /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
     public static IServiceCollection AddLruCache<TKey, TValue>(
        this IServiceCollection services,
        Action<ReplacementPolicyCacheOptions<TKey>>? setupAction = null)
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddCoreServices<TKey>();
        services.ConfigureOptionsInternal(setupAction);
        // Replace LruCache<> with your actual concrete implementation class name
        services.TryAddSingleton<LruCache<TKey, TValue>>(); // Using LruCache placeholder
        services.TryAddSingleton<ReplacementPolicyCache<TKey, TValue>>(sp => sp.GetRequiredService<LruCache<TKey, TValue>>());
        return services;
    }

    // --- Distributed Cache Registration (Core Implementation) ---

    /// <summary>
    /// Adds Distributed LFU cache services (<see cref="DistributedReplacementPolicyCache{TKey, TValue}"/>) to the specified <see cref="IServiceCollection"/>.
    /// Requires <see cref="IRedisClient"/> to be registered separately.
    /// </summary>
    /// <typeparam name="TKey">The type of the key.</typeparam>
    /// <typeparam name="TValue">The type of the value.</typeparam>
    /// <param name="services">The <see cref="IServiceCollection"/> to add services to.</param>
    /// <param name="setupAction">An optional <see cref="Action{T}"/> to configure the <see cref="ReplacementPolicyCacheOptions{TKey}"/>.</param>
    /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
     public static IServiceCollection AddDistributedLfuCache<TKey, TValue>(
        this IServiceCollection services,
        Action<ReplacementPolicyCacheOptions<TKey>>? setupAction = null)
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddCoreServices<TKey>(); // Adds options, validator, meter factory etc.
        services.ConfigureOptionsInternal(setupAction);
        // Replace DistributedLfuCache<> with your actual concrete implementation class name
        services.TryAddSingleton<DistributedLfuCache<TKey, TValue>>();
        // Register the concrete type also as the abstract base type
        services.TryAddSingleton<DistributedReplacementPolicyCache<TKey, TValue>>(sp => sp.GetRequiredService<DistributedLfuCache<TKey, TValue>>());
        // Optionally register as the sync base type too? Could be ambiguous.
        // services.TryAddSingleton<ReplacementPolicyCache<TKey, TValue>>(sp => sp.GetRequiredService<DistributedLfuCache<TKey, TValue>>());
        return services;
    }

    /// <summary>
    /// Adds Distributed LRU cache services (<see cref="DistributedReplacementPolicyCache{TKey, TValue}"/>) to the specified <see cref="IServiceCollection"/>.
    /// Requires <see cref="IRedisClient"/> to be registered separately.
    /// </summary>
    /// <typeparam name="TKey">The type of the key.</typeparam>
    /// <typeparam name="TValue">The type of the value.</typeparam>
    /// <param name="services">The <see cref="IServiceCollection"/> to add services to.</param>
    /// <param name="setupAction">An optional <see cref="Action{T}"/> to configure the <see cref="ReplacementPolicyCacheOptions{TKey}"/>.</param>
    /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
     public static IServiceCollection AddDistributedLruCache<TKey, TValue>(
        this IServiceCollection services,
        Action<ReplacementPolicyCacheOptions<TKey>>? setupAction = null)
        where TKey : notnull
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddCoreServices<TKey>();
        services.ConfigureOptionsInternal(setupAction);
        // Replace DistributedLruCache<> with your actual concrete implementation class name
        services.TryAddSingleton<DistributedLruCache<TKey, TValue>>(); // Using LruCache placeholder
        services.TryAddSingleton<DistributedReplacementPolicyCache<TKey, TValue>>(sp => sp.GetRequiredService<DistributedLruCache<TKey, TValue>>());
        return services;
    }

    // --- Adapter Registration (For IBufferDistributedCache / HybridCache) ---

    /// <summary>
    /// Registers the Distributed LFU Cache (<see cref="DistributedLfuCache{TKey, TValue}"/> with object/object generics)
    /// and the <see cref="BufferDistributedCacheAdapter"/> as <see cref="IBufferDistributedCache"/>.
    /// Requires <see cref="IRedisClient"/> to be registered separately. Intended for use with HybridCache.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add services to.</param>
    /// <param name="setupAction">An optional <see cref="Action{T}"/> to configure the <see cref="ReplacementPolicyCacheOptions{TKey}"/> for the underlying cache.</param>
    /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
     public static IServiceCollection AddLfuCacheAdapter(
        this IServiceCollection services,
        Action<ReplacementPolicyCacheOptions<object>>? setupAction = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        // Register the underlying distributed cache implementation (using object/object)
        services.AddDistributedLfuCache<object, object>(setupAction);

        // Register the adapter, making it the IBufferDistributedCache service
        // It depends on the DistributedReplacementPolicyCache<object, object> registered above.
        services.TryAddSingleton<IBufferDistributedCache>(sp =>
           new BufferDistributedCacheAdapter(
               sp.GetRequiredService<DistributedReplacementPolicyCache<object, object>>()
               // Inject serializer here if adapter needed it
               ));

        return services;
    }

    /// <summary>
    /// Registers the Distributed LRU Cache (<see cref="DistributedLruCache{TKey, TValue}"/> with object/object generics)
    /// and the <see cref="BufferDistributedCacheAdapter"/> as <see cref="IBufferDistributedCache"/>.
    /// Requires <see cref="IRedisClient"/> to be registered separately. Intended for use with HybridCache.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add services to.</param>
    /// <param name="setupAction">An optional <see cref="Action{T}"/> to configure the <see cref="ReplacementPolicyCacheOptions{TKey}"/> for the underlying cache.</param>
    /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
     public static IServiceCollection AddLruCacheAdapter(
        this IServiceCollection services,
        Action<ReplacementPolicyCacheOptions<object>>? setupAction = null)
     {
         ArgumentNullException.ThrowIfNull(services);
         // Register the underlying distributed cache implementation (using object/object)
         services.AddDistributedLruCache<object, object>(setupAction); // Assumes Lru version exists

         // Register the adapter
         services.TryAddSingleton<IBufferDistributedCache>(sp =>
            new BufferDistributedCacheAdapter(
                sp.GetRequiredService<DistributedReplacementPolicyCache<object, object>>()
                ));

         return services;
     }


    // --- Private Helpers ---

    /// <summary> Registers essential services like Options and Validation. </summary>
    private static void AddCoreServices<TKey>(this IServiceCollection services) where TKey: notnull
    {
        services.AddOptions();
        // Ensure validator is registered only once per TKey type
        services.TryAddSingleton<IValidateOptions<ReplacementPolicyCacheOptions<TKey>>, ReplacementPolicyCacheOptionsValidator<TKey>>();
        // Add IMeterFactory dependency if not already present by default in host
        services.TryAddSingleton<IMeterFactory, DefaultMeterFactory>();
    }

    /// <summary> Configures options using the provided action. </summary>
    private static void ConfigureOptionsInternal<TKey>(
       this IServiceCollection services,
       Action<ReplacementPolicyCacheOptions<TKey>>? setupAction) where TKey : notnull
    {
        if (setupAction != null)
        {
            services.Configure(setupAction);
        }
    }

     // TODO: Add overloads accepting IConfiguration section for options binding if desired.
     // public static IServiceCollection AddLfuCache<TKey, TValue>(this IServiceCollection services, IConfiguration configurationSection) { ... }
}

// Dummy IMeterFactory implementation if one isn't provided by the host environment
// (In ASP.NET Core, IMeterFactory is typically registered)
internal sealed class DefaultMeterFactory : IMeterFactory
{
    private readonly ConditionalWeakTable<MeterOptions, Meter> _meters = new();
    public Meter Create(MeterOptions options) => _meters.GetValue(options, o => new Meter(o));
    public void Dispose() { /* No op for default? */ }
}