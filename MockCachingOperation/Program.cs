using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Amazon.Extensions.Configuration.SystemsManager;

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
                    // Add Configurations
                    config.SetBasePath(Directory.GetCurrentDirectory());
                    config.AddJsonFile("./appsettings.json", optional: false, reloadOnChange: true);
                    config.AddEnvironmentVariables();
                })
                .ConfigureServices((hostContext, services) =>
                {
                    // Create an instance of Startup
                    var startup = new Startup(hostContext.Configuration);

                    // Call ConfigureServices
                    startup.ConfigureServices(services);
                }).ConfigureLogging(logging =>
                {   // Set to whatever level you want
                    logging.ClearProviders();
                    logging.AddConsole();
                    logging.AddFilter("Microsoft", LogLevel.Information);
                    logging.AddFilter("System", LogLevel.Information);
                    logging.AddFilter("MockCachingOperation", LogLevel.Information);
                });
    }
}