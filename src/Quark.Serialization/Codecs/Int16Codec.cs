using Quark.Serialization.Abstractions;
using Quark.Serialization.Abstractions.Abstractions;
using Quark.Serialization.Abstractions.Buffers;

namespace Quark.Serialization.Codecs;

/// <summary>Codec for <see cref="short"/>.</summary>
public sealed class Int16Codec : IFieldCodec<short>
{
    /// <inheritdoc/>
    public void WriteField(CodecWriter writer, uint fieldId, Type expectedType, short value)
    {
        writer.WriteFieldHeader(fieldId, WireType.VarInt);
        writer.WriteInt32(value);
    }

    /// <inheritdoc/>
    public short ReadValue(CodecReader reader, Field field) =>
        (short)reader.ReadInt32();
}