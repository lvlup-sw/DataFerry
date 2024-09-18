using DataFerry.Json.Interfaces;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace DataFerry.Json
{
    internal class DefaultSerializer<T> : IJsonSerializer<T> where T : notnull
    {
        public TValue? Deserialize<TValue>([StringSyntax("Json")] string json, JsonSerializerOptions? options = null)
        {
            return JsonSerializer.Deserialize<TValue>(json, options);
        }

        public string Serialize<TValue>(TValue value, JsonSerializerOptions? options = null)
        {
            return JsonSerializer.Serialize(value, options);

        }
    }
}
