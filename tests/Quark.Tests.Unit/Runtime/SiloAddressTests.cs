using Quark.Runtime;
using Xunit;

namespace Quark.Tests.Unit.Runtime;

public sealed class SiloAddressTests
{
    [Fact]
    public void Loopback_ReturnsLoopbackAddress()
    {
        var addr = SiloAddress.Loopback(11111);
        Assert.Equal("127.0.0.1", addr.Host);
        Assert.Equal(11111, addr.Port);
        Assert.Equal(0, addr.Generation);
    }

    [Fact]
    public void ToString_And_Parse_RoundTrip()
    {
        var addr = new SiloAddress("myhost", 12345, 7);
        string str = addr.ToString();
        var parsed = SiloAddress.Parse(str);
        Assert.Equal(addr, parsed);
    }

    [Fact]
    public void Equality_SameValues_AreEqual()
    {
        var a = new SiloAddress("host", 100, 1);
        var b = new SiloAddress("host", 100, 1);
        Assert.Equal(a, b);
        Assert.True(a == b);
        Assert.False(a != b);
    }

    [Fact]
    public void Equality_DifferentGeneration_NotEqual()
    {
        var a = new SiloAddress("host", 100, 0);
        var b = new SiloAddress("host", 100, 1);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void Parse_InvalidFormat_Throws()
    {
        Assert.Throws<FormatException>(() => SiloAddress.Parse("notvalid"));
    }
}