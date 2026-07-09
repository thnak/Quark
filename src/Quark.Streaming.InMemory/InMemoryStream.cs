using Quark.Streaming.Abstractions;

namespace Quark.Streaming.InMemory;

internal sealed class InMemoryStream<T> : IAsyncStream<T>
{
    private readonly StreamSubscriptionRegistry _registry;
    private readonly List<InMemorySubscriptionHandle<T>> _handles = [];
    private long _sequence;

    public InMemoryStream(StreamId streamId, StreamSubscriptionRegistry registry)
    {
        StreamId = streamId;
        _registry = registry;
    }

    public StreamId StreamId { get; }

    public ValueTask OnNextAsync(T item, StreamSequenceToken? token = null)
    {
        var seq = new SequentialToken(Interlocked.Increment(ref _sequence));
        return _registry.PublishAsync(StreamId, item, token ?? seq);
    }

    public ValueTask OnErrorAsync(Exception ex) => _registry.PublishErrorAsync(StreamId, ex);
    public ValueTask OnCompletedAsync() => _registry.PublishCompletedAsync(StreamId);

    public ValueTask<StreamSubscriptionHandle<T>> SubscribeAsync(IAsyncObserver<T> observer)
    {
        var subscriptionId = _registry.Subscribe(StreamId, observer);
        var handle = new InMemorySubscriptionHandle<T>(
            subscriptionId, StreamId, _registry,
            OnUnsubscribe);
        lock (_handles)
        {
            _handles.Add(handle);
        }

        return ValueTask.FromResult<StreamSubscriptionHandle<T>>(handle);
    }

    private void OnUnsubscribe(Guid id)
    {
        lock (_handles)
        {
            _handles.RemoveAll(h => h.HandleId == id);
        }
    }

    public ValueTask<StreamSubscriptionHandle<T>> SubscribeAsync(
        Func<T, StreamSequenceToken?, ValueTask> onNext,
        Func<Exception, ValueTask>? onError = null,
        Func<ValueTask>? onCompleted = null)
        => SubscribeAsync(new DelegateObserver<T>(onNext, onError, onCompleted));

    public ValueTask<StreamSubscriptionHandle<T>> SubscribeAsync<TContext>(
        TContext context,
        Func<TContext, T, StreamSequenceToken?, ValueTask> onNext, Func<Exception, ValueTask>? onError = null,
        Func<ValueTask>? onCompleted = null)
        => SubscribeAsync(new DelegateContextObserver<T, TContext>(context, onNext, onError, onCompleted));

    public ValueTask<IList<StreamSubscriptionHandle<T>>> GetAllSubscriptionHandles()
    {
        lock (_handles)
        {
            return ValueTask.FromResult<IList<StreamSubscriptionHandle<T>>>([.._handles]);
        }
    }
}
