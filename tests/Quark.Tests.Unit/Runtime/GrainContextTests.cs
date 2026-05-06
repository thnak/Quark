using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;
using Quark.Runtime;
using Xunit;

namespace Quark.Tests.Unit.Runtime;

public sealed class GrainContextTests
{
    // Minimal stub factory/provider for tests that don't use GrainFactory.
    private static readonly IGrainFactory NullFactory = new NullGrainFactory();
    private static readonly IServiceProvider NullServices = new NullServiceProvider();

    private static GrainContext MakeContext(GrainId id)
    {
        return new GrainContext(id, NullFactory, NullServices);
    }

    [Fact]
    public async Task ActivateAsync_SetsStatusActive()
    {
        GrainContext ctx = MakeContext(new GrainId(new GrainType("MyGrain"), "1"));
        var grain = new TestGrain();

        await ctx.ActivateAsync(grain);

        Assert.Equal(GrainActivationStatus.Active, ctx.ActivationStatus);
    }

    [Fact]
    public async Task ActivateAsync_CallsOnActivate()
    {
        GrainContext ctx = MakeContext(new GrainId(new GrainType("MyGrain"), "1"));
        var grain = new TestGrain();

        await ctx.ActivateAsync(grain);

        Assert.True(grain.ActivateCalled);
    }

    [Fact]
    public async Task DeactivateAsync_SetsStatusInactive()
    {
        GrainContext ctx = MakeContext(new GrainId(new GrainType("MyGrain"), "1"));
        var grain = new TestGrain();

        await ctx.ActivateAsync(grain);
        await ctx.DeactivateAsync(grain, DeactivationReason.ApplicationRequested);

        Assert.Equal(GrainActivationStatus.Inactive, ctx.ActivationStatus);
        Assert.True(grain.DeactivateCalled);
    }

    [Fact]
    public async Task Deactivate_Method_TriggersStop()
    {
        GrainContext ctx = MakeContext(new GrainId(new GrainType("MyGrain"), "1"));
        var grain = new TestGrain();

        await ctx.ActivateAsync(grain);
        ctx.Deactivate(DeactivationReason.ApplicationRequested);

        await Task.Delay(50);

        Assert.Equal(GrainActivationStatus.Inactive, ctx.ActivationStatus);
    }

    [Fact]
    public void GrainFactory_ExposedOnContext()
    {
        GrainContext ctx = MakeContext(new GrainId(new GrainType("MyGrain"), "1"));
        Assert.Same(NullFactory, ctx.GrainFactory);
    }

    // -----------------------------------------------------------------------

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

    private sealed class NullGrainFactory : IGrainFactory
    {
        public TGI GetGrain<TGI>(string key) where TGI : IGrainWithStringKey
        {
            throw new NotImplementedException();
        }

        public TGI GetGrain<TGI>(long key) where TGI : IGrainWithIntegerKey
        {
            throw new NotImplementedException();
        }

        public TGI GetGrain<TGI>(Guid key) where TGI : IGrainWithGuidKey
        {
            throw new NotImplementedException();
        }

        public TGI GetGrain<TGI>(long key, string? ext) where TGI : IGrainWithIntegerCompoundKey
        {
            throw new NotImplementedException();
        }

        public TGI GetGrain<TGI>(Guid key, string? ext) where TGI : IGrainWithGuidCompoundKey
        {
            throw new NotImplementedException();
        }

        public IGrain GetGrain(Type t, string key)
        {
            throw new NotImplementedException();
        }

        public IGrain GetGrain(Type t, Guid key)
        {
            throw new NotImplementedException();
        }

        public IGrain GetGrain(Type t, long key)
        {
            throw new NotImplementedException();
        }
    }

    private sealed class NullServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType)
        {
            return null;
        }
    }
}
