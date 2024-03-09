using CacheProvider.Providers;
using MockCachingOperation.Process;
using MockCachingOperation.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Hosting;
using System.Collections.Concurrent;
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
            // Get configuration
            var provider    = _serviceProvider.GetService<IRealProvider<Payload>>();
            var appsettings = _serviceProvider.GetService<IOptions<AppSettings>>();
            var _settings   = _serviceProvider.GetService<IOptions<CacheSettings>>();
            var logger      = _serviceProvider.GetService<ILogger<MockCachingOperation>>();
            var connection  = _serviceProvider.GetService<IConnectionMultiplexer>() ?? null;

            // Null check
            ArgumentNullException.ThrowIfNull(provider);
            ArgumentNullException.ThrowIfNull(appsettings);
            ArgumentNullException.ThrowIfNull(_settings);
            ArgumentNullException.ThrowIfNull(logger);
            CacheSettings settings = _settings.Value;

            // Setup the cache provider
            CacheProvider<Payload> cacheProvider;
            CacheType cache = appsettings.Value.CacheType switch
            {
                "Local" => CacheType.Local,
                "Distributed" => CacheType.Distributed,
                _ => throw new ArgumentException("The CacheType is invalid."),
            };

            // Try to create the cache provider
            try
            {
                cacheProvider = new(provider, cache, settings, logger, connection);

                // Create some payloads
                List<Payload> payloads = [];
                while (payloads.Count < 100)
                    payloads.Add(CreatePayload());

                // Run the cache operation
                var tasks = payloads.Select(async payload =>
                {
                    return (cache) switch
                    { 
                        CacheType.Local => cacheProvider.CheckCache(payload, payload.Identifier),
                        CacheType.Distributed => await cacheProvider.CheckCacheAsync(payload, payload.Identifier),
                        _ => throw new InvalidOperationException("The CacheType is invalid.")
                    };
                });

                var cachedPayloads = await Task.WhenAll(tasks);
                List<Payload> results = [.. cachedPayloads];
                var cacheObj = cacheProvider.Cache as ConcurrentDictionary<string, (object, DateTime)>;
                var caches = cacheObj?.Values.Select( item => item.Item1 as Payload).ToList();

                // Display the results
                Console.WriteLine($"Sent {results.Count} payloads to the cache.");
                Console.WriteLine($"Current cache count: {cacheObj?.Count} s.");
                bool aresDifferent = Compares(payloads, results);
                Console.WriteLine(aresDifferent
                    ? "\nThe returned entries are DIFFERENT from the original payloads."
                    : "\nThe returned entries are IDENTICAL to the original payloads.");
                bool arePayloadsCached = CompareCacheds(payloads, caches!);
                Console.WriteLine(arePayloadsCached
                    ? "The payloads HAVE been found in the cache.\n"
                    : "The payloads HAVE NOT been found in the cache.\n");

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

        private static bool CompareCacheds(List<Payload> payloads, List<Payload> cachedPayloads)
        {
            // Null checks
            if (payloads is null || cachedPayloads is null)
            {
                return false;
            }

            List<Payload> commons = cachedPayloads
                .Where(p1 => payloads.Exists(p2 => p2.Identifier == p1.Identifier))
                .ToList();

            return commons.Count == payloads.Count;
        }
    }
}
