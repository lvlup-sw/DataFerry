using CacheProvider.Caches;
using CacheProvider.Providers;
using MockCachingOperation.Process;
using MockCachingOperation.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Hosting;
using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace MockCachingOperation
{
    public class MockCachingOperation(IServiceProvider serviceProvider) : IHostedService
    {
        private readonly IServiceProvider _serviceProvider = serviceProvider;

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            // Get configuration
            var provider = _serviceProvider.GetService<IRealProvider<Payload>>();
            var appsettings = _serviceProvider.GetService<IOptions<AppSettings>>();
            var settings = _serviceProvider.GetService<IOptions<CacheSettings>>();

            // Null check
            ArgumentNullException.ThrowIfNull(provider);
            ArgumentNullException.ThrowIfNull(appsettings);
            ArgumentNullException.ThrowIfNull(settings);

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
                cacheProvider = new(provider, cache, settings);

                // Create some payloads
                List<Payload> payloads = [];
                while (payloads.Count < 100)
                    payloads.Add(CreatePayload());

                // Run the cache operation
                var tasks = payloads.Select(async payload =>
                {
                    return await cacheProvider.CheckCacheAsync(payload, payload.Identifier);
                });

                var cachedPayloads = await Task.WhenAll(tasks);
                List<Payload> results = [.. cachedPayloads];
                var cacheObj = cacheProvider.Cache as ConcurrentDictionary<string, (object, DateTime)>;
                var cacheItems = cacheObj?.Values.Select(item => item.Item1 as Payload).ToList();

                // Display the results
                Console.WriteLine($"Sent {results.Count} payloads to the cache.");
                Console.WriteLine($"Current cache count: {cacheObj?.Count} items.");
                bool areItemsDifferent = CompareItems(payloads, results);
                Console.WriteLine(areItemsDifferent
                    ? "\nThe returned items are DIFFERENT from the original payloads."
                    : "\nThe returned items are IDENTICAL to the original payloads.");
                bool areCachedItemsIdentical = CompareCachedItems(payloads, cacheItems!);
                Console.WriteLine(areCachedItemsIdentical
                    ? "The cached items are IDENTICAL to the original payloads.\n"
                    : "The cached items are DIFFERENT from the original payloads.\n");

                // Continue?
                Console.WriteLine("Continue? y/n");
                var input = Console.ReadKey();
                if (input.Key is ConsoleKey.N)
                    await StopAsync(CancellationToken.None);
                else Console.WriteLine("\n");
            }
            catch (Exception ex)
            {
                Console.WriteLine("An error occurred while executing the program.\n", ex.Message);
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

        private static bool CompareItems(List<Payload> payloads, List<Payload> results)
        {
            if (payloads.Count != results.Count)
            {
                return false;
            }

            // Check if the data in each payload is the same
            return payloads.Zip(results, (payload, result) => payload.Data.SequenceEqual(result.Data)).All(equal => equal);
        }

        private static bool CompareCachedItems(List<Payload> payloads, List<Payload> cachedPayloads)
        {
            // Null checks
            if (payloads is null || cachedPayloads is null)
            {
                return false;
            }

            // Sort the lists by Identifier for comparison
            var sortedPayloads = payloads.OrderBy(p => p.Identifier).ToList();
            var sortedCachedPayloads = cachedPayloads.OrderBy(p => p.Identifier).ToList();

            // Check if the payloads in each list are the same
            // This is a shallow comparison
            return sortedPayloads.SequenceEqual(sortedCachedPayloads[..100]);
        }
    }
}
