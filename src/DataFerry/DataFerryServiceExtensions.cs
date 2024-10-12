using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Buffers;

namespace lvlup.DataFerry
{
    /// <summary>
    /// Configuration extension methods for <see cref="Providers.DataFerry"/>.
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

            // Add array pool
            services.TryAddSingleton(serviceProvider => StackArrayPool<byte>.Shared);

            // Add and return services
            services.TryAddScoped<IBufferWriter<byte>, RentedBufferWriter<byte>>();
            services.TryAddSingleton<IDataFerrySerializer, DataFerrySerializer>();
            services.TryAddSingleton<ISparseDistributedCache, SparseDistributedCache>();
            services.TryAddSingleton<IDataFerry, Providers.DataFerry>();
            return services;
        }
    }
}
