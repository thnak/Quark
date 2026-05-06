using Quark.Serialization.Abstractions;
using Quark.Serialization.Abstractions.Abstractions;
using Quark.Serialization.Abstractions.Buffers;

namespace Quark.Serialization.Codecs;

/// <summary>Codec for <see cref="uint"/>.</summary>
public sealed class UInt32Codec : IFieldCodec<uint>
{
    /// <inheritdoc/>
    public void WriteField(CodecWriter writer, uint fieldId, Type expectedType, uint value)
    {
        writer.WriteFieldHeader(fieldId, WireType.VarInt);
        writer.WriteVarUInt32(value);
    }

    /// <inheritdoc/>
    public uint ReadValue(CodecReader reader, Field field) =>
        reader.ReadVarUInt32();
}