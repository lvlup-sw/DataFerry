using Microsoft.Extensions.DependencyInjection;
using CacheObject.Providers;
using MockCachingOperation.Process;
using MockCachingOperation.Configuration;
using System.Security.Cryptography;

namespace MockCachingOperation
{
    public class Program(IServiceProvider serviceProvider)
    {
        private readonly IServiceProvider _serviceProvider = serviceProvider;

        public async Task ExecuteAsync()
        {
            try
            {
                // Get configuration
                /*
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
                Console.WriteLine($"Sent {results.Count} payloads");
                Console.WriteLine($"Cache count: {cacheProvider.Cache.GetItemCount()}");
                Console.WriteLine($"Returned items are different to payloads: {CompareItems(payloads, results)}");
                Console.WriteLine($"Cached items are identical to payloads: {CompareCachedItems(payloads, cacheItems)}");
            }
            catch (Exception ex)
            {
                Console.WriteLine("An error occurred while executing the program.", ex.Message);
            }
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

            for (int i = 0; i < payloads.Count; i++)
            {
                if (!payloads[i].Data.SequenceEqual(results[i].Data))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool CompareCachedItems(List<Payload> payloads, List<Tuple<string, Payload>> cachedPayloads)
        {
            if (payloads is null || cachedPayloads is null)
            {
                return false;
            }

            foreach (var payload in payloads)
            {
                bool equalsById = cachedPayloads.Any(cached => cached.Item2.Identifier.Equals(payload.Identifier));
                bool equalsByData = cachedPayloads.Any(cached => cached.Item2.Data.Equals(payload.Data));

                if (!equalsById || !equalsByData)
                {
                    return false;
                }
            }

            return true;
        }   
    }
}