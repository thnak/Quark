using Quark.Client;
using Quark.Client.Tcp;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;
using Quark.Runtime;
using Quark.Transport.Abstractions;
using Xunit;

namespace Quark.Tests.Unit.Runtime;

/// <summary>
///     Unit coverage for the observer back-channel leaf primitives that back the fixes shipped in
///     #49 for the observer bug cluster (#20–#23), which previously had no regression coverage:
///     <list type="bullet">
///         <item><see cref="ObserverRegistry"/> — local observer registration, including
///             <c>UnregisterByTarget</c> (the by-instance delete path used by
///             <c>DeleteObjectReference</c>, #21/#23).</item>
///         <item><see cref="TcpClientObserverTable"/> — silo-side write-back table, including
///             <c>RemoveAll</c> (the per-connection teardown cleanup that prevents stale write-backs
///             firing into a disposed connection, #22).</item>
///         <item><see cref="TcpObserverDispatcher"/> — client-side dispatch of incoming
///             <c>ObserverInvoke</c> frames: correct routing plus defensive handling of stale /
///             malformed frames so a bad frame cannot crash the connection (#20).</item>
///     </list>
/// </summary>
public sealed class ObserverBackChannelTests
{
    private static GrainId Observer(string key) =>
        GrainId.Create(new GrainType("observer:IFoo"), key);

    // =====================================================================
    // ObserverRegistry
    // =====================================================================

    [Fact]
    public void Register_ThenTryGet_ReturnsSameTarget()
    {
        var registry = new ObserverRegistry();
        var target = new object();
        GrainId id = Observer("a");

        registry.Register(id, target);

        Assert.True(registry.TryGet(id, out ObserverRegistry.ObserverEntry entry));
        Assert.Same(target, entry.Target);
    }

    [Fact]
    public void TryGet_Unregistered_ReturnsFalse()
    {
        var registry = new ObserverRegistry();
        Assert.False(registry.TryGet(Observer("missing"), out _));
    }

    [Fact]
    public void Register_SameGrainId_OverwritesTarget()
    {
        var registry = new ObserverRegistry();
        GrainId id = Observer("a");
        var first = new object();
        var second = new object();

        registry.Register(id, first);
        registry.Register(id, second);

        Assert.True(registry.TryGet(id, out ObserverRegistry.ObserverEntry entry));
        Assert.Same(second, entry.Target);
    }

    [Fact]
    public void Unregister_RemovesEntry()
    {
        var registry = new ObserverRegistry();
        GrainId id = Observer("a");
        registry.Register(id, new object());

        registry.Unregister(id);

        Assert.False(registry.TryGet(id, out _));
    }

    [Fact]
    public void UnregisterByTarget_RemovesAllRegistrationsOfThatInstance_LeavesOthers()
    {
        // DeleteObjectReference (the in-process / proxy-only path, #21/#23) removes by target
        // instance. The same CLR object may be registered under more than one GrainId; all of its
        // registrations must go, while a different observer's registration must remain.
        var registry = new ObserverRegistry();
        var shared = new object();
        var other = new object();
        GrainId a = Observer("a");
        GrainId b = Observer("b");
        GrainId c = Observer("c");

        registry.Register(a, shared);
        registry.Register(b, shared);
        registry.Register(c, other);

        registry.UnregisterByTarget(shared);

        Assert.False(registry.TryGet(a, out _));
        Assert.False(registry.TryGet(b, out _));
        Assert.True(registry.TryGet(c, out ObserverRegistry.ObserverEntry entry));
        Assert.Same(other, entry.Target);
    }

    // =====================================================================
    // TcpClientObserverTable
    // =====================================================================

    private static Func<uint, ReadOnlyMemory<byte>, CancellationToken, Task> NoopWriteBack()
        => (_, _, _) => Task.CompletedTask;

    [Fact]
    public void Table_Register_ThenTryGet_ReturnsWriteBack()
    {
        var table = new TcpClientObserverTable();
        Func<uint, ReadOnlyMemory<byte>, CancellationToken, Task> writeBack = NoopWriteBack();
        GrainId id = Observer("a");

        table.Register(id, writeBack);

        Assert.True(table.TryGet(id, out Func<uint, ReadOnlyMemory<byte>, CancellationToken, Task>? got));
        Assert.Same(writeBack, got);
    }

    [Fact]
    public void Table_TryGet_Unregistered_ReturnsFalse_AndNullDelegate()
    {
        var table = new TcpClientObserverTable();
        Assert.False(table.TryGet(Observer("missing"), out Func<uint, ReadOnlyMemory<byte>, CancellationToken, Task>? got));
        Assert.Null(got);
    }

    [Fact]
    public void Table_Unregister_RemovesEntry()
    {
        var table = new TcpClientObserverTable();
        GrainId id = Observer("a");
        table.Register(id, NoopWriteBack());

        table.Unregister(id);

        Assert.False(table.TryGet(id, out _));
    }

    [Fact]
    public void Table_RemoveAll_RemovesListedEntries_AndLeavesOthers()
    {
        // The gateway connection teardown removes exactly the observers it registered (#22) so that
        // no stale write-back can fire into the now-disposed connection's writer/lock — while other
        // connections' observers are untouched.
        var table = new TcpClientObserverTable();
        GrainId a = Observer("a");
        GrainId b = Observer("b");
        GrainId c = Observer("c");
        table.Register(a, NoopWriteBack());
        table.Register(b, NoopWriteBack());
        table.Register(c, NoopWriteBack());

        table.RemoveAll([a, b]);

        Assert.False(table.TryGet(a, out _));
        Assert.False(table.TryGet(b, out _));
        Assert.True(table.TryGet(c, out _));
    }

    // =====================================================================
    // TcpObserverDispatcher — incoming ObserverInvoke frame routing (#20)
    // =====================================================================

    private static MessageEnvelope ObserverInvokeFrame(
        string? grainType, string? grainKey, string? methodId, byte[]? payload = null)
    {
        var headers = new MessageHeaders();
        if (grainType is not null) headers.Set("grain-type", grainType);
        if (grainKey is not null) headers.Set("grain-key", grainKey);
        if (methodId is not null) headers.Set("method-id", methodId);

        return new MessageEnvelope
        {
            MessageType = MessageType.ObserverInvoke,
            CorrelationId = -1,
            Headers = headers,
            Payload = payload ?? []
        };
    }

    [Fact]
    public async Task Dispatch_ValidFrame_RoutesToRegisteredDispatcher()
    {
        var registry = new ObserverTransportDispatcherRegistry();
        var fake = new RecordingDispatcher();
        var grainType = new GrainType("observer:IFoo");
        registry.Register(grainType, fake);

        var dispatcher = new TcpObserverDispatcher(new ObserverRegistry(), registry);
        byte[] payload = [1, 2, 3];

        await dispatcher.DispatchAsync(
            ObserverInvokeFrame(grainType.Value, "client-1", "7", payload));

        Assert.Equal(1, fake.Calls);
        Assert.Equal(GrainId.Create(grainType, "client-1"), fake.LastGrainId);
        Assert.Equal(7u, fake.LastMethodId);
        Assert.Equal(payload, fake.LastPayload.ToArray());
    }

    [Fact]
    public async Task Dispatch_MissingHeaders_DoesNotThrow_AndDoesNotDispatch()
    {
        var registry = new ObserverTransportDispatcherRegistry();
        var fake = new RecordingDispatcher();
        registry.Register(new GrainType("observer:IFoo"), fake);
        var dispatcher = new TcpObserverDispatcher(new ObserverRegistry(), registry);

        // No headers at all, and each partial combination — none should throw or dispatch.
        await dispatcher.DispatchAsync(ObserverInvokeFrame(null, null, null));
        await dispatcher.DispatchAsync(ObserverInvokeFrame("observer:IFoo", null, "0"));
        await dispatcher.DispatchAsync(ObserverInvokeFrame("observer:IFoo", "client-1", null));

        Assert.Equal(0, fake.Calls);
    }

    [Fact]
    public async Task Dispatch_UnparseableMethodId_DoesNotThrow_AndDoesNotDispatch()
    {
        var registry = new ObserverTransportDispatcherRegistry();
        var fake = new RecordingDispatcher();
        registry.Register(new GrainType("observer:IFoo"), fake);
        var dispatcher = new TcpObserverDispatcher(new ObserverRegistry(), registry);

        await dispatcher.DispatchAsync(ObserverInvokeFrame("observer:IFoo", "client-1", "not-a-number"));

        Assert.Equal(0, fake.Calls);
    }

    [Fact]
    public async Task Dispatch_StaleFrameForUnknownObserverType_DoesNotThrow_AndDoesNotDispatch()
    {
        // A frame for an observer type with no registered dispatcher (e.g. a stale ObserverInvoke
        // arriving after teardown) must be ignored, not crash the dispatch worker (#20).
        var registry = new ObserverTransportDispatcherRegistry();
        var fake = new RecordingDispatcher();
        registry.Register(new GrainType("observer:IFoo"), fake);
        var dispatcher = new TcpObserverDispatcher(new ObserverRegistry(), registry);

        await dispatcher.DispatchAsync(ObserverInvokeFrame("observer:IUnknown", "client-1", "0"));

        Assert.Equal(0, fake.Calls);
    }

    private sealed class RecordingDispatcher : ITransportGrainDispatcher
    {
        public int Calls { get; private set; }
        public GrainId LastGrainId { get; private set; }
        public uint LastMethodId { get; private set; }
        public ReadOnlyMemory<byte> LastPayload { get; private set; }

        public Task<ReadOnlyMemory<byte>> DispatchAsync(
            GrainId grainId,
            uint methodId,
            ReadOnlyMemory<byte> argumentPayload,
            IGrainCallInvoker invoker,
            IGrainFactory? factory,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            LastGrainId = grainId;
            LastMethodId = methodId;
            LastPayload = argumentPayload;
            return Task.FromResult<ReadOnlyMemory<byte>>(ReadOnlyMemory<byte>.Empty);
        }
    }
}
