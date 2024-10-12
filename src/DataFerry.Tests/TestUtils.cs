using System.Security.Cryptography;
using lvlup.DataFerry.Tests.TestModels;

namespace lvlup.DataFerry.Tests
{
    internal static class TestUtils
    {
        public static Payload CreatePayloadWithInput(string key)
        {
            Payload payload = new()
            {
                Identifier = key,
                Data = GenerateRandomString(100),
                Property = true,
                Version = 1
            };

            return payload;
        }

        public static Payload CreatePayloadRandom()
        {
            Payload payload = new()
            {
                Identifier = GenerateRandomString(10),
                Data = GenerateRandomString(100),
                Property = true,
                Version = 1
            };

            return payload;
        }

        public static string GenerateRandomString(int length)
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

        public static bool Compares(Payload payload, Payload result)
        {
            return payload.Data.SequenceEqual(result.Data);
        }

        public static bool Compares(List<Payload> payloads, List<Payload> results)
        {
            if (payloads.Count != results.Count)
            {
                return false;
            }

            // Check if the data in each payload is the same
            return payloads.Zip(results, (payload, result) => payload.Data.SequenceEqual(result.Data)).All(equal => equal);
        }
    }
}
