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
    private readonly ImplicitStreamSubscriptionRegistry? _implicitRegistry;
    private readonly IImplicitStreamActivator? _implicitActivator;

    public StreamSubscriptionRegistry(
        ImplicitStreamSubscriptionRegistry? implicitRegistry = null,
        IImplicitStreamActivator? implicitActivator = null)
    {
        _implicitRegistry = implicitRegistry;
        _implicitActivator = implicitActivator;
    }

    public Guid Subscribe<T>(StreamId streamId, IAsyncObserver<T> observer)
    {
        List<Subscription> list = _subs.GetOrAdd(streamId, _ => []);
        var sub = new Subscription
        {
            Id = Guid.NewGuid(),
            OnNext = (item, token) => observer.OnNextAsync((T)item, token),
            OnError = observer.OnErrorAsync,
            OnCompleted = observer.OnCompletedAsync
        };
        lock (list)
        {
            list.Add(sub);
        }

        return sub.Id;
    }

    public void Unsubscribe(StreamId streamId, Guid subscriptionId)
    {
        if (!_subs.TryGetValue(streamId, out List<Subscription>? list))
        {
            return;
        }

        lock (list)
        {
            list.RemoveAll(s => s.Id == subscriptionId);
        }
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
        if (!_untypedIndex.TryRemove(subId, out StreamId streamId))
        {
            return;
        }

        if (_untyped.TryGetValue(streamId, out List<(Guid SubId, IUntypedStreamObserver Observer)>? list))
        {
            lock (list) { list.RemoveAll(x => x.SubId == subId); }
        }
    }

    public async Task PublishAsync<T>(StreamId streamId, T item, StreamSequenceToken? token)
    {
        // Ensure implicitly-subscribed grains are activated before fanning out so the
        // first published item is delivered to a freshly-activated grain in the same call.
        if (_implicitRegistry is not null && _implicitActivator is not null
            && _implicitRegistry.TryGetGrainTypes(streamId.Namespace, out IReadOnlyList<string> grainTypeKeys))
        {
            foreach (string grainTypeKey in grainTypeKeys)
                await _implicitActivator.EnsureActivatedAsync(grainTypeKey, streamId.Key).ConfigureAwait(false);
        }

        if (_subs.TryGetValue(streamId, out List<Subscription>? list))
        {
            List<Subscription> snapshot;
            lock (list)
            {
                snapshot = [..list];
            }

            List<Exception>? errors = null;
            foreach (Subscription sub in snapshot)
            {
                try { await sub.OnNext(item!, token).ConfigureAwait(false); }
                catch (Exception ex) { (errors ??= []).Add(ex); }
            }
            if (errors is { Count: > 0 })
            {
                throw new AggregateException(errors);
            }
        }

        if (_untyped.TryGetValue(streamId, out List<(Guid SubId, IUntypedStreamObserver Observer)>? untypedList))
        {
            List<(Guid, IUntypedStreamObserver)> snapshot;
            lock (untypedList) { snapshot = [..untypedList]; }

            List<Exception>? untypedErrors = null;
            foreach (var (_, obs) in snapshot)
            {
                try { await obs.OnNextAsync(item!, token).ConfigureAwait(false); }
                catch (Exception ex) { (untypedErrors ??= []).Add(ex); }
            }
            if (untypedErrors is { Count: > 0 })
            {
                throw new AggregateException(untypedErrors);
            }
        }
    }

    public async Task PublishErrorAsync(StreamId streamId, Exception ex)
    {
        if (!_subs.TryGetValue(streamId, out List<Subscription>? list))
        {
            return;
        }

        List<Subscription> snapshot;
        lock (list)
        {
            snapshot = [..list];
        }

        List<Exception>? errors = null;
        foreach (Subscription sub in snapshot)
        {
            try { await sub.OnError(ex).ConfigureAwait(false); }
            catch (Exception e) { (errors ??= []).Add(e); }
        }
        if (errors is { Count: > 0 }) throw new AggregateException(errors);
    }

    public async Task PublishCompletedAsync(StreamId streamId)
    {
        if (!_subs.TryGetValue(streamId, out List<Subscription>? list))
        {
            return;
        }

        List<Subscription> snapshot;
        lock (list)
        {
            snapshot = [..list];
        }

        List<Exception>? errors = null;
        foreach (Subscription sub in snapshot)
        {
            try { await sub.OnCompleted().ConfigureAwait(false); }
            catch (Exception ex) { (errors ??= []).Add(ex); }
        }
        if (errors is { Count: > 0 })
        {
            throw new AggregateException(errors);
        }
    }
}
