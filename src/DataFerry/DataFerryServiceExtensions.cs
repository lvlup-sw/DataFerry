using lvlup.DataFerry.Orchestrators;
using lvlup.DataFerry.Orchestrators.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IO;

namespace lvlup.DataFerry
{
    /// <summary>
    /// Configuration extension methods for <see cref="DataFerry"/>.
    /// </summary>
    public static class DataFerryServiceExtensions
    {
        public static IServiceCollection AddDataFerry(this IServiceCollection services, Action<CacheSettings> setupAction)
        {
            // Null checks
            ArgumentNullException.ThrowIfNull(services, nameof(services));
            ArgumentNullException.ThrowIfNull(setupAction, nameof(setupAction));

            // Configure options
            services.AddOptions();
            services.Configure(setupAction);

            // Add pooled resource managers
            services.TryAddSingleton(serviceProvider => StackArrayPool<byte>.Shared);
            services.TryAddSingleton<RecyclableMemoryStreamManager>();

            // Add and return services
            services.TryAddSingleton<IDataFerrySerializer, DataFerrySerializer>();
            services.TryAddSingleton<ITaskOrchestrator, TaskOrchestrator>();
            services.TryAddSingleton<IMemCache<string, byte[]>, TtlMemCache<string, byte[]>>();
            services.TryAddSingleton<ICacheOrchestrator, CacheOrchestrator>();
            services.TryAddSingleton<DataFerry>();
            return services;
        }
    }
}
