using System.Buffers;
using Quark.Serialization.Abstractions;
using Xunit;

namespace Quark.Tests.Unit.Serialization;

/// <summary>
/// Tests for the raw CodecWriter/CodecReader binary encoding.
/// </summary>
public sealed class BinaryEncodingTests
{
    private static (CodecWriter writer, ArrayBufferWriter<byte> buffer) CreateWriter()
    {
        ArrayBufferWriter<byte> buf = new();
        return (new CodecWriter(buf), buf);
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(1u)]
    [InlineData(127u)]
    [InlineData(128u)]
    [InlineData(255u)]
    [InlineData(uint.MaxValue)]
    public void VarUInt32_RoundTrip(uint value)
    {
        (CodecWriter writer, ArrayBufferWriter<byte> buf) = CreateWriter();
        writer.WriteVarUInt32(value);
        CodecReader reader = new(buf.WrittenMemory);
        Assert.Equal(value, reader.ReadVarUInt32());
    }

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    [InlineData(int.MinValue)]
    [InlineData(int.MaxValue)]
    [InlineData(long.MinValue)]
    [InlineData(long.MaxValue)]
    public void Int64_ZigZag_RoundTrip(long value)
    {
        (CodecWriter writer, ArrayBufferWriter<byte> buf) = CreateWriter();
        writer.WriteInt64(value);
        CodecReader reader = new(buf.WrittenMemory);
        Assert.Equal(value, reader.ReadInt64());
    }

    [Fact]
    public void Fixed32_RoundTrip()
    {
        (CodecWriter writer, ArrayBufferWriter<byte> buf) = CreateWriter();
        writer.WriteFixed32(0xDEADBEEFu);
        CodecReader reader = new(buf.WrittenMemory);
        Assert.Equal(0xDEADBEEFu, reader.ReadFixed32());
    }

    [Fact]
    public void Fixed64_RoundTrip()
    {
        (CodecWriter writer, ArrayBufferWriter<byte> buf) = CreateWriter();
        writer.WriteFixed64(0x0102030405060708uL);
        CodecReader reader = new(buf.WrittenMemory);
        Assert.Equal(0x0102030405060708uL, reader.ReadFixed64());
    }

    [Fact]
    public void String_LengthPrefixed_RoundTrip()
    {
        (CodecWriter writer, ArrayBufferWriter<byte> buf) = CreateWriter();
        writer.WriteString("Hello, Quark! 🚀");
        CodecReader reader = new(buf.WrittenMemory);
        Assert.Equal("Hello, Quark! 🚀", reader.ReadString());
    }

    [Fact]
    public void Bytes_LengthPrefixed_RoundTrip()
    {
        byte[] original = [0x01, 0x02, 0xAB, 0xCD, 0xFF];
        (CodecWriter writer, ArrayBufferWriter<byte> buf) = CreateWriter();
        writer.WriteBytes(original);
        CodecReader reader = new(buf.WrittenMemory);
        Assert.Equal(original, reader.ReadBytes());
    }

    [Fact]
    public void FieldHeader_RoundTrip()
    {
        (CodecWriter writer, ArrayBufferWriter<byte> buf) = CreateWriter();
        writer.WriteFieldHeader(3, WireType.LengthPrefixed);
        CodecReader reader = new(buf.WrittenMemory);
        Field field = reader.ReadFieldHeader();
        Assert.Equal(3u, field.FieldId);
        Assert.Equal(WireType.LengthPrefixed, field.WireType);
    }

    [Fact]
    public void ExtendedFieldHeader_ReadsExtendedWireType_AndAdvancesPastMarker()
    {
        (CodecWriter writer, ArrayBufferWriter<byte> buf) = CreateWriter();
        writer.WriteFieldHeader(7, WireType.Extended);
        writer.WriteByte((byte)ExtendedWireType.Null);
        writer.WriteByte(0x42);

        CodecReader reader = new(buf.WrittenMemory);
        Field field = reader.ReadFieldHeader();

        Assert.Equal(7u, field.FieldId);
        Assert.Equal(WireType.Extended, field.WireType);
        Assert.Equal(ExtendedWireType.Null, field.ExtendedWireType);
        Assert.Equal(0x42, reader.ReadByte());
    }
}
