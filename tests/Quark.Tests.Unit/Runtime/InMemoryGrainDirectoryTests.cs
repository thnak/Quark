using Quark.Core.Abstractions;
using Quark.Runtime;
using Xunit;

namespace Quark.Tests.Unit.Runtime;

public sealed class InMemoryGrainDirectoryTests
{
    [Fact]
    public void Register_NewGrain_Succeeds()
    {
        var dir = new InMemoryGrainDirectory();
        var id = new GrainId(new GrainType("Counter"), "key1");
        var addr = SiloAddress.Loopback(11111);

        bool ok = dir.TryRegister(id, addr, out _);

        Assert.True(ok);
        Assert.Equal(1, dir.Count);
    }

    [Fact]
    public void Register_ExistingGrain_ReturnsExisting()
    {
        var dir = new InMemoryGrainDirectory();
        var id = new GrainId(new GrainType("Counter"), "key1");
        var addr1 = SiloAddress.Loopback(11111);
        var addr2 = SiloAddress.Loopback(22222);

        dir.TryRegister(id, addr1, out _);
        bool ok = dir.TryRegister(id, addr2, out SiloAddress existing);

        Assert.False(ok);
        Assert.Equal(addr1, existing);
    }

    [Fact]
    public void Lookup_RegisteredGrain_ReturnsAddress()
    {
        var dir = new InMemoryGrainDirectory();
        var id = new GrainId(new GrainType("Counter"), "key1");
        var addr = SiloAddress.Loopback(11111);

        dir.TryRegister(id, addr, out _);
        bool found = dir.TryLookup(id, out SiloAddress result);

        Assert.True(found);
        Assert.Equal(addr, result);
    }

    [Fact]
    public void Lookup_UnregisteredGrain_ReturnsFalse()
    {
        var dir = new InMemoryGrainDirectory();
        var id = new GrainId(new GrainType("Counter"), "missing");

        bool found = dir.TryLookup(id, out _);

        Assert.False(found);
    }

    [Fact]
    public void Unregister_CorrectAddress_RemovesEntry()
    {
        var dir = new InMemoryGrainDirectory();
        var id = new GrainId(new GrainType("Counter"), "key1");
        var addr = SiloAddress.Loopback(11111);

        dir.TryRegister(id, addr, out _);
        bool removed = dir.TryUnregister(id, addr);

        Assert.True(removed);
        Assert.Equal(0, dir.Count);
    }

    [Fact]
    public void Unregister_WrongAddress_DoesNotRemove()
    {
        var dir = new InMemoryGrainDirectory();
        var id = new GrainId(new GrainType("Counter"), "key1");
        var addr1 = SiloAddress.Loopback(11111);
        var addr2 = SiloAddress.Loopback(22222);

        dir.TryRegister(id, addr1, out _);
        bool removed = dir.TryUnregister(id, addr2);

        Assert.False(removed);
        Assert.Equal(1, dir.Count);
    }

    [Fact]
    public void MultipleGrains_IndependentRegistrations()
    {
        var dir = new InMemoryGrainDirectory();
        var addr = SiloAddress.Loopback(11111);

        for (int i = 0; i < 100; i++)
        {
            var id = new GrainId(new GrainType("Grain"), $"key{i}");
            dir.TryRegister(id, addr, out _);
        }

        Assert.Equal(100, dir.Count);
    }
}