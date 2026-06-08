using Quark.Client.Tcp;
using Quark.Streaming.Abstractions;
using Quark.Transport.Abstractions;
using Xunit;

namespace Quark.Tests.Unit.Streaming;

public class TcpStreamPushDispatcherTests
{
    [Fact]
    public async Task DispatchAsync_RoutesToRegisteredSubscription()
    {
        var dispatcher = new TcpStreamPushDispatcher();
        var streamId = StreamId.Create("ns", "key");
        var subId = Guid.NewGuid();
        var dispatched = new List<byte[]>();

        var sub = new FakeSubscription(streamId, (payload, _) =>
        {
            dispatched.Add(payload.ToArray());
            return Task.CompletedTask;
        });
        dispatcher.Register(subId, sub);

        var envelope = BuildPushEnvelope(subId, streamId, new byte[] { 0x01, 0x02 }, seq: 1);
        await dispatcher.DispatchAsync(envelope);

        Assert.Single(dispatched);
        Assert.Equal(new byte[] { 0x01, 0x02 }, dispatched[0]);
    }

    [Fact]
    public async Task DispatchAsync_NoOp_WhenSubIdUnknown()
    {
        var dispatcher = new TcpStreamPushDispatcher();
        var envelope = BuildPushEnvelope(Guid.NewGuid(), StreamId.Create("ns", "key"), Array.Empty<byte>(), seq: 0);
        // Should not throw
        await dispatcher.DispatchAsync(envelope);
    }

    [Fact]
    public void Unregister_RemovesSubscription()
    {
        var dispatcher = new TcpStreamPushDispatcher();
        var streamId = StreamId.Create("ns", "key");
        var subId = Guid.NewGuid();
        dispatcher.Register(subId, new FakeSubscription(streamId, (_, _) => Task.CompletedTask));
        dispatcher.Unregister(subId);
        Assert.Empty(dispatcher.GetForStream(streamId));
    }

    private static MessageEnvelope BuildPushEnvelope(Guid subId, StreamId streamId, byte[] payload, long seq)
    {
        var headers = new MessageHeaders();
        headers.Set("sub-id", subId.ToString("D"));
        headers.Set("stream-ns", streamId.Namespace);
        headers.Set("stream-key", streamId.Key);
        headers.Set("seq", seq.ToString());

        return new MessageEnvelope
        {
            MessageType = MessageType.StreamPush,
            CorrelationId = -1,
            Headers = headers,
            Payload = payload,
        };
    }

    private sealed class FakeSubscription(
        StreamId streamId,
        Func<ReadOnlyMemory<byte>, StreamSequenceToken, Task> onDispatch) : IClientStreamSubscription
    {
        public StreamId StreamId => streamId;
        public Task DispatchAsync(ReadOnlyMemory<byte> payload, StreamSequenceToken token) => onDispatch(payload, token);
        public Task ErrorAsync(Exception ex) => Task.CompletedTask;
    }
}
