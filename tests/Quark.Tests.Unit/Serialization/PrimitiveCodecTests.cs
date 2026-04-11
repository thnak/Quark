using System.Buffers;
using Microsoft.Extensions.DependencyInjection;
using Quark.Serialization;
using Quark.Serialization.Abstractions;
using Xunit;

namespace Quark.Tests.Unit.Serialization;

/// <summary>
/// Round-trip tests for all built-in primitive codecs.
/// </summary>
public sealed class PrimitiveCodecTests
{
    private readonly QuarkSerializer _serializer;

    public PrimitiveCodecTests()
    {
        ServiceCollection services = new();
        services.AddQuarkSerialization();
        ServiceProvider sp = services.BuildServiceProvider();
        _serializer = sp.GetRequiredService<QuarkSerializer>();
    }

    private T RoundTrip<T>(T value)
    {
        byte[] bytes = _serializer.SerializeToArray(value);
        return _serializer.Deserialize<T>(bytes)!;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Bool_RoundTrip(bool value) => Assert.Equal(value, RoundTrip(value));

    [Theory]
    [InlineData((byte)0)]
    [InlineData((byte)1)]
    [InlineData(byte.MaxValue)]
    public void Byte_RoundTrip(byte value) => Assert.Equal(value, RoundTrip(value));

    [Theory]
    [InlineData((sbyte)-1)]
    [InlineData((sbyte)0)]
    [InlineData(sbyte.MaxValue)]
    [InlineData(sbyte.MinValue)]
    public void SByte_RoundTrip(sbyte value) => Assert.Equal(value, RoundTrip(value));

    [Theory]
    [InlineData((short)-32768)]
    [InlineData((short)0)]
    [InlineData(short.MaxValue)]
    public void Int16_RoundTrip(short value) => Assert.Equal(value, RoundTrip(value));

    [Theory]
    [InlineData((ushort)0)]
    [InlineData(ushort.MaxValue)]
    public void UInt16_RoundTrip(ushort value) => Assert.Equal(value, RoundTrip(value));

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    [InlineData(int.MinValue)]
    [InlineData(127)]
    [InlineData(128)]
    public void Int32_RoundTrip(int value) => Assert.Equal(value, RoundTrip(value));

    [Theory]
    [InlineData(0u)]
    [InlineData(1u)]
    [InlineData(uint.MaxValue)]
    public void UInt32_RoundTrip(uint value) => Assert.Equal(value, RoundTrip(value));

    [Theory]
    [InlineData(0L)]
    [InlineData(-1L)]
    [InlineData(long.MaxValue)]
    [InlineData(long.MinValue)]
    public void Int64_RoundTrip(long value) => Assert.Equal(value, RoundTrip(value));

    [Theory]
    [InlineData(0uL)]
    [InlineData(ulong.MaxValue)]
    public void UInt64_RoundTrip(ulong value) => Assert.Equal(value, RoundTrip(value));

    [Theory]
    [InlineData(0.0f)]
    [InlineData(1.5f)]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.MaxValue)]
    [InlineData(float.MinValue)]
    public void Float_RoundTrip(float value) => Assert.Equal(value, RoundTrip(value));

    [Theory]
    [InlineData(0.0)]
    [InlineData(1.23456789)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.MaxValue)]
    public void Double_RoundTrip(double value) => Assert.Equal(value, RoundTrip(value));

    [Fact]
    public void Decimal_RoundTrip()
    {
        Assert.Equal(0m, RoundTrip(0m));
        Assert.Equal(123.456m, RoundTrip(123.456m));
        Assert.Equal(decimal.MaxValue, RoundTrip(decimal.MaxValue));
        Assert.Equal(decimal.MinValue, RoundTrip(decimal.MinValue));
    }

    [Theory]
    [InlineData("")]
    [InlineData("hello")]
    [InlineData("héllo wörld 🌍")]
    public void String_RoundTrip(string value) => Assert.Equal(value, RoundTrip(value));

    [Fact]
    public void String_Null_RoundTrip()
    {
        // null string should survive round-trip via nullable codec
        IFieldCodec<string?> codec = new ServiceCollection()
            .AddQuarkSerialization()
            .BuildServiceProvider()
            .GetRequiredService<IFieldCodec<string?>>();

        ArrayBufferWriter<byte> buf = new();
        CodecWriter writer = new(buf);
        codec.WriteField(writer, 0, typeof(string), null);

        CodecReader reader = new(buf.WrittenMemory);
        Field field = reader.ReadFieldHeader();
        string? result = codec.ReadValue(reader, field);
        Assert.Null(result);
    }

    [Theory]
    [InlineData('A')]
    [InlineData('z')]
    [InlineData('€')]
    public void Char_RoundTrip(char value) => Assert.Equal(value, RoundTrip(value));

    [Fact]
    public void Guid_RoundTrip()
    {
        Guid original = Guid.NewGuid();
        Assert.Equal(original, RoundTrip(original));
        Assert.Equal(Guid.Empty, RoundTrip(Guid.Empty));
    }

    [Fact]
    public void DateTime_RoundTrip()
    {
        DateTime now = DateTime.UtcNow;
        DateTime result = RoundTrip(now);
        Assert.Equal(now, result);
    }

    [Fact]
    public void DateTimeOffset_RoundTrip()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        DateTimeOffset result = RoundTrip(now);
        Assert.Equal(now, result);
    }

    [Fact]
    public void TimeSpan_RoundTrip()
    {
        TimeSpan ts = TimeSpan.FromSeconds(42.5);
        Assert.Equal(ts, RoundTrip(ts));
        Assert.Equal(TimeSpan.Zero, RoundTrip(TimeSpan.Zero));
        Assert.Equal(TimeSpan.MaxValue, RoundTrip(TimeSpan.MaxValue));
    }

    [Fact]
    public void ByteArray_RoundTrip()
    {
        IFieldCodec<byte[]?> codec = new ServiceCollection()
            .AddQuarkSerialization()
            .BuildServiceProvider()
            .GetRequiredService<IFieldCodec<byte[]?>>();

        byte[] original = [1, 2, 3, 4, 5];
        ArrayBufferWriter<byte> buf = new();
        CodecWriter writer = new(buf);
        codec.WriteField(writer, 0, typeof(byte[]), original);

        CodecReader reader = new(buf.WrittenMemory);
        Field field = reader.ReadFieldHeader();
        byte[]? result = codec.ReadValue(reader, field);
        Assert.Equal(original, result);
    }
}
