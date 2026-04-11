using Quark.Serialization.Abstractions;

namespace Quark.Serialization.Codecs;

/// <summary>Codec for <see cref="string"/> (UTF-8, null-safe).</summary>
public sealed class StringCodec : IFieldCodec<string?>
{
    /// <inheritdoc/>
    public void WriteField(CodecWriter writer, uint fieldId, Type expectedType, string? value)
    {
        if (value is null)
        {
            // Write an Extended null field header.
            writer.WriteFieldHeader(fieldId, WireType.Extended);
            writer.WriteByte((byte)ExtendedWireType.Null);
            return;
        }
        writer.WriteFieldHeader(fieldId, WireType.LengthPrefixed);
        writer.WriteString(value);
    }

    /// <inheritdoc/>
    public string? ReadValue(CodecReader reader, Field field)
    {
        if (field.WireType == WireType.Extended && field.ExtendedWireType == ExtendedWireType.Null)
            return null;
        return reader.ReadString();
    }
}

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
