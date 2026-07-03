using Microsoft.Extensions.DependencyInjection;
using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;
using Quark.Runtime;
using Quark.Runtime.Clustering;
using Quark.Serialization;
using Quark.Serialization.Abstractions.Buffers;
using Quark.Transport.Abstractions;
using Xunit;

namespace Quark.Tests.Unit.Clustering;

public sealed class SiloCallInvokerTests
{
    private static (GrainMessageSerializer grain, MessageSerializer msg) BuildSerializers()
    {
        var services = new ServiceCollection();
        services.AddQuarkSerialization();
        services.AddQuarkRuntime();
        using var sp = services.BuildServiceProvider();
        return (sp.GetRequiredService<GrainMessageSerializer>(), sp.GetRequiredService<MessageSerializer>());
    }

    [Fact]
    public async Task InvokeVoidAsync_Stamps_XQuarkHop_Header()
    {
        MessageEnvelope? sent = null;
        var (grain, msg) = BuildSerializers();
        var peer = SiloAddress.Loopback(22221);

        var invoker = new SiloCallInvoker(peer, (env, _) =>
        {
            sent = env;
            var response = new GrainInvocationResponse(true, ReadOnlyMemory<byte>.Empty, null);
            return Task.FromResult(new MessageEnvelope
            {
                CorrelationId = env.CorrelationId,
                MessageType = MessageType.Response,
                Payload = grain.SerializeResponse(response)
            });
        }, grain, msg);

        var grainId = new GrainId(new GrainType("TestGrain"), "k1");
        await invoker.InvokeVoidAsync(grainId, new TestVoidInvokable());

        Assert.NotNull(sent);
        Assert.Equal("1", sent!.Headers?.Get("x-quark-hop"));
    }

    [Fact]
    public async Task InvokeAsync_Stamps_XQuarkHop_Header()
    {
        MessageEnvelope? sent = null;
        var (grain, msg) = BuildSerializers();
        var peer = SiloAddress.Loopback(22222);

        var invoker = new SiloCallInvoker(peer, (env, _) =>
        {
            sent = env;
            // TestIntInvokable.DeserializeResult reads nothing (returns hardcoded 0)
            var response = new GrainInvocationResponse(true, ReadOnlyMemory<byte>.Empty, null);
            return Task.FromResult(new MessageEnvelope
            {
                CorrelationId = env.CorrelationId,
                MessageType = MessageType.Response,
                Payload = grain.SerializeResponse(response)
            });
        }, grain, msg);

        var grainId = new GrainId(new GrainType("TestGrain"), "k2");
        await invoker.InvokeAsync<TestIntInvokable, int>(grainId, new TestIntInvokable());

        Assert.NotNull(sent);
        Assert.Equal("1", sent!.Headers?.Get("x-quark-hop"));
    }

    [Fact]
    public async Task InvokeVoidAsync_RemoteError_Throws()
    {
        var (grain, msg) = BuildSerializers();
        var peer = SiloAddress.Loopback(22223);

        var invoker = new SiloCallInvoker(peer, (env, _) =>
        {
            var response = new GrainInvocationResponse(false, ReadOnlyMemory<byte>.Empty, "remote error");
            return Task.FromResult(new MessageEnvelope
            {
                CorrelationId = env.CorrelationId,
                MessageType = MessageType.Response,
                Payload = grain.SerializeResponse(response)
            });
        }, grain, msg);

        var grainId = new GrainId(new GrainType("TestGrain"), "k3");
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => invoker.InvokeVoidAsync(grainId, new TestVoidInvokable()).AsTask());
        Assert.Contains("remote error", ex.Message);
    }

    [Fact]
    public void InvokeObserverAsync_Throws_NotSupported()
    {
        var (grain, msg) = BuildSerializers();
        var peer = SiloAddress.Loopback(22224);

        var invoker = new SiloCallInvoker(peer, (_, _) => Task.FromResult(new MessageEnvelope()), grain, msg);
        var grainId = new GrainId(new GrainType("Observer"), "obs1");

        Assert.Throws<NotSupportedException>(
            () => invoker.InvokeObserverAsync(grainId, new TestObserverInvokable()));
    }

    // --- test invokables ---

    private struct TestVoidInvokable : IGrainVoidInvokable
    {
        public uint MethodId => 1u;
        public void Serialize(ref CodecWriter writer) { }
        public ValueTask Invoke(IGrainBehavior behavior) => ValueTask.CompletedTask;
    }

    private struct TestIntInvokable : IGrainInvokable<int>
    {
        public uint MethodId => 2u;
        public void Serialize(ref CodecWriter writer) { }
        public ValueTask<int> Invoke(IGrainBehavior behavior) => ValueTask.FromResult(0);
        public int DeserializeResult(ref CodecReader reader) => 0;
    }

    private struct TestObserverInvokable : IObserverVoidInvokable
    {
        public uint MethodId => 3u;
        public void Serialize(ref CodecWriter writer) { }
        public ValueTask Invoke(object target) => ValueTask.CompletedTask;
    }
}
