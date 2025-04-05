
using System;
using System.Diagnostics.Metrics;
using System.Runtime.CompilerServices;
using Dataferry.Caching.Memory;

using lvlup.DataFerry.Caching.Abstractions;
using lvlup.DataFerry.Caching.Adapters;
using lvlup.DataFerry.Caching.Lfu;
using lvlup.DataFerry.Caching.Lru;
using lvlup.DataFerry.Caching.Options; // Namespace for your cache classes
// Add using directives for LFU/LRU concrete classes when implemented, e.g.:
// using Dataferry.Caching.Memory.Lfu;
// using Dataferry.Caching.Memory.Lru;
using Microsoft.Extensions.Caching.Distributed; // For IBufferDistributedCache
using Microsoft.Extensions.Caching.StackExchangeRedis; // For RedisCache concrete type
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging; // Added for Decorator registration
using Microsoft.Extensions.Options;
using Polly; // Assuming Polly policies are used by decorator
using Polly.Wrap;

namespace Dataferry.Caching.Memory.Extensions; // Updated namespace to match example

/// <summary>
/// Provides extension methods for registering replacement policy cache services and related components.
/// </summary>
public static class ReplacementPolicyCacheExtensions
{
    // --- In-Memory Cache Registration ---

    /// <summary>
    /// Adds LFU in-memory cache services (<see cref="ReplacementPolicyCache{TKey,TValue}"/>) to the specified <see cref="IServiceCollection"/>.
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
        services.AddCoreCacheServices<TKey>(); // Renamed helper
        services.ConfigureOptionsInternal(setupAction);

        // --- Replace LfuCache<> with your actual concrete implementation class name ---
        services.TryAddSingleton<LfuCache<TKey, TValue>>();
        // Register the concrete type also as the abstract base type for consumers who need the base
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
        services.AddCoreCacheServices<TKey>(); // Renamed helper
        services.ConfigureOptionsInternal(setupAction);

        // --- Replace LruCache<> with your actual concrete implementation class name ---
        services.TryAddSingleton<LruCache<TKey, TValue>>(); // Using LruCache placeholder
        services.TryAddSingleton<ReplacementPolicyCache<TKey, TValue>>(sp => sp.GetRequiredService<LruCache<TKey, TValue>>());
        return services;
    }

    // --- Removed Obsolete Methods ---
    // AddDistributedLfuCache, AddDistributedLruCache, AddLfuCacheAdapter, AddLruCacheAdapter
    // are removed as the custom distributed cache implementation and adapter are no longer used.

    // --- Decorator Registration (For Standard RedisCache Enhancement) ---

    /// <summary>
    /// Adds the <see cref="ResilientRedisCacheDecorator"/> to wrap the standard <see cref="IBufferDistributedCache"/> registration (typically <see cref="RedisCache"/>).
    /// This adds Polly resilience and custom instrumentation to the standard distributed cache.
    /// </summary>
    /// <remarks>
    /// IMPORTANT: You MUST register a standard <see cref="IBufferDistributedCache"/> implementation BEFORE calling this method,
    /// typically by calling <c>services.AddStackExchangeRedisCache(...)</c>.
    /// This method also requires Polly policies (e.g., <see cref="AsyncPolicyWrap"/>, <see cref="PolicyWrap"/>) to be registered in the service collection.
    /// </remarks>
    /// <param name="services">The <see cref="IServiceCollection"/> to add services to.</param>
    /// <returns>The <see cref="IServiceCollection"/> so that additional calls can be chained.</returns>
    public static IServiceCollection AddResilientRedisCacheDecorator(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Attempt to find the existing IBufferDistributedCache registration (likely RedisCache)
        var existingDescriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IBufferDistributedCache));
        if (existingDescriptor == null)
        {
            throw new InvalidOperationException(
                $"No {nameof(IBufferDistributedCache)} service is registered. " +
                $"Please call services.AddStackExchangeRedisCache(...) or another provider before calling {nameof(AddResilientRedisCacheDecorator)}.");
        }

        // Ensure RedisCache concrete type is resolvable if needed by decorator constructor
        // This assumes AddStackExchangeRedisCache registers the concrete type too. Check its implementation if issues arise.
        services.TryAddSingleton(sp => (RedisCache)sp.GetRequiredService(existingDescriptor.ImplementationType ?? existingDescriptor.ImplementationInstance?.GetType() ?? typeof(RedisCache)));

        // Register the decorator itself
        services.TryAddSingleton<ResilientRedisCacheDecorator>();

        // Replace the IBufferDistributedCache registration to point to the decorator factory
        // The factory ensures the decorator gets the *original* RedisCache instance
        // Note: This relies on the previous registration of RedisCache itself.
        services.AddSingleton<IBufferDistributedCache>(sp => sp.GetRequiredService<ResilientRedisCacheDecorator>());

        // Also replace the IDistributedCache registration if present (IBufferDistributedCache inherits it)
        // Check if it points to the same implementation as IBufferDistributedCache before replacing
        var distributedDesc = services.FirstOrDefault(d => d.ServiceType == typeof(IDistributedCache));
        if (distributedDesc != null && (distributedDesc.ImplementationType == existingDescriptor.ImplementationType || distributedDesc.ImplementationInstance == existingDescriptor.ImplementationInstance || distributedDesc.ImplementationFactory == existingDescriptor.ImplementationFactory))
        {
             services.AddSingleton<IDistributedCache>(sp => sp.GetRequiredService<ResilientRedisCacheDecorator>());
        }

        return services;
    }

     // Optional: Overload to configure Polly policies specifically for the decorator
     /*
     public static IServiceCollection AddResilientRedisCacheDecorator(
        this IServiceCollection services,
        Action<PollyOptions> configurePolly)
     {
         // ... register standard cache ...
         // ... register Polly policies based on configurePolly action ...
         // ... register decorator ...
         // ... replace interface registrations ...
         return services;
     }
     */


    // --- Private Helpers ---

    /// <summary> Registers essential services like Options and Validation for custom caches. </summary>
    private static void AddCoreCacheServices<TKey>(this IServiceCollection services) where TKey: notnull
    {
        services.AddOptions();
        // Validator for custom cache options
        services.TryAddSingleton<IValidateOptions<ReplacementPolicyCacheOptions<TKey>>, ReplacementPolicyCacheOptionsValidator<TKey>>();
        // Add IMeterFactory dependency if not already present by default in host
        services.TryAddSingleton<IMeterFactory, DefaultMeterFactory>();
    }

    /// <summary> Configures custom cache options using the provided action. </summary>
    private static void ConfigureOptionsInternal<TKey>(
       this IServiceCollection services,
       Action<ReplacementPolicyCacheOptions<TKey>>? setupAction) where TKey : notnull
    {
        if (setupAction != null)
        {
            // Ensure options are configured with a unique name if needed later, otherwise default name is used
            services.Configure(Options.DefaultName, setupAction);
        }
    }

     // TODO: Add overloads accepting IConfiguration section for options binding for AddLfuCache/AddLruCache.
     // public static IServiceCollection AddLfuCache<TKey, TValue>(this IServiceCollection services, IConfiguration configurationSection) { ... }
}

// Dummy IMeterFactory implementation (Unchanged)
internal sealed class DefaultMeterFactory : IMeterFactory { private readonly ConditionalWeakTable<MeterOptions, Meter> _meters = new(); public Meter Create(MeterOptions options) => _meters.GetValue(options, o => new Meter(o)); public void Dispose() { } }