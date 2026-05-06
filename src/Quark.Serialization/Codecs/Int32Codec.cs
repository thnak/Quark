using Quark.Serialization.Abstractions;
using Quark.Serialization.Abstractions.Abstractions;
using Quark.Serialization.Abstractions.Buffers;

namespace Quark.Serialization.Codecs;

/// <summary>Codec for <see cref="int"/>.</summary>
public sealed class Int32Codec : IFieldCodec<int>
{
    /// <inheritdoc/>
    public void WriteField(CodecWriter writer, uint fieldId, Type expectedType, int value)
    {
        writer.WriteFieldHeader(fieldId, WireType.VarInt);
        writer.WriteInt32(value);
    }

    /// <inheritdoc/>
    public int ReadValue(CodecReader reader, Field field) =>
        reader.ReadInt32();
}