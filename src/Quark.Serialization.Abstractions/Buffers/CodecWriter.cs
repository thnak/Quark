using System.Buffers;
using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Text;

namespace Quark.Serialization.Abstractions.Buffers;

/// <summary>
///     A forward-only writer for Quark's binary encoding, backed by any <see cref="IBufferWriter{T}" />.
/// </summary>
public sealed class CodecWriter
{
    private readonly IBufferWriter<byte> _output;

    /// <summary>Initialises a new <see cref="CodecWriter" /> backed by <paramref name="output" />.</summary>
    public CodecWriter(IBufferWriter<byte> output)
    {
        ArgumentNullException.ThrowIfNull(output);
        _output = output;
    }

    /// <summary>Writes a single byte.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void WriteByte(byte value)
    {
        Span<byte> span = _output.GetSpan(1);
        span[0] = value;
        _output.Advance(1);
    }

    /// <summary>Writes a span of bytes verbatim (no length prefix).</summary>
    public void WriteRaw(ReadOnlySpan<byte> bytes)
    {
        Span<byte> span = _output.GetSpan(bytes.Length);
        bytes.CopyTo(span);
        _output.Advance(bytes.Length);
    }

    /// <summary>Writes an unsigned 32-bit integer using LEB128 variable-length encoding.</summary>
    public void WriteVarUInt32(uint value)
    {
        while (value >= 0x80)
        {
            WriteByte((byte)((value & 0x7F) | 0x80));
            value >>= 7;
        }

        WriteByte((byte)value);
    }

    /// <summary>Writes an unsigned 64-bit integer using LEB128 variable-length encoding.</summary>
    public void WriteVarUInt64(ulong value)
    {
        while (value >= 0x80)
        {
            WriteByte((byte)((value & 0x7F) | 0x80));
            value >>= 7;
        }

        WriteByte((byte)value);
    }

    /// <summary>Writes a signed 32-bit integer using ZigZag + LEB128 encoding.</summary>
    public void WriteInt32(int value)
    {
        WriteVarUInt32((uint)((value << 1) ^ (value >> 31)));
    }

    /// <summary>Writes a signed 64-bit integer using ZigZag + LEB128 encoding.</summary>
    public void WriteInt64(long value)
    {
        WriteVarUInt64((ulong)((value << 1) ^ (value >> 63)));
    }

    /// <summary>Writes a 32-bit fixed-width little-endian integer.</summary>
    public void WriteFixed32(uint value)
    {
        Span<byte> span = _output.GetSpan(4);
        BinaryPrimitives.WriteUInt32LittleEndian(span, value);
        _output.Advance(4);
    }

    /// <summary>Writes a 64-bit fixed-width little-endian integer.</summary>
    public void WriteFixed64(ulong value)
    {
        Span<byte> span = _output.GetSpan(8);
        BinaryPrimitives.WriteUInt64LittleEndian(span, value);
        _output.Advance(8);
    }

    /// <summary>Writes a length-prefixed UTF-8 string (null treated as empty).</summary>
    public void WriteString(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            WriteVarUInt32(0);
            return;
        }

        int byteCount = Encoding.UTF8.GetByteCount(value);
        WriteVarUInt32((uint)byteCount);
        Span<byte> span = _output.GetSpan(byteCount);
        Encoding.UTF8.GetBytes(value, span);
        _output.Advance(byteCount);
    }

    /// <summary>Writes a length-prefixed byte array (null treated as zero length).</summary>
    public void WriteBytes(ReadOnlySpan<byte> value)
    {
        WriteVarUInt32((uint)value.Length);
        WriteRaw(value);
    }

    /// <summary>Writes a field header — packed field id and wire type.</summary>
    public void WriteFieldHeader(uint fieldId, WireType wireType)
    {
        WriteVarUInt32((fieldId << 3) | (uint)wireType);
    }
}
