using CacheProvider.Providers;
using MockCachingOperation.Process;
using MockCachingOperation.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Hosting;
using System.Security.Cryptography;
using StackExchange.Redis;
using Microsoft.Extensions.Logging;
using CacheProvider.Providers.Interfaces;

namespace MockCachingOperation
{
    public class MockCachingOperation(IServiceProvider serviceProvider) : IHostedService
    {
        private readonly IServiceProvider _serviceProvider = serviceProvider;

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            try
            {
                // Get configuration
                var provider    = _serviceProvider.GetRequiredService<IRealProvider<Payload>>();
                var _settings   = _serviceProvider.GetRequiredService<IOptions<CacheSettings>>();
                var logger      = _serviceProvider.GetRequiredService<ILogger<MockCachingOperation>>();
                var connection  = _serviceProvider.GetRequiredService<IConnectionMultiplexer>();
                var appsettings = _serviceProvider.GetService<IOptions<AppSettings>>();
                CacheSettings settings = _settings.Value;

                // Create the cache provider
                CacheProvider<Payload> cacheProvider = new(connection, provider, settings, logger);

                // Create some payloads
                List<Payload> payloads = [];
                while (payloads.Count < 100)
                    payloads.Add(CreatePayload());

                // Run the cache operation
                var tasks = payloads.Select(async payload =>
                {
                    return await cacheProvider.GetFromCacheAsync(payload, payload.Identifier);
                });

                // Wait for the tasks to complete
                var cachedPayloads = await Task.WhenAll(tasks);
                List<Payload> cachedPayloadsList = cachedPayloads
                    .Where(payload => payload is not null)
                    .Cast<Payload>()
                    .ToList();

                // Display the results
                Console.WriteLine($"Sent {payloads.Count} payloads to the cache.");
                Console.WriteLine($"Current cache count: {GetCount(connection)} entries.");
                bool areDifferent = Compares(payloads, cachedPayloadsList);
                Console.WriteLine(areDifferent
                    ? "\nThe retrieved entries are DIFFERENT from the original payloads."
                    : "\nThe retrieved entries are IDENTICAL to the original payloads.");

                Console.WriteLine("Beginning batch retrieval operation...\n");
                Task.Delay(2000, cancellationToken)
                    .Wait(cancellationToken);

                // Get the payloads from the cache
                var retrievedPayloads = await cacheProvider.GetBatchFromCacheAsync(
                    payloads.ToDictionary(payload => payload.Identifier, payload => payload),
                    payloads.Select(payload => payload.Identifier)                       
                );

                // Display the results
                Console.WriteLine($"Retrieved {retrievedPayloads.Count} payloads from the cache.");
                Console.WriteLine($"Current cache count: {GetCount(connection)} entries.");
                areDifferent = Compares(cachedPayloadsList, retrievedPayloads.Values.ToList());
                Console.WriteLine(areDifferent
                    ? "\nThe retrieved entries are DIFFERENT from the cached payloads."
                    : "\nThe retrieved entries are IDENTICAL to the cached payloads.");

                // Continue?
                Console.WriteLine("Continue? y/n");
                var input = Console.ReadKey();
                if (input.Key is ConsoleKey.N)
                    await StopAsync(CancellationToken.None);
                else Console.WriteLine("\n");
            }
            catch (Exception ex)
            {
                Console.WriteLine("An error occurred while executing the program.\n" + ex.Message);
                Console.WriteLine("Press any key to exit...");
                Console.ReadKey();
                await StopAsync(CancellationToken.None);
            }
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            Environment.Exit(0);
            return Task.CompletedTask;
        }

        private static Payload CreatePayload()
        {
            List<string> data = [];
            while (data.Count < 200)
                data.Add(GenerateRandomString(100));

            Payload payload = new()
            {
                Identifier = GenerateRandomString(10),
                Data = data,
                Property = true,
                Version = 1
            };

            return payload;
        }

        private static string GenerateRandomString(int length)
        {
            if (length <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(length), "Length must be positive.");
            }

            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
            var randomBytes = new byte[length];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(randomBytes);
            }

            return new string(randomBytes.Select(b => chars[b % chars.Length]).ToArray());
        }

        private static bool Compares(List<Payload> payloads, List<Payload> results)
        {
            if (payloads.Count != results.Count)
            {
                return false;
            }

            // Check if the data in each payload is the same
            return payloads.Zip(results, (payload, result) => payload.Data.SequenceEqual(result.Data)).All(equal => equal);
        }

        public static long GetCount(IConnectionMultiplexer connection)
        {
            var server = connection.GetServer(connection.GetEndPoints().First());
            return server.DatabaseSize();
        }
    }
}
