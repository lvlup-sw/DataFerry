using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using CacheProvider.Caches;
using CacheProvider.Providers;
using MockCachingOperation.Process;
using MockCachingOperation.Configuration;
using Microsoft.Extensions.Options;
using StackExchange.Redis;
using Microsoft.Extensions.Logging.Abstractions;

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

            // Inject redis connection
            string redisConnection = Configuration.GetConnectionString("Redis") ?? "";
            if (!string.IsNullOrWhiteSpace(redisConnection))
            {
                services.AddSingleton<IConnectionMultiplexer>(serviceProvider =>
                {
                    return ConnectionMultiplexer.Connect(redisConnection);
                });
                services.AddSingleton<IDistributedCache, DistributedCache>(serviceProvider =>
                {
                    var connectionMultiplexer = serviceProvider.GetRequiredService<IConnectionMultiplexer>();
                    var cacheSettings = serviceProvider.GetRequiredService<CacheSettings>();
                    return new DistributedCache(connectionMultiplexer, cacheSettings);
                });
            }

            // Inject application and worker to execute
            services.AddScoped(provider => new MockCachingOperation(provider));
            services.AddHostedService<Worker>();
        }
    }
}