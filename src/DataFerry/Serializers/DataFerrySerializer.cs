using System.Buffers;
using System.Text.Json;

namespace lvlup.DataFerry.Serializers
{
    public class DataFerrySerializer : IDataFerrySerializer
    {
        public T? Deserialize<T>(ReadOnlySequence<byte> source, JsonSerializerOptions? options = null)
        {
            throw new NotImplementedException();
        }

        public ValueTask<T?> DeserializeAsync<T>(ReadOnlySequence<byte> source, JsonSerializerOptions? options = null, CancellationToken token = default)
        {
            throw new NotImplementedException();
        }

        public void Serialize<T>(T value, IBufferWriter<byte> target, JsonSerializerOptions? options = null)
        {
            throw new NotImplementedException();
        }

        public string Serialize<T>(T value, JsonSerializerOptions? options = null)
        {
            throw new NotImplementedException();
        }

        public ValueTask SerializeAsync<T>(T value, IBufferWriter<byte> target, JsonSerializerOptions? options = null, CancellationToken token = default)
        {
            throw new NotImplementedException();
        }

        public ValueTask<string> SerializeAsync<T>(T value, JsonSerializerOptions? options = null, CancellationToken token = default)
        {
            throw new NotImplementedException();
        }
    }
}
