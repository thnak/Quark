using Quark.Serialization.Abstractions.Abstractions;
using Quark.Serialization.Abstractions.Buffers;

namespace Quark.Serialization.Codecs;

/// <summary>Codec for <see cref="Guid" /> (16 bytes, standard format).</summary>
public sealed class GuidCodec : IFieldCodec<Guid>
{
    /// <inheritdoc />
    public void WriteField(CodecWriter writer, uint fieldId, Type expectedType, Guid value)
    {
        writer.WriteFieldHeader(fieldId, WireType.LengthPrefixed);
        writer.WriteVarUInt32(16u);
        Span<byte> bytes = stackalloc byte[16];
        value.TryWriteBytes(bytes);
        writer.WriteRaw(bytes);
    }

    /// <inheritdoc />
    public Guid ReadValue(CodecReader reader, Field field)
    {
        uint length = reader.ReadVarUInt32();
        if (length != 16)
        {
            throw new InvalidDataException($"Expected 16 bytes for Guid, got {length}.");
        }

        ReadOnlySpan<byte> bytes = reader.ReadRaw(16);
        return new Guid(bytes);
    }
}
