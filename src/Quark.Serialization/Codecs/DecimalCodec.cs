using Quark.Serialization.Abstractions;

namespace Quark.Serialization.Codecs;

/// <summary>Codec for <see cref="decimal"/> (encoded as 16 bytes, little-endian).</summary>
public sealed class DecimalCodec : IFieldCodec<decimal>
{
    /// <inheritdoc/>
    public void WriteField(CodecWriter writer, uint fieldId, Type expectedType, decimal value)
    {
        writer.WriteFieldHeader(fieldId, WireType.LengthPrefixed);
        Span<int> bits = stackalloc int[4];
        decimal.GetBits(value, bits);
        writer.WriteVarUInt32(16u);
        for (int i = 0; i < 4; i++)
            writer.WriteFixed32((uint)bits[i]);
    }

    /// <inheritdoc/>
    public decimal ReadValue(CodecReader reader, Field field)
    {
        uint length = reader.ReadVarUInt32();
        if (length != 16)
            throw new InvalidDataException($"Expected 16 bytes for decimal, got {length}.");
        Span<int> bits = stackalloc int[4];
        for (int i = 0; i < 4; i++)
            bits[i] = (int)reader.ReadFixed32();
        return new decimal(bits);
    }
}