namespace Quark.Streaming.Abstractions;

/// <summary>A pub/sub stream. Drop-in equivalent of Orleans' <c>IAsyncStream&lt;T&gt;</c>.</summary>
public interface IAsyncStream<T>
{
    StreamId StreamId { get; }

    Task OnNextAsync(T item, StreamSequenceToken? token = null);
    Task OnErrorAsync(Exception ex);
    Task OnCompletedAsync();

    Task<StreamSubscriptionHandle<T>> SubscribeAsync(IAsyncObserver<T> observer);

    Task<StreamSubscriptionHandle<T>> SubscribeAsync(
        Func<T, StreamSequenceToken?, Task> onNext,
        Func<Exception, Task>? onError = null,
        Func<Task>? onCompleted = null);

    Task<IList<StreamSubscriptionHandle<T>>> GetAllSubscriptionHandles();
}
