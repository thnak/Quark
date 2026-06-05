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

    public Task OnNextAsync(T item, StreamSequenceToken? token = null)
    {
        var seq = new SequentialToken(Interlocked.Increment(ref _sequence));
        return _registry.PublishAsync(StreamId, item, token ?? seq);
    }

    public Task OnErrorAsync(Exception ex) => _registry.PublishErrorAsync(StreamId, ex);
    public Task OnCompletedAsync() => _registry.PublishCompletedAsync(StreamId);

    public Task<StreamSubscriptionHandle<T>> SubscribeAsync(IAsyncObserver<T> observer)
    {
        var subscriptionId = _registry.Subscribe(StreamId, observer);
        var handle = new InMemorySubscriptionHandle<T>(
            subscriptionId, StreamId, _registry,
            id => { lock (_handles) _handles.RemoveAll(h => h.HandleId == id); });
        lock (_handles) _handles.Add(handle);
        return Task.FromResult<StreamSubscriptionHandle<T>>(handle);
    }

    public Task<StreamSubscriptionHandle<T>> SubscribeAsync(
        Func<T, StreamSequenceToken?, Task> onNext,
        Func<Exception, Task>? onError = null,
        Func<Task>? onCompleted = null)
        => SubscribeAsync(new DelegateObserver<T>(onNext, onError, onCompleted));

    public Task<IList<StreamSubscriptionHandle<T>>> GetAllSubscriptionHandles()
    {
        lock (_handles) return Task.FromResult<IList<StreamSubscriptionHandle<T>>>([.._handles]);
    }
}

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

    public override Task ResumeAsync(IAsyncObserver<T> observer, StreamSequenceToken? token = null)
        => throw new NotSupportedException("InMemory stream subscriptions cannot be resumed; create a new subscription instead.");
}

internal sealed class DelegateObserver<T> : IAsyncObserver<T>
{
    private readonly Func<T, StreamSequenceToken?, Task> _onNext;
    private readonly Func<Exception, Task>? _onError;
    private readonly Func<Task>? _onCompleted;

    public DelegateObserver(
        Func<T, StreamSequenceToken?, Task> onNext,
        Func<Exception, Task>? onError,
        Func<Task>? onCompleted)
    {
        _onNext = onNext;
        _onError = onError;
        _onCompleted = onCompleted;
    }

    public Task OnNextAsync(T item, StreamSequenceToken? token = null) => _onNext(item, token);
    public Task OnErrorAsync(Exception ex) => _onError?.Invoke(ex) ?? Task.CompletedTask;
    public Task OnCompletedAsync() => _onCompleted?.Invoke() ?? Task.CompletedTask;
}
