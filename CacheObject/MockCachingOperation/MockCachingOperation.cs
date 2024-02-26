using CacheObject.Providers;
using Microsoft.Extensions.Hosting;
using MockCachingOperation.Process;
using System.Collections.Concurrent;
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
                var cacheItems = cacheProvider.Cache.GetItems();

                // Display the results
                Console.WriteLine($"Sent {results.Count} payloads to the cache.");
                Console.WriteLine($"Current cache count: {cacheProvider.Cache.GetItemCount()} items.");
                bool areItemsDifferent = CompareItems(payloads, results);
                Console.WriteLine(areItemsDifferent
                    ? "\nThe returned items are DIFFERENT from the original payloads."
                    : "\nThe returned items are IDENTICAL to the original payloads.");
                bool areCachedItemsIdentical = CompareCachedItems(payloads, cacheItems);
                Console.WriteLine(areCachedItemsIdentical
                    ? "The cached items are IDENTICAL to the original payloads.\n"
                    : "The cached items are DIFFERENT from the original payloads.\n");

                // Continue?
                Console.WriteLine("Continue? y/n");
                var input = Console.ReadKey();
                if (input.Key is ConsoleKey.N)
                    await StopAsync(new CancellationToken());
                else Console.WriteLine("\n");
            }
            catch (Exception ex)
            {
                Console.WriteLine("An error occurred while executing the program.\n", ex.Message);
                Console.WriteLine("Press any key to exit...");
                Console.ReadKey();
                await StopAsync(new CancellationToken());
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

        private static bool CompareCachedItems(List<Payload> payloads, object cachedPayloads)
        {
            // Null checks
            if (payloads is null || cachedPayloads is null)
            {
                return false;
            }

            // Convert the cached items to a dictionary
            var cachedItems = cachedPayloads as ConcurrentDictionary<string, Payload>;
            if (cachedItems is null) return false;

            // Check if the cached items are identical to the payloads sent
            foreach (var payload in payloads)
            {
                bool doesPayloadExistInCache = cachedItems.Values.Any(cachedPayload =>
                {
                    return cachedPayload.Identifier == payload.Identifier
                        && cachedPayload.Data.SequenceEqual(payload.Data);
                });

                if (!doesPayloadExistInCache) return false;
            }

            return true;
        }
    }
}
