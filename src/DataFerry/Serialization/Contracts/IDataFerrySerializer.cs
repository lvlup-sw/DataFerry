using System.Buffers;
using System.Text.Json;

namespace lvlup.DataFerry.Serialization.Contracts;

/// <summary>
/// A contract for serializing and deserializing data using System.Text.Json.
/// </summary>
public interface IDataFerrySerializer
{
    /// <summary>
    /// Deserializes a <typeparamref name="T"/> from a <see cref="ReadOnlySequence{T}"/> of bytes.
    /// </summary>
    /// <typeparam name="T">The type to deserialize to.</typeparam>
    /// <param name="source">The <see cref="ReadOnlySequence{T}"/> of bytes to deserialize from.</param>
    /// <param name="options">Options to control the behavior during deserialization.</param>
    /// <returns>A <typeparamref name="T"/> instance representing the deserialized object, or null if deserialization fails.</returns>
    T? Deserialize<T>(ReadOnlySequence<byte> source, JsonSerializerOptions? options = null);

    /// <summary>
    /// Serializes a <typeparamref name="T"/> to an <see cref="IBufferWriter{T}"/>.
    /// </summary>
    /// <typeparam name="T">The type to serialize.</typeparam>
    /// <param name="value">The <typeparamref name="T"/> instance to serialize.</param>
    /// <param name="destination">The <see cref="IBufferWriter{T}"/> to serialize to.</param>
    /// <param name="options">Options to control the behavior during serialization.</param>
    void Serialize<T>(T value, IBufferWriter<byte> destination, JsonSerializerOptions? options = null);

    /// <summary>
    /// Serializes a <typeparamref name="T"/> to a JSON string.
    /// </summary>
    /// <typeparam name="T">The type to serialize.</typeparam>
    /// <param name="value">The <typeparamref name="T"/> instance to serialize.</param>
    /// <param name="options">Options to control the behavior during serialization.</param>
    /// <returns>A JSON string representing the serialized object.</returns>
    string Serialize<T>(T value, JsonSerializerOptions? options = null);

    /// <summary>
    /// Serializes a <typeparamref name="T"/> to a byte array.
    /// </summary>
    /// <typeparam name="T">The type to serialize.</typeparam>
    /// <param name="value">The <typeparamref name="T"/> instance to serialize.</param>
    /// <param name="options">Options to control the behavior during serialization.</param>
    /// <returns>A byte array representing the serialized object.</returns>
    byte[] SerializeToUtf8Bytes<T>(T value, JsonSerializerOptions? options = null);

    /// <summary>
    /// Asynchronously deserializes a <typeparamref name="T"/> from a <see cref="byte[]"/> of serialized bytes.
    /// </summary>
    /// <typeparam name="T">The type to deserialize to.</typeparam>
    /// <param name="serializedValue">The <see cref="byte[]"/> of serialized bytes to deserialize from.</param>
    /// <param name="options">Options to control the behavior during deserialization.</param>
    /// <param name="token">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A <see cref="ValueTask{TResult}"/> that represents the asynchronous operation. The task result contains a <typeparamref name="T"/> instance representing the deserialized object, or null if deserialization fails.</returns>
    ValueTask<T?> DeserializeAsync<T>(byte[] serializedValue, JsonSerializerOptions? options = null, CancellationToken token = default);

    /// <summary>
    /// Asynchronously serializes a <typeparamref name="T"/> and writes it to a <see cref="IBufferWriter{T}"/>.
    /// </summary>
    /// <typeparam name="T">The type to serialize.</typeparam>
    /// <param name="value">The <typeparamref name="T"/> instance to serialize.</param>
    /// <param name="destination">The <see cref="IBufferWriter{T}"/> to serialize to.</param>
    /// <param name="options">Options to control the behavior during serialization.</param>
    /// <param name="token">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A <see cref="ValueTask"/> that represents the asynchronous operation.</returns>
    ValueTask SerializeAsync<T>(T value, IBufferWriter<byte> destination, JsonSerializerOptions? options = null, CancellationToken token = default);

    /// <summary>
    /// Asynchronously serializes a <typeparamref name="T"/> to a byte array.
    /// </summary>
    /// <typeparam name="T">The type to serialize.</typeparam>
    /// <param name="value">The <typeparamref name="T"/> instance to serialize.</param>
    /// <param name="options">Options to control the behavior during serialization.</param>
    /// <param name="token">A cancellation token that can be used to cancel the operation.</param>
    /// <returns>A <see cref="ValueTask{TResult}"/> that represents the asynchronous operation. The task result contains a byte array representing the serialized object.</returns>
    ValueTask<byte[]> SerializeAsync<T>(T value, JsonSerializerOptions? options = null, CancellationToken token = default);
}