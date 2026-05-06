using Quark.Serialization.Abstractions;
using Quark.Serialization.Abstractions.Abstractions;
using Quark.Serialization.Abstractions.Buffers;

namespace Quark.Serialization.Codecs;

/// <summary>Codec for <see cref="ushort"/>.</summary>
public sealed class UInt16Codec : IFieldCodec<ushort>
{
    /// <inheritdoc/>
    public void WriteField(CodecWriter writer, uint fieldId, Type expectedType, ushort value)
    {
        writer.WriteFieldHeader(fieldId, WireType.VarInt);
        writer.WriteVarUInt32(value);
    }

    /// <inheritdoc/>
    public ushort ReadValue(CodecReader reader, Field field) =>
        (ushort)reader.ReadVarUInt32();
}