using Quark.Serialization.Abstractions.Abstractions;
using Quark.Serialization.Abstractions.Buffers;

namespace Quark.Serialization.Codecs;

/// <summary>Codec for <see cref="long" />.</summary>
public sealed class Int64Codec : IFieldCodec<long>
{
    /// <inheritdoc />
    public void WriteField(CodecWriter writer, uint fieldId, Type expectedType, long value)
    {
        writer.WriteFieldHeader(fieldId, WireType.VarInt);
        writer.WriteInt64(value);
    }

    /// <inheritdoc />
    public long ReadValue(CodecReader reader, Field field)
    {
        return reader.ReadInt64();
    }
}
