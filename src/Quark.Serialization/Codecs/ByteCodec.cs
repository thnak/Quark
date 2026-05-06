using Quark.Serialization.Abstractions.Abstractions;
using Quark.Serialization.Abstractions.Buffers;

namespace Quark.Serialization.Codecs;

/// <summary>Codec for <see cref="byte" />.</summary>
public sealed class ByteCodec : IFieldCodec<byte>
{
    /// <inheritdoc />
    public void WriteField(CodecWriter writer, uint fieldId, Type expectedType, byte value)
    {
        writer.WriteFieldHeader(fieldId, WireType.VarInt);
        writer.WriteVarUInt32(value);
    }

    /// <inheritdoc />
    public byte ReadValue(CodecReader reader, Field field)
    {
        return (byte)reader.ReadVarUInt32();
    }
}
