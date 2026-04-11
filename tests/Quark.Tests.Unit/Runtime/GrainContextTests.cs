using Quark.Core.Abstractions;
using Quark.Runtime;
using Xunit;

namespace Quark.Tests.Unit.Runtime;

public sealed class GrainContextTests
{
    [Fact]
    public async Task ActivateAsync_SetsStatusActive()
    {
        var id = new GrainId(new GrainType("MyGrain"), "1");
        var ctx = new GrainContext(id);
        var grain = new TestGrain();

        await ctx.ActivateAsync(grain);

        Assert.Equal(GrainActivationStatus.Active, ctx.ActivationStatus);
    }

    [Fact]
    public async Task ActivateAsync_CallsOnActivate()
    {
        var id = new GrainId(new GrainType("MyGrain"), "1");
        var ctx = new GrainContext(id);
        var grain = new TestGrain();

        await ctx.ActivateAsync(grain);

        Assert.True(grain.ActivateCalled);
    }

    [Fact]
    public async Task DeactivateAsync_SetsStatusInactive()
    {
        var id = new GrainId(new GrainType("MyGrain"), "1");
        var ctx = new GrainContext(id);
        var grain = new TestGrain();

        await ctx.ActivateAsync(grain);
        await ctx.DeactivateAsync(grain, DeactivationReason.ApplicationRequested);

        Assert.Equal(GrainActivationStatus.Inactive, ctx.ActivationStatus);
        Assert.True(grain.DeactivateCalled);
    }

    [Fact]
    public async Task Deactivate_Method_TriggersStop()
    {
        var id = new GrainId(new GrainType("MyGrain"), "1");
        var ctx = new GrainContext(id);
        var grain = new TestGrain();

        await ctx.ActivateAsync(grain);
        ctx.Deactivate(DeactivationReason.ApplicationRequested);

        // Allow async stop to complete.
        await Task.Delay(50);

        Assert.Equal(GrainActivationStatus.Inactive, ctx.ActivationStatus);
    }

    private sealed class TestGrain : Grain
    {
        public bool ActivateCalled { get; private set; }
        public bool DeactivateCalled { get; private set; }

        public override Task OnActivateAsync(CancellationToken ct)
        {
            ActivateCalled = true;
            return Task.CompletedTask;
        }

        public override Task OnDeactivateAsync(DeactivationReason reason, CancellationToken ct)
        {
            DeactivateCalled = true;
            return Task.CompletedTask;
        }
    }
}

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
