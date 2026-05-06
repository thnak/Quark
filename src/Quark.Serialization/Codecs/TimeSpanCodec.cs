using Quark.Serialization.Abstractions;
using Quark.Serialization.Abstractions.Abstractions;
using Quark.Serialization.Abstractions.Buffers;

namespace Quark.Serialization.Codecs;

/// <summary>Codec for <see cref="TimeSpan"/> (ticks, ZigZag-encoded).</summary>
public sealed class TimeSpanCodec : IFieldCodec<TimeSpan>
{
    /// <inheritdoc/>
    public void WriteField(CodecWriter writer, uint fieldId, Type expectedType, TimeSpan value)
    {
        writer.WriteFieldHeader(fieldId, WireType.VarInt);
        writer.WriteInt64(value.Ticks);
    }

    /// <inheritdoc/>
    public TimeSpan ReadValue(CodecReader reader, Field field) =>
        TimeSpan.FromTicks(reader.ReadInt64());
}