namespace Quark.Streaming.Abstractions;

public interface IStreamSubscriptionHandleFactory
{
    StreamSubscriptionHandle<T> Create<T>(IAsyncObserver<T> observer);
}