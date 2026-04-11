using System.Runtime.InteropServices;
using Quark.Serialization.Abstractions;

namespace Quark.Serialization.Codecs;

/// <summary>Codec for <see cref="float"/>.</summary>
public sealed class FloatCodec : IFieldCodec<float>
{
    /// <inheritdoc/>
    public void WriteField(CodecWriter writer, uint fieldId, Type expectedType, float value)
    {
        writer.WriteFieldHeader(fieldId, WireType.Fixed32);
        writer.WriteFixed32(BitConverter.SingleToUInt32Bits(value));
    }

    /// <inheritdoc/>
    public float ReadValue(CodecReader reader, Field field) =>
        BitConverter.UInt32BitsToSingle(reader.ReadFixed32());
}

/// <summary>Codec for <see cref="double"/>.</summary>
public sealed class DoubleCodec : IFieldCodec<double>
{
    /// <inheritdoc/>
    public void WriteField(CodecWriter writer, uint fieldId, Type expectedType, double value)
    {
        writer.WriteFieldHeader(fieldId, WireType.Fixed64);
        writer.WriteFixed64(BitConverter.DoubleToUInt64Bits(value));
    }

    /// <inheritdoc/>
    public double ReadValue(CodecReader reader, Field field) =>
        BitConverter.UInt64BitsToDouble(reader.ReadFixed64());
}

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
