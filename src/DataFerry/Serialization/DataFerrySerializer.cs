using System.Buffers;
using System.Text.Json;
using lvlup.DataFerry.Serialization.Contracts;
using Microsoft.Extensions.Logging;
using Microsoft.IO;

namespace lvlup.DataFerry.Serialization;

/// <summary>
/// Implements the <see cref="IDataFerrySerializer"/> interface for serializing and deserializing data using JSON.
/// </summary>
/// <remarks>
/// A <see cref="RecyclableMemoryStreamManager"/> is used to rent stream instances.
/// This class is optimally used with an <see cref="IBufferWriter{T}"/>.
/// </remarks>
public sealed class DataFerrySerializer : IDataFerrySerializer
{
    private readonly RecyclableMemoryStreamManager _streamManager;
    private readonly ILogger<DataFerrySerializer> _logger;

    /// <summary>
    /// The tag passed to the <see cref="RecyclableMemoryStreamManager"/> <c>GetStream()</c> method.
    /// </summary>
    public string StreamTag { get; set; } = "DataFerry";

    /// <summary>
    /// Initializes a new instance of the <see cref="DataFerrySerializer"/> class.
    /// </summary>
    /// <param name="streamManager">The <see cref="RecyclableMemoryStreamManager"/> to use for stream management.</param>
    /// <param name="logger">The <see cref="ILogger"/> to use for logging.</param>
    public DataFerrySerializer(
        RecyclableMemoryStreamManager streamManager,
        ILogger<DataFerrySerializer> logger)
    {
        _streamManager = streamManager;
        _logger = logger;
    }

    /// <inheritdoc/>
    public T? Deserialize<T>(
        ReadOnlySequence<byte> source,
        JsonSerializerOptions? options = null)
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
    public void Serialize<T>(
        T value,
        IBufferWriter<byte> destination,
        JsonSerializerOptions? options = null)
    {
        try
        {
            // This will write to the target buffer
            using var writer = new Utf8JsonWriter(destination);
            JsonSerializer.Serialize(writer, value, options);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex.GetBaseException(), "Failed to serialize the value.");
            throw;
        }
    }

    /// <inheritdoc/>
    public string Serialize<T>(
        T value,
        JsonSerializerOptions? options = null)
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
    public byte[] SerializeToUtf8Bytes<T>(
        T value,
        JsonSerializerOptions? options = null)
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
    public async ValueTask<T?> DeserializeAsync<T>(
        byte[] serializedValue,
        JsonSerializerOptions? options = null,
        CancellationToken token = default)
    {
        try
        {
            // Get a stream from the pool
            await using var stream = _streamManager.GetStream(StreamTag, serializedValue, 0, serializedValue.Length);

            return await JsonSerializer.DeserializeAsync<T>(stream, options, token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex.GetBaseException(), "Failed to deserialize the value.");
            return default;
        }
    }

    /// <inheritdoc/>
    public async ValueTask SerializeAsync<T>(
        T value,
        IBufferWriter<byte> destination,
        JsonSerializerOptions? options = null,
        CancellationToken token = default)
    {
        try
        {
            // Get a memory stream from the pool
            await using var stream = _streamManager.GetStream(StreamTag);

            await JsonSerializer.SerializeAsync(stream, value, options, token).ConfigureAwait(false);

            // Try to get the buffer from the stream
            if (stream.TryGetBuffer(out ArraySegment<byte> bufferSegment))
            {
                // Write the buffer segment to the target
                // This allows us to avoid an allocation
                // if the target is also rented from a pool
                destination.Write(bufferSegment.AsSpan());
            }
            else
            {
                // If this fails somehow, we fallback
                // to allocating the buffer
                _logger.LogWarning("Unable to get buffer from MemoryStream in SerializeAsync; using fallback with additional allocation.");

                destination.Write(stream.GetBuffer().AsSpan(0, (int)stream.Position));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex.GetBaseException(), "Failed to serialize the value asynchronously.");
            throw;
        }
    }

    /// <inheritdoc/>
    public async ValueTask<byte[]> SerializeAsync<T>(
        T value,
        JsonSerializerOptions? options = null,
        CancellationToken token = default)
    {
        try
        {
            // Get a stream from the pool using the rented buffer
            await using var stream = _streamManager.GetStream(StreamTag);

            await JsonSerializer.SerializeAsync(stream, value, options, token).ConfigureAwait(false);

            // Try to get the buffer from the stream
            if (stream.TryGetBuffer(out ArraySegment<byte> bufferSegment))
            {
                // Write the buffer segment to the new byte[]
                return [.. bufferSegment];
            }

            // If this fails somehow, we fall back
            // to allocating the entire buffer
            _logger.LogWarning("Unable to get buffer from MemoryStream in SerializeAsync; using fallback with additional allocation.");

            return stream.GetBuffer()
                .AsSpan(0, (int)stream.Position)
                .ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex.GetBaseException(), "Failed to serialize the value asynchronously.");
            throw;
        }
    }
}