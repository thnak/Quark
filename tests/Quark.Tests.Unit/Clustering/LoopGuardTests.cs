using Microsoft.Extensions.DependencyInjection;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;
using Quark.Runtime;
using Quark.Serialization;
using Quark.Serialization.Abstractions.Buffers;
using Quark.Transport.Abstractions;
using Xunit;

namespace Quark.Tests.Unit.Clustering;

public sealed class LoopGuardTests
{
    [Fact]
    public async Task Request_WithHopHeader_DispatchesToTerminalInvoker()
    {
        bool routingCalled = false;
        bool terminalCalled = false;

        var routing = new RecordingInvoker(() => routingCalled = true);
        var terminal = new RecordingInvoker(() => terminalCalled = true);

        var dispatcher = BuildDispatcher(routing, terminal);

        var headers = new MessageHeaders();
        headers.Set("x-quark-hop", "1");
        var grainId = new GrainId(new GrainType("TestGrain"), "k1");

        await dispatcher.DispatchAsync(BuildRequestEnvelope(grainId, headers));

        Assert.False(routingCalled, "Routing invoker must not be called for a forwarded hop.");
        Assert.True(terminalCalled, "Terminal invoker must handle forwarded hops.");
    }

    [Fact]
    public async Task Request_WithoutHopHeader_DispatchesToRoutingInvoker()
    {
        bool routingCalled = false;
        bool terminalCalled = false;

        var routing = new RecordingInvoker(() => routingCalled = true);
        var terminal = new RecordingInvoker(() => terminalCalled = true);

        var dispatcher = BuildDispatcher(routing, terminal);
        var grainId = new GrainId(new GrainType("TestGrain"), "k2");

        await dispatcher.DispatchAsync(BuildRequestEnvelope(grainId, headers: null));

        Assert.True(routingCalled);
        Assert.False(terminalCalled);
    }

    [Fact]
    public async Task Request_WithHopHeader_NoTerminalConfigured_FallsBackToRoutingInvoker()
    {
        bool routingCalled = false;

        var routing = new RecordingInvoker(() => routingCalled = true);
        // no terminal invoker — MessageDispatcher falls back to routing invoker
        var dispatcher = BuildDispatcher(routing, terminalInvoker: null);

        var headers = new MessageHeaders();
        headers.Set("x-quark-hop", "1");
        var grainId = new GrainId(new GrainType("TestGrain"), "k3");

        await dispatcher.DispatchAsync(BuildRequestEnvelope(grainId, headers));

        Assert.True(routingCalled, "When no terminal configured, routing invoker handles the hop.");
    }

    // --- helpers ---

    private static MessageDispatcher BuildDispatcher(
        IGrainCallInvoker routing, IGrainCallInvoker? terminalInvoker)
    {
        var services = new ServiceCollection();
        services.AddQuarkSerialization();
        services.AddQuarkRuntime();
        using var sp = services.BuildServiceProvider();
        var serializer = sp.GetRequiredService<GrainMessageSerializer>();
        return new MessageDispatcher(
            new TransportGrainDispatcherRegistry(),
            routing,
            serializer,
            grainFactory: null,
            terminalInvoker: terminalInvoker);
    }

    private static MessageEnvelope BuildRequestEnvelope(GrainId grainId, MessageHeaders? headers)
    {
        var services = new ServiceCollection();
        services.AddQuarkSerialization();
        services.AddQuarkRuntime();
        using var sp = services.BuildServiceProvider();
        var serializer = sp.GetRequiredService<GrainMessageSerializer>();
        byte[] payload = serializer.SerializeRequest(new GrainInvocationRequest(grainId, 99u, ReadOnlyMemory<byte>.Empty));
        return new MessageEnvelope
        {
            CorrelationId = 1,
            MessageType = MessageType.Request,
            Payload = payload,
            Headers = headers
        };
    }

    private sealed class RecordingInvoker(Action onInvoke) : IGrainCallInvoker
    {
        public ValueTask<TResult> InvokeAsync<TInvokable, TResult>(
            GrainId grainId, TInvokable invokable, CancellationToken cancellationToken = default)
            where TInvokable : struct, IGrainInvokable<TResult>
        {
            onInvoke();
            return ValueTask.FromResult(default(TResult)!);
        }

        public ValueTask InvokeVoidAsync<TInvokable>(
            GrainId grainId, TInvokable invokable, CancellationToken cancellationToken = default)
            where TInvokable : struct, IGrainVoidInvokable
        {
            onInvoke();
            return ValueTask.CompletedTask;
        }

        public ValueTask InvokeObserverAsync<TInvokable>(
            GrainId grainId, TInvokable invokable, CancellationToken cancellationToken = default)
            where TInvokable : struct, IObserverVoidInvokable
        {
            onInvoke();
            return ValueTask.CompletedTask;
        }
    }
}
