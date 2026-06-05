using System.Collections.Concurrent;
using Quark.Streaming.Abstractions;

namespace Quark.Streaming.InMemory;

internal sealed class StreamSubscriptionRegistry
{
    private readonly ConcurrentDictionary<StreamId, List<(Guid Id, Func<object, StreamSequenceToken?, Task> Handler)>> _subs = new();

    public Guid Subscribe<T>(StreamId streamId, IAsyncObserver<T> observer)
    {
        var list = _subs.GetOrAdd(streamId, _ => []);
        var id = Guid.NewGuid();
        Func<object, StreamSequenceToken?, Task> handler = (item, token) =>
            observer.OnNextAsync((T)item, token);
        lock (list) list.Add((id, handler));
        return id;
    }

    public void Unsubscribe(StreamId streamId, Guid subscriptionId)
    {
        if (!_subs.TryGetValue(streamId, out var list)) return;
        lock (list) list.RemoveAll(s => s.Id == subscriptionId);
    }

    public async Task PublishAsync<T>(StreamId streamId, T item, StreamSequenceToken? token)
    {
        if (!_subs.TryGetValue(streamId, out var list)) return;
        List<(Guid, Func<object, StreamSequenceToken?, Task>)> snapshot;
        lock (list) snapshot = [..list];
        foreach (var (_, handler) in snapshot)
            await handler(item!, token).ConfigureAwait(false);
    }
}
