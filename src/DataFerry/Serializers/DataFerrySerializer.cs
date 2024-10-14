using Microsoft.Extensions.Logging;
using Microsoft.IO;
using System.Buffers;
using System.Text.Json;

namespace lvlup.DataFerry.Serializers
{
    /// <summary>
    /// Implements the <see cref="IDataFerrySerializer"/> interface for serializing and deserializing data using JSON.
    /// </summary>
    /// <remarks>
    /// This implementation utilizes pooled resources for efficient buffer management. 
    /// An <see cref="ArrayPool{T}"/> is used to rent and return buffers.
    /// A <see cref="RecyclableMemoryStreamManager"/> is used to rent stream instances.
    /// </remarks>
    public sealed class DataFerrySerializer : IDataFerrySerializer
    {
        private readonly StackArrayPool<byte> _arrayPool;
        private readonly RecyclableMemoryStreamManager _streamManager;
        private readonly ILogger<DataFerrySerializer> _logger;

        /// <summary>
        /// The tag passed to the <see cref="RecyclableMemoryStreamManager"/> <c>GetStream()</c> method.
        /// </summary>
        public string StreamTag { get; set; } = "DataFerry";

        /// <summary>
        /// Initializes a new instance of the <see cref="DataFerrySerializer"/> class.
        /// </summary>
        /// <param name="arrayPool">The <see cref="ArrayPool{T}"/> to use for buffer management.</param>
        /// <param name="streamManager">The <see cref="RecyclableMemoryStreamManager"/> to use for stream management.</param>
        /// <param name="logger">The <see cref="ILogger"/> to use for logging.</param>
        public DataFerrySerializer(
            StackArrayPool<byte> arrayPool,
            RecyclableMemoryStreamManager streamManager,
            ILogger<DataFerrySerializer> logger)
        {
            _arrayPool = arrayPool;
            _streamManager = streamManager;
            _logger = logger;
        }

        /// <inheritdoc/>
        public T? Deserialize<T>(ReadOnlySequence<byte> source, JsonSerializerOptions? options = default)
        {
            try
            {
                var reader = new Utf8JsonReader(source);
                return JsonSerializer.Deserialize<T>(ref reader, options);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.GetBaseException(), "Failed to deserialize the value.");
                return default;
            }
        }

        /// <inheritdoc/>
        public void Serialize<T>(T value, IBufferWriter<byte> target, JsonSerializerOptions? options = default)
        {
            try
            {
                // This will write to the target buffer
                using var writer = new Utf8JsonWriter(target);
                JsonSerializer.Serialize(writer, value, options);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.GetBaseException(), "Failed to serialize the value.");
                throw;
            }
        }

        /// <inheritdoc/>
        public string Serialize<T>(T value, JsonSerializerOptions? options = default)
        {
            try
            {
                return JsonSerializer.Serialize(value, options);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.GetBaseException(), "Failed to serialize the value.");
                throw;
            }
        }

        /// <inheritdoc/>
        public byte[] SerializeToUtf8Bytes<T>(T value, JsonSerializerOptions? options = default)
        {
            try
            {
                return JsonSerializer.SerializeToUtf8Bytes(value, options);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.GetBaseException(), "Failed to serialize the value.");
                throw;
            }
        }

        /// <inheritdoc/>
        /// <remarks>
        /// This method rents a buffer from the <see cref="ArrayPool{T}"/> and uses it to serialize the data. 
        /// The buffer is returned to the pool after the operation completes.
        /// </remarks>
        public async ValueTask<T?> DeserializeAsync<T>(
            ReadOnlySequence<byte> source, 
            JsonSerializerOptions? options = default, 
            CancellationToken token = default)
        {
            // Rent a buffer from the pool
            byte[] buffer = _arrayPool.Rent((int)source.Length);

            try
            {
                // Copy the data to the rented buffer
                source.CopyTo(buffer);

                // Get a stream from the pool using the rented buffer
                using var stream = _streamManager.GetStream(StreamTag, buffer, 0, (int)source.Length);

                return await JsonSerializer.DeserializeAsync<T>(stream, options, token);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.GetBaseException(), "Failed to deserialize the value.");
                return default;
            }
            finally
            {
                // Return the buffer to the pool
                _arrayPool.Return(buffer);
            }
        }

        /// <inheritdoc/>
        public async ValueTask SerializeAsync<T>(
            T value,
            IBufferWriter<byte> target,
            JsonSerializerOptions? options = default,
            CancellationToken token = default)
        {
            try
            {
                // Get a memory stream from the pool
                using var stream = _streamManager.GetStream(StreamTag);

                await JsonSerializer.SerializeAsync(stream, value, options, token);

                // Try to get the buffer from the stream
                if (stream.TryGetBuffer(out ArraySegment<byte> bufferSegment))
                {
                    // Write the buffer segment to the target
                    // This allows us to avoid an allocation
                    // if the target is also rented from a pool
                    target.Write(bufferSegment.AsSpan());
                }
                else
                {
                    // If this fails somehow, we
                    // fallback to allocating the buffer
                    _logger.LogWarning("Unable to get buffer from MemoryStream in SerializeAsync; using fallback with additional allocation.");

                    var buffer = stream.GetBuffer();
                    target.Write(buffer.AsSpan(0, (int)stream.Position));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.GetBaseException(), "Failed to serialize the value asynchronously.");
                throw;
            }
        }

        /// <inheritdoc/>
        public async ValueTask<byte[]> SerializeAsync<T>(T value, JsonSerializerOptions? options = default, CancellationToken token = default)
        {
            try
            {
                // Get a stream from the pool using the rented buffer
                using var stream = _streamManager.GetStream(StreamTag);

                await JsonSerializer.SerializeAsync(stream, value, options, token);

                // Try to get the buffer from the stream
                if (stream.TryGetBuffer(out ArraySegment<byte> bufferSegment))
                {
                    // Write the buffer segment to the a new byte[]
                    return [.. bufferSegment];
                }
                else
                {
                    // If this fails somehow, we
                    // fallback to allocating the buffer
                    _logger.LogWarning("Unable to get buffer from MemoryStream in SerializeAsync; using fallback with additional allocation.");

                    return stream.GetBuffer()
                        .AsSpan(0, (int)stream.Position)
                        .ToArray();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex.GetBaseException(), "Failed to serialize the value asynchronously.");
                throw;
            }
        }
    }
}