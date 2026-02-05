// Copyright (c) Quark Framework. All rights reserved.

namespace Quark.Abstractions.Converters;

/// <summary>
/// Built-in converter for string types.
/// </summary>
public sealed class StringConverter : QuarkBinaryConverter<string>
{
    /// <inheritdoc/>
    public override void Write(BinaryWriter writer, string value)
    {
        writer.Write(value ?? string.Empty);
    }

    /// <inheritdoc/>
    public override string Read(BinaryReader reader)
    {
        return reader.ReadString();
    }
}

/// <summary>
/// Built-in converter for int types.
/// </summary>
public sealed class Int32Converter : QuarkBinaryConverter<int>
{
    /// <inheritdoc/>
    public override void Write(BinaryWriter writer, int value)
    {
        writer.Write(value);
    }

    /// <inheritdoc/>
    public override int Read(BinaryReader reader)
    {
        return reader.ReadInt32();
    }
}

/// <summary>
/// Built-in converter for long types.
/// </summary>
public sealed class Int64Converter : QuarkBinaryConverter<long>
{
    /// <inheritdoc/>
    public override void Write(BinaryWriter writer, long value)
    {
        writer.Write(value);
    }

    /// <inheritdoc/>
    public override long Read(BinaryReader reader)
    {
        return reader.ReadInt64();
    }
}

/// <summary>
/// Built-in converter for bool types.
/// </summary>
public sealed class BooleanConverter : QuarkBinaryConverter<bool>
{
    /// <inheritdoc/>
    public override void Write(BinaryWriter writer, bool value)
    {
        writer.Write(value);
    }

    /// <inheritdoc/>
    public override bool Read(BinaryReader reader)
    {
        return reader.ReadBoolean();
    }
}

/// <summary>
/// Built-in converter for double types.
/// </summary>
public sealed class DoubleConverter : QuarkBinaryConverter<double>
{
    /// <inheritdoc/>
    public override void Write(BinaryWriter writer, double value)
    {
        writer.Write(value);
    }

    /// <inheritdoc/>
    public override double Read(BinaryReader reader)
    {
        return reader.ReadDouble();
    }
}

/// <summary>
/// Built-in converter for Guid types.
/// </summary>
public sealed class GuidConverter : QuarkBinaryConverter<Guid>
{
    /// <inheritdoc/>
    public override void Write(BinaryWriter writer, Guid value)
    {
        writer.Write(value.ToByteArray());
    }

    /// <inheritdoc/>
    public override Guid Read(BinaryReader reader)
    {
        var bytes = reader.ReadBytes(16);
        return new Guid(bytes);
    }
}

/// <summary>
/// Built-in converter for DateTime types (UTC).
/// </summary>
public sealed class DateTimeConverter : QuarkBinaryConverter<DateTime>
{
    /// <inheritdoc/>
    public override void Write(BinaryWriter writer, DateTime value)
    {
        writer.Write(value.ToBinary());
    }

    /// <inheritdoc/>
    public override DateTime Read(BinaryReader reader)
    {
        return DateTime.FromBinary(reader.ReadInt64());
    }
}

/// <summary>
/// Built-in converter for byte array types.
/// </summary>
public sealed class ByteArrayConverter : QuarkBinaryConverter<byte[]>
{
    /// <inheritdoc/>
    public override void Write(BinaryWriter writer, byte[] value)
    {
        if (value == null)
        {
            writer.Write(-1);
            return;
        }
        
        writer.Write(value.Length);
        writer.Write(value);
    }

    /// <inheritdoc/>
    public override byte[] Read(BinaryReader reader)
    {
        var length = reader.ReadInt32();
        if (length == -1)
        {
            return Array.Empty<byte>();
        }
        
        return reader.ReadBytes(length);
    }
}
