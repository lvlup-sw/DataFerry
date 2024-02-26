using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using CacheObject.Caches;
using CacheObject.Providers;
using MockCachingOperation.Process;
using MockCachingOperation.Configuration;
using Microsoft.Extensions.Hosting;

namespace MockCachingOperation
{
    public class Startup(IConfiguration configuration)
    {
        public IConfiguration Configuration { get; } = configuration;

        public void ConfigureServices(IServiceCollection services)
        {
            // Add configuration
            var appSettingsSection = Configuration.GetSection("AppSettings");
            services.Configure<AppSettings>(options => appSettingsSection.Bind(options));

            // Inject services
            //services.AddSingleton<ICache<Payload>, DistributedCache<Payload>>();
            services.AddSingleton<ICache<Payload>, TestCache<Payload>>();
            services.AddSingleton<IRealProvider<Payload>, RealProvider>();

            /* Uncomment to use Redis cache
            services.AddStackExchangeRedisCache(options =>
            {
                options.Configuration = Configuration.GetConnectionString("Redis");
                options.InstanceName = "MockCachingOperation";
            });
            */

            // Inject application and worker to execute
            services.AddScoped(provider => new MockCachingOperation(provider));
            services.AddHostedService<Worker>();
        }
    }
}