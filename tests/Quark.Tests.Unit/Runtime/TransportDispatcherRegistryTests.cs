using Quark.Client;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;
using Quark.Runtime;
using Xunit;

namespace Quark.Tests.Unit.Runtime;

/// <summary>
///     Unit coverage for the transport-dispatcher registries (#24): the silo-side
///     <see cref="TransportGrainDispatcherRegistry"/> and the client-side
///     <see cref="ObserverTransportDispatcherRegistry"/>, which share a common
///     <see cref="TransportDispatcherRegistry"/> base after de-duplication.
/// </summary>
public sealed class TransportDispatcherRegistryTests
{
    private static GrainType Type(string name) => new(name);

    // =====================================================================
    // TransportGrainDispatcherRegistry (silo-side)
    // =====================================================================

    [Fact]
    public void Grain_Register_ThenTryGet_ReturnsSameDispatcher()
    {
        var registry = new TransportGrainDispatcherRegistry();
        var dispatcher = new StubDispatcher();
        registry.Register(Type("G"), dispatcher);

        Assert.True(registry.TryGet(Type("G"), out ITransportGrainDispatcher? got));
        Assert.Same(dispatcher, got);
    }

    [Fact]
    public void Grain_TryGet_Unregistered_ReturnsFalse()
    {
        var registry = new TransportGrainDispatcherRegistry();
        Assert.False(registry.TryGet(Type("missing"), out _));
    }

    [Fact]
    public void Grain_GetDispatcher_Registered_ReturnsDispatcher()
    {
        var registry = new TransportGrainDispatcherRegistry();
        var dispatcher = new StubDispatcher();
        registry.Register(Type("G"), dispatcher);

        Assert.Same(dispatcher, registry.GetDispatcher(Type("G")));
    }

    [Fact]
    public void Grain_GetDispatcher_Unregistered_Throws()
    {
        var registry = new TransportGrainDispatcherRegistry();
        InvalidOperationException ex =
            Assert.Throws<InvalidOperationException>(() => registry.GetDispatcher(Type("missing")));
        Assert.Contains("missing", ex.Message);
    }

    [Fact]
    public void Grain_Register_Overwrites()
    {
        var registry = new TransportGrainDispatcherRegistry();
        var first = new StubDispatcher();
        var second = new StubDispatcher();
        registry.Register(Type("G"), first);
        registry.Register(Type("G"), second);

        Assert.True(registry.TryGet(Type("G"), out ITransportGrainDispatcher? got));
        Assert.Same(second, got);
    }

    // =====================================================================
    // ObserverTransportDispatcherRegistry (client-side)
    // =====================================================================

    [Fact]
    public void Observer_Register_ThenTryGet_ReturnsSameDispatcher()
    {
        var registry = new ObserverTransportDispatcherRegistry();
        var dispatcher = new StubDispatcher();
        registry.Register(Type("observer:IFoo"), dispatcher);

        Assert.True(registry.TryGet(Type("observer:IFoo"), out ITransportGrainDispatcher? got));
        Assert.Same(dispatcher, got);
    }

    [Fact]
    public void Observer_TryGet_Unregistered_ReturnsFalse()
    {
        var registry = new ObserverTransportDispatcherRegistry();
        Assert.False(registry.TryGet(Type("missing"), out _));
    }

    // =====================================================================
    // Shared base (the de-duplication this issue is about, #24)
    // =====================================================================

    [Fact]
    public void BothRegistries_ShareCommonBase()
    {
        Assert.IsAssignableFrom<TransportDispatcherRegistry>(new TransportGrainDispatcherRegistry());
        Assert.IsAssignableFrom<TransportDispatcherRegistry>(new ObserverTransportDispatcherRegistry());
    }

    private sealed class StubDispatcher : ITransportGrainDispatcher
    {
        public Task<ReadOnlyMemory<byte>> DispatchAsync(
            GrainId grainId, uint methodId, ReadOnlyMemory<byte> argumentPayload,
            IGrainCallInvoker invoker, IGrainFactory? factory, CancellationToken cancellationToken = default)
            => Task.FromResult<ReadOnlyMemory<byte>>(ReadOnlyMemory<byte>.Empty);
    }
}
