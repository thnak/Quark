using System.Collections.Concurrent;
using Quark.Streaming.Abstractions;

namespace Quark.Streaming.InMemory;

public sealed class StreamSubscriptionRegistry : IUntypedStreamSubscriptionRegistry
{
    private sealed class Subscription
    {
        public required Guid Id { get; init; }
        public required Func<object, StreamSequenceToken?, ValueTask> OnNext { get; init; }
        public required Func<Exception, ValueTask> OnError { get; init; }
        public required Func<ValueTask> OnCompleted { get; init; }
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
        List<Subscription> list = _subs.GetOrAdd(streamId, ValueFactory);

        var sub = new Subscription
        {
            Id = Guid.NewGuid(),
            OnNext = OnNext,
            OnError = observer.OnErrorAsync,
            OnCompleted = observer.OnCompletedAsync
        };
        lock (list)
        {
            list.Add(sub);
        }

        return sub.Id;

        ValueTask OnNext(object item, StreamSequenceToken? token) => observer.OnNextAsync((T)item, token);
    }

    private static List<Subscription> ValueFactory(StreamId _)
    {
        return [];
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
            AddValueFactory,
            UpdateValueFactory);
        _untypedIndex[subId] = streamId;
        return subId;

        List<(Guid SubId, IUntypedStreamObserver Observer)> AddValueFactory(StreamId _) => [(subId, observer)];

        List<(Guid SubId, IUntypedStreamObserver Observer)> UpdateValueFactory(StreamId _, List<(Guid SubId, IUntypedStreamObserver Observer)> list)
        {
            lock (list)
            {
                list.Add((subId, observer));
            }

            return list;
        }
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

    private static async Task FanOutAsync(List<Task> tasks)
    {
        Task all = Task.WhenAll(tasks);
        try
        {
            await all.ConfigureAwait(false);
        }
        catch when (all.Exception is not null)
        {
            // Task.WhenAll aggregates every failure into all.Exception; awaiting surfaces only the first.
            // Rethrow the flattened AggregateException to preserve the prior all-failures contract.
            throw all.Exception;
        }
    }

    public async ValueTask PublishAsync<T>(StreamId streamId, T item, StreamSequenceToken? token)
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

            var tasks = new List<Task>(snapshot.Count);
            foreach (Subscription sub in snapshot)
            {
                Task task;
                // Convert a synchronous throw into a faulted task so every subscriber is still invoked
                // and the failure is aggregated by FanOutAsync (matches the prior per-subscriber try/catch).
                try { task = sub.OnNext(item!, token).AsTask(); }
                catch (Exception ex) { task = Task.FromException(ex); }
                tasks.Add(task);
            }

            await FanOutAsync(tasks).ConfigureAwait(false);
        }

        if (_untyped.TryGetValue(streamId, out List<(Guid SubId, IUntypedStreamObserver Observer)>? untypedList))
        {
            List<(Guid, IUntypedStreamObserver)> snapshot;
            lock (untypedList) { snapshot = [..untypedList]; }

            var shared = new SharedStreamItem(item!);
            var tasks = new List<Task>(snapshot.Count);
            foreach (var (_, obs) in snapshot)
            {
                Task task;
                try
                {
                    task = obs is ISharedEncodingStreamObserver enc
                        ? enc.OnNextSharedAsync(shared, token)
                        : obs.OnNextAsync(item!, token);
                }
                catch (Exception ex) { task = Task.FromException(ex); }
                tasks.Add(task);
            }

            await FanOutAsync(tasks).ConfigureAwait(false);
        }
    }

    public async ValueTask PublishErrorAsync(StreamId streamId, Exception ex)
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

        var tasks = new List<Task>(snapshot.Count);
        foreach (Subscription sub in snapshot)
        {
            Task task;
            try { task = sub.OnError(ex).AsTask(); }
            catch (Exception e) { task = Task.FromException(e); }
            tasks.Add(task);
        }
        await FanOutAsync(tasks).ConfigureAwait(false);
    }

    public async ValueTask PublishCompletedAsync(StreamId streamId)
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

        var tasks = new List<Task>(snapshot.Count);
        foreach (Subscription sub in snapshot)
        {
            Task task;
            try { task = sub.OnCompleted().AsTask(); }
            catch (Exception ex) { task = Task.FromException(ex); }
            tasks.Add(task);
        }
        await FanOutAsync(tasks).ConfigureAwait(false);
    }
}
