using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;
using Quark.Diagnostics.Abstractions;
using Quark.Runtime;
using Quark.Serialization.Abstractions.Buffers;
using Xunit;

namespace Quark.Tests.Unit.Diagnostics;

public sealed class ObserverInvokedDiagnosticTests
{
    [Fact]
    public async Task InvokeObserverAsync_LocalRegistry_FiresOnObserverInvoked()
    {
        var listener = new FakeListener();
        var registry = new ObserverRegistry();
        LocalGrainCallInvoker invoker = BuildInvoker(registry, listener);

        GrainId observerId = GrainId.Create(new GrainType("observer:IFoo"), "client-1");
        registry.Register(observerId, new object());

        bool invoked = false;
        await invoker.InvokeObserverAsync(observerId, new StubInvokable(7u, _ => invoked = true));

        Assert.True(invoked);
        Assert.Single(listener.Events);
        ObserverInvokedEvent evt = listener.Events[0];
        Assert.Equal(observerId, evt.ObserverId);
        Assert.Equal(7u, evt.MethodId);
        Assert.True(evt.Success);
        Assert.Null(evt.Exception);
    }

    [Fact]
    public async Task InvokeObserverAsync_ThrowingCallback_FiresOnObserverInvoked_WithFailure()
    {
        var listener = new FakeListener();
        var registry = new ObserverRegistry();
        LocalGrainCallInvoker invoker = BuildInvoker(registry, listener);

        GrainId observerId = GrainId.Create(new GrainType("observer:IFoo"), "client-2");
        registry.Register(observerId, new object());

        var boom = new InvalidOperationException("boom");
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => invoker.InvokeObserverAsync(observerId, new StubInvokable(3u, _ => throw boom)).AsTask());

        Assert.Single(listener.Events);
        ObserverInvokedEvent evt = listener.Events[0];
        Assert.Equal(3u, evt.MethodId);
        Assert.False(evt.Success);
        Assert.Same(boom, evt.Exception);
    }

    [Fact]
    public async Task InvokeObserverAsync_UnregisteredObserver_DoesNotFireOnObserverInvoked()
    {
        var listener = new FakeListener();
        LocalGrainCallInvoker invoker = BuildInvoker(new ObserverRegistry(), listener);

        GrainId observerId = GrainId.Create(new GrainType("observer:IFoo"), "missing");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => invoker.InvokeObserverAsync(observerId, new StubInvokable(0u, _ => { })).AsTask());

        Assert.Empty(listener.Events);
    }

    // -----------------------------------------------------------------------

    private static LocalGrainCallInvoker BuildInvoker(
        ObserverRegistry observerRegistry,
        IQuarkDiagnosticListener listener)
    {
        IServiceProvider sp = new ServiceCollection().AddLogging().BuildServiceProvider();
        IOptions<SiloRuntimeOptions> options = Options.Create(new SiloRuntimeOptions
        {
            ClusterId = "test",
            ServiceId = "diag-test",
            SiloName = "silo0",
        });

        return new LocalGrainCallInvoker(
            new GrainActivationTable(NullLogger<GrainActivationTable>.Instance),
            new GrainTypeRegistry(),
            new InMemoryGrainDirectory(),
            sp,
            options,
            NullLogger<LocalGrainCallInvoker>.Instance,
            NullLogger<GrainActivation>.Instance,
            observerRegistry: observerRegistry,
            diagnostics: listener);
    }

    private readonly struct StubInvokable(uint methodId, Action<object> onInvoke) : IObserverVoidInvokable
    {
        public uint MethodId { get; } = methodId;

        public ValueTask Invoke(object target)
        {
            onInvoke(target);
            return ValueTask.CompletedTask;
        }

        public void Serialize(ref CodecWriter writer) { }
    }

    private sealed class FakeListener : IQuarkDiagnosticListener
    {
        private readonly List<ObserverInvokedEvent> _events = [];
        public IReadOnlyList<ObserverInvokedEvent> Events => _events;
        public void OnObserverInvoked(in ObserverInvokedEvent e) => _events.Add(e);
    }
}
