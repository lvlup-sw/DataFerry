// ==============================================================================
// <copyright file="DataFerryServiceExtensions.cs" company="Level Up Software">
// Copyright (c) Level Up Software. All rights reserved.
// </copyright>
// ==============================================================================

using lvlup.DataFerry.Memory;
using lvlup.DataFerry.Properties;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace lvlup.DataFerry;

/// <summary>
/// Configuration extension methods for <see cref="DataFerry"/>.
/// </summary>
public static class DataFerryServiceExtensions
{
    public static IServiceCollection AddDataFerry(this IServiceCollection services, Action<CacheSettings>? setupAction = null)
    {
        // Null check
        ArgumentNullException.ThrowIfNull(services, nameof(services));

        // Configure options
        services.AddOptions();
        if (setupAction is not null)
            services.Configure(setupAction);

        // Add pooled resource managers
        services.TryAddSingleton(ThreadLocalArrayPool<byte>.Shared);

        // Add and return services


        return services;
    }
}