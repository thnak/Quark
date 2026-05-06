using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Text;

namespace Quark.Serialization.Abstractions.Buffers;

/// <summary>
///     A forward-only reader for Quark's binary encoding, backed by a <see cref="ReadOnlyMemory{T}" />.
/// </summary>
public sealed class CodecReader
{
    private readonly ReadOnlyMemory<byte> _buffer;

    /// <summary>Initialises a new <see cref="CodecReader" /> over <paramref name="buffer" />.</summary>
    public CodecReader(ReadOnlyMemory<byte> buffer)
    {
        _buffer = buffer;
        Position = 0;
    }

    /// <summary>Gets the current read position.</summary>
    public int Position { get; private set; }

    /// <summary>Whether there is any data remaining to be read.</summary>
    public bool HasMore => Position < _buffer.Length;

    /// <summary>Reads a single byte.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public byte ReadByte()
    {
        if (Position >= _buffer.Length)
        {
            throw new EndOfStreamException("Unexpected end of serialized data.");
        }

        return _buffer.Span[Position++];
    }

    /// <summary>Reads exactly <paramref name="count" /> bytes verbatim.</summary>
    public ReadOnlySpan<byte> ReadRaw(int count)
    {
        if (Position + count > _buffer.Length)
        {
            throw new EndOfStreamException("Unexpected end of serialized data.");
        }

        ReadOnlySpan<byte> slice = _buffer.Span.Slice(Position, count);
        Position += count;
        return slice;
    }

    /// <summary>Reads an unsigned 32-bit LEB128-encoded integer.</summary>
    public uint ReadVarUInt32()
    {
        uint result = 0;
        int shift = 0;
        while (true)
        {
            byte b = ReadByte();
            result |= (uint)(b & 0x7F) << shift;
            if ((b & 0x80) == 0)
            {
                return result;
            }

            shift += 7;
            if (shift >= 35)
            {
                throw new OverflowException("VarUInt32 is too large.");
            }
        }
    }

    /// <summary>Reads an unsigned 64-bit LEB128-encoded integer.</summary>
    public ulong ReadVarUInt64()
    {
        ulong result = 0;
        int shift = 0;
        while (true)
        {
            byte b = ReadByte();
            result |= (ulong)(b & 0x7F) << shift;
            if ((b & 0x80) == 0)
            {
                return result;
            }

            shift += 7;
            if (shift >= 70)
            {
                throw new OverflowException("VarUInt64 is too large.");
            }
        }
    }

    /// <summary>Reads a ZigZag + LEB128 signed 32-bit integer.</summary>
    public int ReadInt32()
    {
        uint n = ReadVarUInt32();
        return (int)((n >> 1) ^ -(n & 1));
    }

    /// <summary>Reads a ZigZag + LEB128 signed 64-bit integer.</summary>
    public long ReadInt64()
    {
        ulong n = ReadVarUInt64();
        return (long)(n >> 1) ^ -(long)(n & 1);
    }

    /// <summary>Reads a 32-bit fixed little-endian unsigned integer.</summary>
    public uint ReadFixed32()
    {
        ReadOnlySpan<byte> span = ReadRaw(4);
        return BinaryPrimitives.ReadUInt32LittleEndian(span);
    }

    /// <summary>Reads a 64-bit fixed little-endian unsigned integer.</summary>
    public ulong ReadFixed64()
    {
        ReadOnlySpan<byte> span = ReadRaw(8);
        return BinaryPrimitives.ReadUInt64LittleEndian(span);
    }

    /// <summary>Reads a length-prefixed UTF-8 string (empty returns <see cref="string.Empty" />).</summary>
    public string ReadString()
    {
        uint byteCount = ReadVarUInt32();
        if (byteCount == 0)
        {
            return string.Empty;
        }

        ReadOnlySpan<byte> bytes = ReadRaw((int)byteCount);
        return Encoding.UTF8.GetString(bytes);
    }

    /// <summary>Reads a length-prefixed byte array.</summary>
    public byte[] ReadBytes()
    {
        uint length = ReadVarUInt32();
        if (length == 0)
        {
            return Array.Empty<byte>();
        }

        return ReadRaw((int)length).ToArray();
    }

    /// <summary>Reads a field header and returns the decoded <see cref="Field" />.</summary>
    public Field ReadFieldHeader()
    {
        uint tag = ReadVarUInt32();
        uint fieldId = tag >> 3;
        var wireType = (WireType)(tag & 0x07);

        ExtendedWireType extendedWireType = default;
        if (wireType == WireType.Extended)
        {
            extendedWireType = (ExtendedWireType)ReadByte();
        }

        return new Field
        {
            FieldId = fieldId,
            WireType = wireType,
            ExtendedWireType = extendedWireType
        };
    }
}
