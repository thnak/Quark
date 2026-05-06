using Quark.Serialization.Abstractions;
using Quark.Serialization.Abstractions.Abstractions;
using Quark.Serialization.Abstractions.Buffers;

namespace Quark.Serialization.Codecs;

/// <summary>Codec for <see cref="char"/>.</summary>
public sealed class CharCodec : IFieldCodec<char>
{
    /// <inheritdoc/>
    public void WriteField(CodecWriter writer, uint fieldId, Type expectedType, char value)
    {
        writer.WriteFieldHeader(fieldId, WireType.VarInt);
        writer.WriteVarUInt32(value);
    }

    /// <inheritdoc/>
    public char ReadValue(CodecReader reader, Field field) =>
        (char)reader.ReadVarUInt32();
}