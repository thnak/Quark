namespace Quark.Streaming.Abstractions;

/// <summary>
///     Implemented by grains that use <c>[ImplicitStreamSubscription]</c> to receive
///     subscription lifecycle notifications.
/// </summary>
public interface IStreamSubscriptionObserver // TODO did not implemented or used in any elsewhere
{
    Task OnSubscribed(IStreamSubscriptionHandleFactory handleFactory);
}
