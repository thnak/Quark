using Quark.Core.Abstractions.Identity;
using Xunit;

namespace Quark.Tests.Unit.Identity;

public sealed class GrainIdTests
{
    [Fact]
    public void GrainId_EqualityByTypeAndKey()
    {
        GrainId a = new(new GrainType("counter"), "key-1");
        GrainId b = new(new GrainType("counter"), "key-1");
        GrainId c = new(new GrainType("counter"), "key-2");
        GrainId d = new(new GrainType("timer"), "key-1");

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
        Assert.NotEqual(a, d);
    }

    [Fact]
    public void GrainId_GetHashCode_ConsistentWithEquality()
    {
        GrainId a = new(new GrainType("counter"), "key-1");
        GrainId b = new(new GrainType("counter"), "key-1");
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void GrainId_Create_FromGuid()
    {
        Guid key = Guid.NewGuid();
        GrainId id = GrainId.Create(new GrainType("grain"), key);
        Assert.Equal(key.ToString("N"), id.Key);
    }

    [Fact]
    public void GrainId_Create_FromLong()
    {
        GrainId id = GrainId.Create(new GrainType("grain"), 42L);
        Assert.Equal("42", id.Key);
    }

    [Fact]
    public void GrainId_ToString_ContainsTypeAndKey()
    {
        GrainId id = new(new GrainType("myGrain"), "abc");
        Assert.Contains("myGrain", id.ToString());
        Assert.Contains("abc", id.ToString());
    }

    [Fact]
    public void GrainId_CanBeUsedAsDictionaryKey()
    {
        Dictionary<GrainId, string> dict = new();
        GrainId id = new(new GrainType("counter"), "k");
        dict[id] = "value";
        Assert.True(dict.ContainsKey(new GrainId(new GrainType("counter"), "k")));
    }

    [Fact]
    public void GrainType_Equality()
    {
        GrainType a = new("grain");
        GrainType b = new("grain");
        GrainType c = new("other");
        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
    }

    [Fact]
    public void GrainType_ThrowsOnNullOrEmpty()
    {
        Assert.Throws<ArgumentException>(() => new GrainType(string.Empty));
        Assert.Throws<ArgumentNullException>(() => new GrainType(null!));
    }
}
