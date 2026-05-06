using System.Buffers;

namespace Quark.Serialization.Abstractions.Abstractions;

/// <summary>
/// Top-level serializer interface: serialize/deserialize a full object graph.
/// </summary>
public interface ISerializer
{
    /// <summary>Serializes <paramref name="value"/> to <paramref name="output"/>.</summary>
    void Serialize<T>(IBufferWriter<byte> output, T? value);

    /// <summary>Deserializes a value from <paramref name="buffer"/>.</summary>
    T? Deserialize<T>(ReadOnlyMemory<byte> buffer);
}