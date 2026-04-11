using Quark.Serialization.Abstractions;

namespace Quark.Serialization.Codecs;

/// <summary>Codec for <see cref="bool"/>.</summary>
public sealed class BoolCodec : IFieldCodec<bool>
{
    /// <inheritdoc/>
    public void WriteField(CodecWriter writer, uint fieldId, Type expectedType, bool value)
    {
        writer.WriteFieldHeader(fieldId, WireType.VarInt);
        writer.WriteVarUInt32(value ? 1u : 0u);
    }

    /// <inheritdoc/>
    public bool ReadValue(CodecReader reader, Field field) =>
        reader.ReadVarUInt32() != 0;
}

/// <summary>Codec for <see cref="byte"/>.</summary>
public sealed class ByteCodec : IFieldCodec<byte>
{
    /// <inheritdoc/>
    public void WriteField(CodecWriter writer, uint fieldId, Type expectedType, byte value)
    {
        writer.WriteFieldHeader(fieldId, WireType.VarInt);
        writer.WriteVarUInt32(value);
    }

    /// <inheritdoc/>
    public byte ReadValue(CodecReader reader, Field field) =>
        (byte)reader.ReadVarUInt32();
}

/// <summary>Codec for <see cref="sbyte"/>.</summary>
public sealed class SByteCodec : IFieldCodec<sbyte>
{
    /// <inheritdoc/>
    public void WriteField(CodecWriter writer, uint fieldId, Type expectedType, sbyte value)
    {
        writer.WriteFieldHeader(fieldId, WireType.VarInt);
        writer.WriteInt32(value);
    }

    /// <inheritdoc/>
    public sbyte ReadValue(CodecReader reader, Field field) =>
        (sbyte)reader.ReadInt32();
}

/// <summary>Codec for <see cref="short"/>.</summary>
public sealed class Int16Codec : IFieldCodec<short>
{
    /// <inheritdoc/>
    public void WriteField(CodecWriter writer, uint fieldId, Type expectedType, short value)
    {
        writer.WriteFieldHeader(fieldId, WireType.VarInt);
        writer.WriteInt32(value);
    }

    /// <inheritdoc/>
    public short ReadValue(CodecReader reader, Field field) =>
        (short)reader.ReadInt32();
}

/// <summary>Codec for <see cref="ushort"/>.</summary>
public sealed class UInt16Codec : IFieldCodec<ushort>
{
    /// <inheritdoc/>
    public void WriteField(CodecWriter writer, uint fieldId, Type expectedType, ushort value)
    {
        writer.WriteFieldHeader(fieldId, WireType.VarInt);
        writer.WriteVarUInt32(value);
    }

    /// <inheritdoc/>
    public ushort ReadValue(CodecReader reader, Field field) =>
        (ushort)reader.ReadVarUInt32();
}

/// <summary>Codec for <see cref="int"/>.</summary>
public sealed class Int32Codec : IFieldCodec<int>
{
    /// <inheritdoc/>
    public void WriteField(CodecWriter writer, uint fieldId, Type expectedType, int value)
    {
        writer.WriteFieldHeader(fieldId, WireType.VarInt);
        writer.WriteInt32(value);
    }

    /// <inheritdoc/>
    public int ReadValue(CodecReader reader, Field field) =>
        reader.ReadInt32();
}

/// <summary>Codec for <see cref="uint"/>.</summary>
public sealed class UInt32Codec : IFieldCodec<uint>
{
    /// <inheritdoc/>
    public void WriteField(CodecWriter writer, uint fieldId, Type expectedType, uint value)
    {
        writer.WriteFieldHeader(fieldId, WireType.VarInt);
        writer.WriteVarUInt32(value);
    }

    /// <inheritdoc/>
    public uint ReadValue(CodecReader reader, Field field) =>
        reader.ReadVarUInt32();
}

/// <summary>Codec for <see cref="long"/>.</summary>
public sealed class Int64Codec : IFieldCodec<long>
{
    /// <inheritdoc/>
    public void WriteField(CodecWriter writer, uint fieldId, Type expectedType, long value)
    {
        writer.WriteFieldHeader(fieldId, WireType.VarInt);
        writer.WriteInt64(value);
    }

    /// <inheritdoc/>
    public long ReadValue(CodecReader reader, Field field) =>
        reader.ReadInt64();
}

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
