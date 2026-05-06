using Quark.Serialization.Abstractions;
using Quark.Serialization.Abstractions.Abstractions;
using Quark.Serialization.Abstractions.Buffers;

namespace Quark.Serialization.Codecs;

/// <summary>Codec for <see cref="bool"/>.</summary>
public sealed class BoolCodec : IFieldCodec<bool>
{
    /// <inheritdoc/>
    public void WriteField(CodecWriter writer, uint fieldId, Type expectedType, bool value)
    {
        writer.WriteFieldHeader(fieldId, WireType.VarInt);
        writer.WriteVarUInt32(value ? 1u : 0u);
    }

    /// <inheritdoc/>
    public bool ReadValue(CodecReader reader, Field field) =>
        reader.ReadVarUInt32() != 0;
}