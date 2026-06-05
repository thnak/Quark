using System.Collections.Concurrent;
using Quark.Streaming.Abstractions;

namespace Quark.Streaming.InMemory;

internal sealed class StreamSubscriptionRegistry
{
    private sealed class Subscription
    {
        public required Guid Id { get; init; }
        public required Func<object, StreamSequenceToken?, Task> OnNext { get; init; }
        public required Func<Exception, Task> OnError { get; init; }
        public required Func<Task> OnCompleted { get; init; }
    }

    private readonly ConcurrentDictionary<StreamId, List<Subscription>> _subs = new();

    public Guid Subscribe<T>(StreamId streamId, IAsyncObserver<T> observer)
    {
        var list = _subs.GetOrAdd(streamId, _ => []);
        var sub = new Subscription
        {
            Id = Guid.NewGuid(),
            OnNext = (item, token) => observer.OnNextAsync((T)item, token),
            OnError = ex => observer.OnErrorAsync(ex),
            OnCompleted = () => observer.OnCompletedAsync()
        };
        lock (list) list.Add(sub);
        return sub.Id;
    }

    public void Unsubscribe(StreamId streamId, Guid subscriptionId)
    {
        if (!_subs.TryGetValue(streamId, out var list)) return;
        lock (list) list.RemoveAll(s => s.Id == subscriptionId);
    }

    public async Task PublishAsync<T>(StreamId streamId, T item, StreamSequenceToken? token)
    {
        if (!_subs.TryGetValue(streamId, out var list)) return;
        List<Subscription> snapshot;
        lock (list) snapshot = [..list];
        foreach (var sub in snapshot)
            await sub.OnNext(item!, token).ConfigureAwait(false);
    }

    public async Task PublishErrorAsync(StreamId streamId, Exception ex)
    {
        if (!_subs.TryGetValue(streamId, out var list)) return;
        List<Subscription> snapshot;
        lock (list) snapshot = [..list];
        foreach (var sub in snapshot)
            await sub.OnError(ex).ConfigureAwait(false);
    }

    public async Task PublishCompletedAsync(StreamId streamId)
    {
        if (!_subs.TryGetValue(streamId, out var list)) return;
        List<Subscription> snapshot;
        lock (list) snapshot = [..list];
        foreach (var sub in snapshot)
            await sub.OnCompleted().ConfigureAwait(false);
    }
}
