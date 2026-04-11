using System.Buffers;

namespace Quark.Serialization.Abstractions;

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

/// <summary>
/// Deep-copy interface — returns a detached copy of the object graph.
/// </summary>
public interface IDeepCopyable
{
    /// <summary>Returns a deep copy of <paramref name="value"/>.</summary>
    T DeepCopy<T>(T value);
}
