namespace Quark.Streaming.Abstractions;

/// <summary>A pub/sub stream. Drop-in equivalent of Orleans' <c>IAsyncStream&lt;T&gt;</c>.</summary>
public interface IAsyncStream<T>
{
    StreamId StreamId { get; } // TODO did not implemented or used in any elsewhere

    ValueTask OnNextAsync(T item, StreamSequenceToken? token = null);
    ValueTask OnErrorAsync(Exception ex); // TODO did not implemented or used in any elsewhere
    ValueTask OnCompletedAsync(); // TODO did not implemented or used in any elsewhere

    Task<StreamSubscriptionHandle<T>> SubscribeAsync(IAsyncObserver<T> observer);

    Task<StreamSubscriptionHandle<T>> SubscribeAsync(
        Func<T, StreamSequenceToken?, ValueTask> onNext,
        Func<Exception, ValueTask>? onError = null,
        Func<ValueTask>? onCompleted = null);

    Task<StreamSubscriptionHandle<T>> SubscribeAsync<TContext>(
        TContext context,
        Func<TContext, T, StreamSequenceToken?, ValueTask> onNext,
        Func<Exception, ValueTask>? onError = null,
        Func<ValueTask>? onCompleted = null);

    ValueTask<IList<StreamSubscriptionHandle<T>>>
        GetAllSubscriptionHandles(); // TODO did not implemented or used in any elsewhere
}
