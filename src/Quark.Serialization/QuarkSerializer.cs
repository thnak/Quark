using System.Buffers;
using Quark.Serialization.Abstractions;

namespace Quark.Serialization;

/// <summary>
/// Default top-level serializer: uses registered <see cref="IFieldCodec{T}"/>s to
/// serialize/deserialize full values.
/// </summary>
public sealed class QuarkSerializer : ISerializer
{
    private readonly ICodecProvider _codecs;

    /// <summary>Creates a <see cref="QuarkSerializer"/> backed by <paramref name="codecs"/>.</summary>
    public QuarkSerializer(ICodecProvider codecs)
    {
        _codecs = codecs;
    }

    /// <inheritdoc/>
    public void Serialize<T>(IBufferWriter<byte> output, T? value)
    {
        IFieldCodec<T> codec = _codecs.GetRequiredCodec<T>();
        CodecWriter writer = new(output);
        // Field id 0 is used for the root value of a top-level serialize call.
        codec.WriteField(writer, 0, typeof(T), value!);
    }

    /// <inheritdoc/>
    public T? Deserialize<T>(ReadOnlyMemory<byte> buffer)
    {
        IFieldCodec<T> codec = _codecs.GetRequiredCodec<T>();
        CodecReader reader = new(buffer);
        Field field = reader.ReadFieldHeader();
        return codec.ReadValue(reader, field);
    }

    /// <summary>Convenience helper: returns serialized bytes for <paramref name="value"/>.</summary>
    public byte[] SerializeToArray<T>(T? value)
    {
        ArrayBufferWriter<byte> buf = new();
        Serialize(buf, value);
        return buf.WrittenSpan.ToArray();
    }
}
