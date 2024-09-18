using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace DataFerry.Json.Interfaces
{
    public interface IJsonSerializer<T> where T : notnull
    {
        TValue? Deserialize<TValue>([StringSyntax("Json")] string json, JsonSerializerOptions? options = null);
        string Serialize<TValue>(TValue value, JsonSerializerOptions? options = null);
    }
}
