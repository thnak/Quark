using Quark.Serialization.Abstractions.Abstractions;
using Quark.Serialization.Abstractions.Buffers;

namespace Quark.Serialization.Codecs;

/// <summary>Codec for <see cref="DateTime" /> (UTC ticks, fixed-64).</summary>
public sealed class DateTimeCodec : IFieldCodec<DateTime>
{
    /// <inheritdoc />
    public void WriteField(CodecWriter writer, uint fieldId, Type expectedType, DateTime value)
    {
        writer.WriteFieldHeader(fieldId, WireType.Fixed64);
        writer.WriteFixed64((ulong)value.ToBinary());
    }

    /// <inheritdoc />
    public DateTime ReadValue(CodecReader reader, Field field)
    {
        return DateTime.FromBinary((long)reader.ReadFixed64());
    }
}
