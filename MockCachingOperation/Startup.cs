using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using CacheProvider.Providers;
using MockCachingOperation.Process;
using MockCachingOperation.Configuration;
using StackExchange.Redis;
using CacheProvider.Providers.Interfaces;
using Microsoft.Extensions.Logging;

namespace MockCachingOperation
{
    public class Startup(IConfiguration configuration)
    {
        public IConfiguration Configuration { get; } = configuration;

        public void ConfigureServices(IServiceCollection services)
        {
            // Add configuration
            services.AddOptions();
            services.Configure<AppSettings>(Configuration.GetSection("AppSettings"));
            services.Configure<CacheSettings>(Configuration.GetSection("CacheSettings"));

            // Inject services
            services.AddSingleton<IRealProvider<Payload>, RealProvider>();
            services.AddSingleton<IConnectionMultiplexer>(serviceProvider =>
            {
                return ConnectionMultiplexer.Connect(
                    Configuration.GetConnectionString("Redis") ?? string.Empty
                );
            });
            services.AddSingleton<ICacheProvider<Payload>>(serviceProvider =>
            {
                return new CacheProvider<Payload>(
                    serviceProvider.GetRequiredService<IConnectionMultiplexer>(),
                    serviceProvider.GetRequiredService<IRealProvider<Payload>>(),
                    serviceProvider.GetRequiredService<CacheSettings>(),
                    serviceProvider.GetRequiredService<ILogger<CacheProvider<Payload>>>()
                );
            });

            // Inject application and worker to execute
            services.AddScoped(provider => new MockCachingOperation(provider));
            services.AddHostedService<Worker>();
        }
    }
}