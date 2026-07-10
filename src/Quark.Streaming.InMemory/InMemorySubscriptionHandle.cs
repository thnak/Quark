using Quark.Streaming.Abstractions;

namespace Quark.Streaming.InMemory;

internal sealed class InMemorySubscriptionHandle<T> : StreamSubscriptionHandle<T>
{
    private readonly StreamSubscriptionRegistry _registry;
    private readonly Action<Guid> _onUnsubscribe;

    public InMemorySubscriptionHandle(Guid id, StreamId streamId, StreamSubscriptionRegistry registry, Action<Guid> onUnsubscribe)
    {
        HandleId = id;
        StreamId = streamId;
        _registry = registry;
        _onUnsubscribe = onUnsubscribe;
    }

    public override Guid HandleId { get; }
    public override StreamId StreamId { get; }

    public override Task UnsubscribeAsync()
    {
        _registry.Unsubscribe(StreamId, HandleId);
        _onUnsubscribe(HandleId);
        return Task.CompletedTask;
    }
}