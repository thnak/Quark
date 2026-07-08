using Quark.Streaming.Abstractions;
using Quark.Streaming.InMemory;
using Xunit;

namespace Quark.Tests.Unit.Streaming;

public class UntypedSubscriptionTests
{
    [Fact]
    public async Task SubscribeUntyped_ReceivesPublishedItem()
    {
        var registry = new StreamSubscriptionRegistry();
        var streamId = StreamId.Create("ns", "key");
        var received = new List<object>();
        var observer = new TestUntypedObserver(item => received.Add(item));

        registry.SubscribeUntyped(streamId, observer);
        await registry.PublishAsync(streamId, "hello", null);

        Assert.Single(received);
        Assert.Equal("hello", received[0]);
    }

    [Fact]
    public async Task UnsubscribeUntyped_StopsDelivery()
    {
        var registry = new StreamSubscriptionRegistry();
        var streamId = StreamId.Create("ns", "key");
        var received = new List<object>();
        var observer = new TestUntypedObserver(item => received.Add(item));

        var subId = registry.SubscribeUntyped(streamId, observer);
        registry.UnsubscribeUntyped(subId);
        await registry.PublishAsync(streamId, "hello", null);

        Assert.Empty(received);
    }

    [Fact]
    public async Task PublishAsync_UntypedObserverThrows_StillDeliversToRemainingSubscribers()
    {
        var registry = new StreamSubscriptionRegistry();
        var streamId = StreamId.Create("ns", "key");
        var received = new List<object>();
        var throwingObserver = new TestUntypedObserver(_ => throw new InvalidOperationException("boom"));
        var healthyObserver = new TestUntypedObserver(item => received.Add(item));

        registry.SubscribeUntyped(streamId, throwingObserver);
        registry.SubscribeUntyped(streamId, healthyObserver);

        var ex = await Assert.ThrowsAsync<AggregateException>(
            async () => await registry.PublishAsync(streamId, "hello", null));

        Assert.Single(received);
        Assert.Equal("hello", received[0]);
        Assert.Single(ex.InnerExceptions);
        Assert.IsType<InvalidOperationException>(ex.InnerExceptions[0]);
    }

    private sealed class TestUntypedObserver(Action<object> onNext) : IUntypedStreamObserver
    {
        public Task OnNextAsync(object item, StreamSequenceToken? token) { onNext(item); return Task.CompletedTask; }
        public Task OnErrorAsync(Exception ex) => Task.CompletedTask;
        public Task OnCompletedAsync() => Task.CompletedTask;
    }
}
