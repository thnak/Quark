using System.Collections.Concurrent;
using Quark.Streaming.Abstractions;

namespace Quark.Streaming.InMemory;

public sealed class StreamSubscriptionRegistry : IUntypedStreamSubscriptionRegistry
{
    private sealed class Subscription
    {
        public required Guid Id { get; init; }
        public required Func<object, StreamSequenceToken?, Task> OnNext { get; init; }
        public required Func<Exception, Task> OnError { get; init; }
        public required Func<Task> OnCompleted { get; init; }
    }

    private readonly ConcurrentDictionary<StreamId, List<Subscription>> _subs = new();
    private readonly ConcurrentDictionary<StreamId, List<(Guid SubId, IUntypedStreamObserver Observer)>> _untyped = new();
    private readonly ConcurrentDictionary<Guid, StreamId> _untypedIndex = new();

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

    public Guid SubscribeUntyped(StreamId streamId, IUntypedStreamObserver observer)
    {
        var subId = Guid.NewGuid();
        _untyped.AddOrUpdate(
            streamId,
            _ => [(subId, observer)],
            (_, list) => { lock (list) { list.Add((subId, observer)); } return list; });
        _untypedIndex[subId] = streamId;
        return subId;
    }

    public void UnsubscribeUntyped(Guid subId)
    {
        if (!_untypedIndex.TryRemove(subId, out var streamId)) return;
        if (_untyped.TryGetValue(streamId, out var list))
            lock (list) { list.RemoveAll(x => x.SubId == subId); }
    }

    public async Task PublishAsync<T>(StreamId streamId, T item, StreamSequenceToken? token)
    {
        if (_subs.TryGetValue(streamId, out var list))
        {
            List<Subscription> snapshot;
            lock (list) snapshot = [..list];
            List<Exception>? errors = null;
            foreach (var sub in snapshot)
            {
                try { await sub.OnNext(item!, token).ConfigureAwait(false); }
                catch (Exception ex) { (errors ??= []).Add(ex); }
            }
            if (errors is { Count: > 0 }) throw new AggregateException(errors);
        }

        if (_untyped.TryGetValue(streamId, out var untypedList))
        {
            List<(Guid, IUntypedStreamObserver)> snapshot;
            lock (untypedList) { snapshot = [..untypedList]; }
            foreach (var (_, obs) in snapshot)
                await obs.OnNextAsync(item!, token).ConfigureAwait(false);
        }
    }

    public async Task PublishErrorAsync(StreamId streamId, Exception ex)
    {
        if (!_subs.TryGetValue(streamId, out var list)) return;
        List<Subscription> snapshot;
        lock (list) snapshot = [..list];
        List<Exception>? errors = null;
        foreach (var sub in snapshot)
        {
            try { await sub.OnError(ex).ConfigureAwait(false); }
            catch (Exception e) { (errors ??= []).Add(e); }
        }
        if (errors is { Count: > 0 }) throw new AggregateException(errors);
    }

    public async Task PublishCompletedAsync(StreamId streamId)
    {
        if (!_subs.TryGetValue(streamId, out var list)) return;
        List<Subscription> snapshot;
        lock (list) snapshot = [..list];
        List<Exception>? errors = null;
        foreach (var sub in snapshot)
        {
            try { await sub.OnCompleted().ConfigureAwait(false); }
            catch (Exception ex) { (errors ??= []).Add(ex); }
        }
        if (errors is { Count: > 0 }) throw new AggregateException(errors);
    }
}
