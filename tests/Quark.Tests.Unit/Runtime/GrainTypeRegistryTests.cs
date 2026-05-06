using Quark.Core.Abstractions;
using Quark.Runtime;
using Xunit;

namespace Quark.Tests.Unit.Runtime;

public sealed class GrainTypeRegistryTests
{
    [Fact]
    public void Register_AndLookup_Succeeds()
    {
        var registry = new GrainTypeRegistry();
        var grainType = new GrainType("MyGrain");
        registry.Register(grainType, typeof(string));

        bool found = registry.TryGetGrainClass(grainType, out Type? result);

        Assert.True(found);
        Assert.Equal(typeof(string), result);
    }

    [Fact]
    public void TryGetGrainClass_MissingType_ReturnsFalse()
    {
        var registry = new GrainTypeRegistry();
        bool found = registry.TryGetGrainClass(new GrainType("Missing"), out _);
        Assert.False(found);
    }

    [Fact]
    public void GetAll_ReturnsAllRegistrations()
    {
        var registry = new GrainTypeRegistry();
        registry.Register(new GrainType("A"), typeof(string));
        registry.Register(new GrainType("B"), typeof(int));

        var all = registry.GetAll().ToList();
        Assert.Equal(2, all.Count);
    }
}