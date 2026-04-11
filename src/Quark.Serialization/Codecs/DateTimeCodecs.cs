using Quark.Serialization.Abstractions;

namespace Quark.Serialization.Codecs;

/// <summary>Codec for <see cref="DateTime"/> (UTC ticks, fixed-64).</summary>
public sealed class DateTimeCodec : IFieldCodec<DateTime>
{
    /// <inheritdoc/>
    public void WriteField(CodecWriter writer, uint fieldId, Type expectedType, DateTime value)
    {
        writer.WriteFieldHeader(fieldId, WireType.Fixed64);
        writer.WriteFixed64((ulong)value.ToBinary());
    }

    /// <inheritdoc/>
    public DateTime ReadValue(CodecReader reader, Field field) =>
        DateTime.FromBinary((long)reader.ReadFixed64());
}

/// <summary>Codec for <see cref="DateTimeOffset"/>.</summary>
public sealed class DateTimeOffsetCodec : IFieldCodec<DateTimeOffset>
{
    /// <inheritdoc/>
    public void WriteField(CodecWriter writer, uint fieldId, Type expectedType, DateTimeOffset value)
    {
        writer.WriteFieldHeader(fieldId, WireType.LengthPrefixed);
        // Encode as: utc-ticks (8 bytes) + offset-minutes (4 bytes)
        writer.WriteVarUInt32(12u);
        writer.WriteFixed64((ulong)value.UtcTicks);
        writer.WriteFixed32((uint)(int)value.Offset.TotalMinutes);
    }

    /// <inheritdoc/>
    public DateTimeOffset ReadValue(CodecReader reader, Field field)
    {
        uint length = reader.ReadVarUInt32();
        if (length != 12)
            throw new InvalidDataException($"Expected 12 bytes for DateTimeOffset, got {length}.");
        long utcTicks = (long)reader.ReadFixed64();
        int offsetMinutes = (int)reader.ReadFixed32();
        return new DateTimeOffset(utcTicks, TimeSpan.FromMinutes(offsetMinutes));
    }
}

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
