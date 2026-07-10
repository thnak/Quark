using Quark.Streaming.Abstractions;
using Quark.Streaming.InMemory;
using Xunit;

namespace Quark.Tests.Unit.Streaming;

public class StreamFanOutTests
{
    [Fact]
    public async Task PublishAsync_FansOutConcurrently_FastSubscriberNotBlockedBySlowOne()
    {
        var registry = new StreamSubscriptionRegistry();
        StreamId streamId = StreamId.Create("ns", "key");

        var slowGate = new TaskCompletionSource();
        var fastDelivered = new TaskCompletionSource();

        // Subscribe the SLOW observer FIRST: with a sequential foreach the fast one would be blocked
        // behind it and this test would hang.
        var slow = new GateObserver<string>(_ => slowGate.Task);
        var fast = new GateObserver<string>(_ =>
        {
            fastDelivered.TrySetResult();
            return Task.CompletedTask;
        });
        registry.Subscribe(streamId, slow);
        registry.Subscribe(streamId, fast);

        Task publish = registry.PublishAsync(streamId, "x", null).AsTask();

        await fastDelivered.Task.WaitAsync(TimeSpan.FromSeconds(5)); // fast ran despite slow blocking
        Assert.False(publish.IsCompleted);                          // publisher still awaits slow (backpressure)

        slowGate.TrySetResult();
        await publish.WaitAsync(TimeSpan.FromSeconds(5));           // completes once all delivered
    }

    [Fact]
    public async Task PublishAsync_Untyped_SharesSingleEncoding_AcrossSharedEncodingObservers()
    {
        var registry = new StreamSubscriptionRegistry();
        StreamId streamId = StreamId.Create("ns", "key");

        int encodeCalls = 0;
        ReadOnlyMemory<byte> Encode(object o)
        {
            Interlocked.Increment(ref encodeCalls);
            return new byte[] { 7 };
        }

        var received = new List<SharedStreamItem>();
        registry.SubscribeUntyped(streamId, new RecordingSharedObserver(received, Encode));
        registry.SubscribeUntyped(streamId, new RecordingSharedObserver(received, Encode));

        await registry.PublishAsync(streamId, "payload", null);

        Assert.Equal(2, received.Count);
        Assert.Same(received[0], received[1]); // one SharedStreamItem shared across observers
        Assert.Equal(1, encodeCalls);          // encoded exactly once
    }

    [Fact]
    public async Task PublishAsync_Untyped_DeliversRawItemToPlainObserver_AndSharedToEncodingObserver()
    {
        var registry = new StreamSubscriptionRegistry();
        StreamId streamId = StreamId.Create("ns", "key");

        object? plainReceived = null;
        var plain = new PlainObserver(item => plainReceived = item);

        var sharedReceived = new List<SharedStreamItem>();
        var shared = new RecordingSharedObserver(sharedReceived, _ => new byte[] { 9 });

        registry.SubscribeUntyped(streamId, plain);
        registry.SubscribeUntyped(streamId, shared);

        await registry.PublishAsync(streamId, "payload", null);

        Assert.Equal("payload", plainReceived);          // plain observer receives the raw object
        Assert.Single(sharedReceived);
        Assert.Equal("payload", sharedReceived[0].Item); // shared observer receives the wrapped item
    }

    private sealed class GateObserver<T>(Func<T, Task> onNext) : IAsyncObserver<T>
    {
        public async ValueTask OnNextAsync(T item, StreamSequenceToken? token = null) => await onNext(item);
        public ValueTask OnErrorAsync(Exception ex) => ValueTask.CompletedTask;
        public ValueTask OnCompletedAsync() => ValueTask.CompletedTask;
    }

    private sealed class RecordingSharedObserver(
        List<SharedStreamItem> received,
        Func<object, ReadOnlyMemory<byte>> encode) : ISharedEncodingStreamObserver
    {
        public Task OnNextSharedAsync(SharedStreamItem item, StreamSequenceToken? token)
        {
            lock (received) { received.Add(item); }
            _ = item.GetOrEncode(encode);
            return Task.CompletedTask;
        }
        public Task OnNextAsync(object item, StreamSequenceToken? token)
            => OnNextSharedAsync(new SharedStreamItem(item), token);
        public Task OnErrorAsync(Exception ex) => Task.CompletedTask;
        public Task OnCompletedAsync() => Task.CompletedTask;
    }

    private sealed class PlainObserver(Action<object> onNext) : IUntypedStreamObserver
    {
        public Task OnNextAsync(object item, StreamSequenceToken? token)
        {
            onNext(item);
            return Task.CompletedTask;
        }
        public Task OnErrorAsync(Exception ex) => Task.CompletedTask;
        public Task OnCompletedAsync() => Task.CompletedTask;
    }
}
