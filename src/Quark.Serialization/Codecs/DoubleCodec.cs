using Quark.Serialization.Abstractions.Abstractions;
using Quark.Serialization.Abstractions.Buffers;

namespace Quark.Serialization.Codecs;

/// <summary>Codec for <see cref="double" />.</summary>
public sealed class DoubleCodec : IFieldCodec<double>
{
    /// <inheritdoc />
    public void WriteField(CodecWriter writer, uint fieldId, Type expectedType, double value)
    {
        writer.WriteFieldHeader(fieldId, WireType.Fixed64);
        writer.WriteFixed64(BitConverter.DoubleToUInt64Bits(value));
    }

    /// <inheritdoc />
    public double ReadValue(CodecReader reader, Field field)
    {
        return BitConverter.UInt64BitsToDouble(reader.ReadFixed64());
    }
}
