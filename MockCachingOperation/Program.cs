using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using CacheProvider.Caches;
using Amazon.Extensions.Configuration.SystemsManager;
using Microsoft.Extensions.Logging;

namespace MockCachingOperation
{
    public class Program()
    {
        public static void Main(string[] args)
        {
            CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureAppConfiguration((hostContext, config) =>
                {
                    // Add AppSettings.json and AWS Systems Manager
                    config.SetBasePath(Directory.GetCurrentDirectory());
                    // ~/appsettings.json?
                    config.AddJsonFile("../../../appsettings.json", optional: false, reloadOnChange: true);
                    /*config.AddSystemsManager("/MockCachingOperation", false);
                    config.AddSystemsManager(configureSource =>
                    {
                        configureSource.Path = "/MockCachingOperation";
                        configureSource.Optional = false;
                        configureSource.ParameterProcessor = new JsonParameterProcessor();
                        configureSource.ReloadAfter = TimeSpan.FromHours(1);
                    });*/
                    config.AddEnvironmentVariables();
                })
                .ConfigureServices((hostContext, services) =>
                {
                    // Create an instance of Startup
                    var startup = new Startup(hostContext.Configuration);

                    // Call ConfigureServices
                    startup.ConfigureServices(services);
                }).ConfigureLogging(logging =>
                {
                    logging.ClearProviders();
                    logging.AddConsole();
                    logging.AddFilter("Microsoft", LogLevel.Warning);
                    logging.AddFilter("System", LogLevel.Warning);
                    logging.AddFilter("MockCachingOperation", LogLevel.Warning);
                });
    }
}