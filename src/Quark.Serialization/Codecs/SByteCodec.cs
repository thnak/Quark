using Quark.Serialization.Abstractions;

namespace Quark.Serialization.Codecs;

/// <summary>Codec for <see cref="sbyte"/>.</summary>
public sealed class SByteCodec : IFieldCodec<sbyte>
{
    /// <inheritdoc/>
    public void WriteField(CodecWriter writer, uint fieldId, Type expectedType, sbyte value)
    {
        writer.WriteFieldHeader(fieldId, WireType.VarInt);
        writer.WriteInt32(value);
    }

    /// <inheritdoc/>
    public sbyte ReadValue(CodecReader reader, Field field) =>
        (sbyte)reader.ReadInt32();
}