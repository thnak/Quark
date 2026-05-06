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