using lvlup.DataFerry.Orchestrators;
using lvlup.DataFerry.Orchestrators.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IO;

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
        services.TryAddSingleton(StackArrayPool<byte>.Shared);
        services.TryAddSingleton<RecyclableMemoryStreamManager>();

        // Add and return services
        services.TryAddSingleton<IDataFerrySerializer, DataFerrySerializer>();
        services.TryAddSingleton<ITaskOrchestratorFactory, TaskOrchestratorFactory>();
        services.TryAddSingleton<IMemCache<string, byte[]>, LfuMemCache<string, byte[]>>();
        services.TryAddSingleton<ICacheOrchestrator, CacheOrchestrator>();
        services.TryAddSingleton<IDataFerry, DataFerry>();

        return services;
    }
}