using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using CacheObject.Caches;
using CacheObject.Providers;
using MockCachingOperation.Process;
using MockCachingOperation.Configuration;

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
            services.AddSingleton<ICache<Payload>, TestCache<Payload>>();
            services.AddSingleton<IRealProvider<Payload>, RealProvider>();
        }

        public static void Main(string[] args)
        {
            var host = Host.CreateDefaultBuilder(args)
                .ConfigureServices((hostContext, services) =>
                {
                    // Create an instance of Startup
                    var startup = new Startup(hostContext.Configuration);

                    // Call ConfigureServices
                    startup.ConfigureServices(services);
                })
                .Build();

            // Execute the program
            Program program = new(host.Services);
            _ = program.ExecuteAsync();
        }
    }
}