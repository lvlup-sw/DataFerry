using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Json;
using DataFerry.Json.Interfaces;
using Microsoft.IO;

namespace DataFerry.Json
{
    public class ArrayPoolingSerializer<T> : IJsonSerializer<T> where T : notnull
    {
        private readonly ArrayPool<byte> _arrayPool;
        private readonly RecyclableMemoryStreamManager _streamManager = new();

        public ArrayPoolingSerializer(ArrayPool<byte> arrayPool)
        {
            _arrayPool = arrayPool;
        }

        #region Deserialization

        public TValue? Deserialize<TValue>([StringSyntax("Json")] string json, JsonSerializerOptions? options = null)
        {
            int estimatedSize = Encoding.UTF8.GetByteCount(json);
            byte[] buffer = _arrayPool.Rent(estimatedSize);

            try
            {
                // Investigate optimizations here.. ideally we don't want to 
                // be making these allocations for every Deserialization call
                Utf8JsonReader reader = new(new ReadOnlySpan<byte>(buffer, 0, estimatedSize));

                try
                {
                    return JsonSerializer.Deserialize<TValue>(ref reader, options);
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException("Failed to deserialize input.", ex.GetBaseException());
                }
            }
            finally
            {
                _arrayPool.Return(buffer);
            }
        }

        public async Task<TValue?> DeserializeAsync<TValue>([StringSyntax("Json")] string json, JsonSerializerOptions? options = null)
        {
            using var stream = _streamManager.GetStream(Encoding.UTF8.GetBytes(json));
            try
            {
                return await JsonSerializer.DeserializeAsync<TValue>(stream, options);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException("Failed to deserialize async input.", ex.GetBaseException());
            }
        }

        #endregion
        #region Serialization

        public string Serialize<TValue>(TValue value, JsonSerializerOptions? options = null)
        {
            using var stream = _streamManager.GetStream();
            try
            {
                JsonSerializer.Serialize(stream, value, options);
                stream.Seek(0, SeekOrigin.Begin);
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException("Failed to serialize input.", ex.GetBaseException());
            }
        }

        public async Task<string> SerializeAsync<TValue>(TValue value, JsonSerializerOptions? options = null)
        {
            using var stream = _streamManager.GetStream();
            try
            {
                await JsonSerializer.SerializeAsync(stream, value, options);
                stream.Seek(0, SeekOrigin.Begin);
                using var reader = new StreamReader(stream);
                return await reader.ReadToEndAsync();
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException("Failed to serialize async input.", ex.GetBaseException());
            }
        }

        #endregion
    }
}
