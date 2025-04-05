using lvlup.DataFerry.Caching.Abstractions;
using lvlup.DataFerry.Caching.Options;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace lvlup.DataFerry.Caching.Extensions;

/// <summary>
/// Extension methods for caching.
/// </summary>
public static class ReplacementPolicyCacheExtensions
{
    // --- LRU Extensions --- Updated to use consolidated options ---
    public static IServiceCollection AddReplacementPolicyCacheLru<TKey, TValue>(this IServiceCollection services)
        where TKey : notnull { /* Implementation */ return services; }

    public static IServiceCollection AddReplacementPolicyCacheLru<TKey, TValue>(this IServiceCollection services, string name)
        where TKey : notnull { /* Implementation */ return services; }

    public static IServiceCollection AddReplacementPolicyCacheLru<TKey, TValue>(this IServiceCollection services, Action<ReplacementPolicyCacheOptions<TKey>> configure) // Updated options type
        where TKey : notnull { /* Implementation */ return services; }

    public static IServiceCollection AddReplacementPolicyCacheLru<TKey, TValue>(this IServiceCollection services, IConfigurationSection section)
        where TKey : notnull { /* Implementation */ return services; }

    public static IServiceCollection AddReplacementPolicyCacheLru<TKey, TValue>(this IServiceCollection services, string name, IConfigurationSection section)
        where TKey : notnull { /* Implementation */ return services; }

    public static IServiceCollection AddReplacementPolicyCacheLru<TKey, TValue>(this IServiceCollection services, string name, Action<ReplacementPolicyCacheOptions<TKey>> configure) // Updated options type
        where TKey : notnull { /* Implementation */ return services; }


    // --- LFU Extensions --- Updated to use consolidated options AND corrected naming typo ---
    public static IServiceCollection AddReplacementPolicyCacheLfu<TKey, TValue>(this IServiceCollection services) // Corrected naming
        where TKey : notnull { /* Implementation */ return services; }

    public static IServiceCollection AddReplacementPolicyCacheLfu<TKey, TValue>(this IServiceCollection services, string name) // Corrected naming
        where TKey : notnull { /* Implementation */ return services; }

    public static IServiceCollection AddReplacementPolicyCacheLfu<TKey, TValue>(this IServiceCollection services, Action<ReplacementPolicyCacheOptions<TKey>> configure) // Corrected naming and options type
        where TKey : notnull { /* Implementation */ return services; }

    public static IServiceCollection AddReplacementPolicyCacheLfu<TKey, TValue>(this IServiceCollection services, IConfigurationSection section) // Corrected naming
        where TKey : notnull { /* Implementation */ return services; }

    public static IServiceCollection AddReplacementPolicyCacheLfu<TKey, TValue>(this IServiceCollection services, string name, IConfigurationSection section) // Corrected naming
        where TKey : notnull { /* Implementation */ return services; }

    public static IServiceCollection AddReplacementPolicyCacheLfu<TKey, TValue>(this IServiceCollection services, string name, Action<ReplacementPolicyCacheOptions<TKey>> configure) // Corrected naming and options type
        where TKey : notnull { /* Implementation */ return services; }
}