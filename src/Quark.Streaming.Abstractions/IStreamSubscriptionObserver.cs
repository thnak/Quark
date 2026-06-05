namespace Quark.Streaming.Abstractions;

/// <summary>
///     Implemented by grains that use <c>[ImplicitStreamSubscription]</c> to receive
///     subscription lifecycle notifications.
/// </summary>
public interface IStreamSubscriptionObserver
{
    Task OnSubscribed(IStreamSubscriptionHandleFactory handleFactory);
}

public interface IStreamSubscriptionHandleFactory
{
    StreamSubscriptionHandle<T> Create<T>(IAsyncObserver<T> observer);
}
