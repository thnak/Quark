using Quark.Serialization.Abstractions;

namespace Quark.Serialization.Codecs;

/// <summary>Codec for <c>byte[]</c> (null-safe).</summary>
public sealed class ByteArrayCodec : IFieldCodec<byte[]?>
{
    /// <inheritdoc/>
    public void WriteField(CodecWriter writer, uint fieldId, Type expectedType, byte[]? value)
    {
        if (value is null)
        {
            writer.WriteFieldHeader(fieldId, WireType.Extended);
            writer.WriteByte((byte)ExtendedWireType.Null);
            return;
        }
        writer.WriteFieldHeader(fieldId, WireType.LengthPrefixed);
        writer.WriteBytes(value);
    }

    /// <inheritdoc/>
    public byte[]? ReadValue(CodecReader reader, Field field)
    {
        if (field.WireType == WireType.Extended && field.ExtendedWireType == ExtendedWireType.Null)
            return null;
        return reader.ReadBytes();
    }
}
