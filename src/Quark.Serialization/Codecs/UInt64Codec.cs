using Quark.Serialization.Abstractions;
using Quark.Serialization.Abstractions.Abstractions;
using Quark.Serialization.Abstractions.Buffers;

namespace Quark.Serialization.Codecs;

/// <summary>Codec for <see cref="ulong"/>.</summary>
public sealed class UInt64Codec : IFieldCodec<ulong>
{
    /// <inheritdoc/>
    public void WriteField(CodecWriter writer, uint fieldId, Type expectedType, ulong value)
    {
        writer.WriteFieldHeader(fieldId, WireType.VarInt);
        writer.WriteVarUInt64(value);
    }

    /// <inheritdoc/>
    public ulong ReadValue(CodecReader reader, Field field) =>
        reader.ReadVarUInt64();
}