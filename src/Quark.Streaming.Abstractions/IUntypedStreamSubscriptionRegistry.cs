namespace Quark.Streaming.Abstractions;

/// <summary>
///     Registry for untyped stream subscriptions. Allows the TCP gateway to manage
///     stream subscriptions without knowing the item type T at compile time.
/// </summary>
public interface IUntypedStreamSubscriptionRegistry
{
    Guid SubscribeUntyped(StreamId streamId, IUntypedStreamObserver observer);
    void UnsubscribeUntyped(Guid subId);
}
