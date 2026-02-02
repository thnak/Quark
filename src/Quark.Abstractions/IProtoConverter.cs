using System;
using System.IO;

namespace Quark.Abstractions;

/// <summary>
/// Interface for custom ProtoBuf converters.
/// Implement this interface to provide custom serialization logic for specific types.
/// </summary>
/// <typeparam name="T">The type to convert.</typeparam>
public interface IProtoConverter<T>
{
    /// <summary>
    /// Serializes the specified value to the stream.
    /// </summary>
    /// <param name="stream">The stream to write to.</param>
    /// <param name="value">The value to serialize.</param>
    void Serialize(Stream stream, T value);

    /// <summary>
    /// Deserializes a value from the stream.
    /// </summary>
    /// <param name="stream">The stream to read from.</param>
    /// <returns>The deserialized value.</returns>
    T Deserialize(Stream stream);
}
