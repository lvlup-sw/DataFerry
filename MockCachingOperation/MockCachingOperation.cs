using CacheObject.Providers;
using Microsoft.Extensions.Hosting;
using MockCachingOperation.Process;
using System.Collections.Concurrent;
using System.Linq;
using System.Security.Cryptography;

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
                /* This keeps throwing an error for some reason...
                AppSettings appSettings = _serviceProvider.GetService<AppSettings>()
                    ?? throw new InvalidOperationException($"Could not retrieve service of type {typeof(AppSettings)}");
                */

                // Create a cache provider
                CacheProvider<Payload> cacheProvider = new(_serviceProvider);

                // Create some payloads
                List<Payload> payloads = [];
                while (payloads.Count < 100)
                    payloads.Add(CreatePayload());

                // Run the cache operation
                var tasks = payloads.Select(async payload =>
                {
                    return await cacheProvider.CacheObjectAsync(payload, payload.Identifier);
                });

                var cachedPayloads = await Task.WhenAll(tasks);
                List<Payload> results = [.. cachedPayloads];
                var cache = cacheProvider.Cache as ConcurrentDictionary<string, Payload>;
                var cacheItems = cache?.Values.ToList();

                // Display the results
                Console.WriteLine($"Sent {results.Count} payloads to the cache.");
                Console.WriteLine($"Current cache count: {cache?.Count} items.");
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
            var sortedCachedPayloads = cachedPayloads.OrderBy(p => p.Identifier).ToList()[..100];

            // Check if the payloads in each list are the same
            return sortedPayloads.SequenceEqual(sortedCachedPayloads);
        }
    }
}
