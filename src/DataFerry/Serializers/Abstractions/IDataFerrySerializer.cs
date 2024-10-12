using System.Buffers;
using System.Text.Json;

namespace lvlup.DataFerry.Serializers.Abstractions
{
    public interface IDataFerrySerializer
    {
        T? Deserialize<T>(ReadOnlySequence<byte> source, JsonSerializerOptions? options = default);

        void Serialize<T>(T value, IBufferWriter<byte> target, JsonSerializerOptions? options = default);

        string Serialize<T>(T value, JsonSerializerOptions? options = default);

        ValueTask<T?> DeserializeAsync<T>(ReadOnlySequence<byte> source, JsonSerializerOptions? options = default, CancellationToken token = default);

        ValueTask SerializeAsync<T>(T value, IBufferWriter<byte> target, JsonSerializerOptions? options = default, CancellationToken token = default);

        ValueTask<string> SerializeAsync<T>(T value, JsonSerializerOptions? options = default, CancellationToken token = default);
    }
}
