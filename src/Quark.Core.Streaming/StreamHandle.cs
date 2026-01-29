// Copyright (c) Quark Framework. All rights reserved.

using System.Collections.Concurrent;
using Quark.Abstractions.Streaming;

namespace Quark.Core.Streaming;

/// <summary>
/// In-memory implementation of a stream handle.
/// </summary>
/// <typeparam name="T">The type of messages in the stream.</typeparam>
internal class StreamHandle<T> : IStreamHandle<T>
{
    private readonly StreamBroker _broker;
    private readonly ConcurrentDictionary<Guid, Func<T, Task>> _subscribers = new();

    public StreamHandle(StreamId streamId, StreamBroker broker)
    {
        StreamId = streamId;
        _broker = broker ?? throw new ArgumentNullException(nameof(broker));
    }

    /// <inheritdoc/>
    public StreamId StreamId { get; }

    /// <inheritdoc/>
    public async Task PublishAsync(T message, CancellationToken cancellationToken = default)
    {
        // Publish to all explicit subscribers
        // Note: If any subscriber throws, the exception will propagate and may prevent
        // delivery to remaining subscribers. This is intentional to maintain fail-fast semantics.
        var tasks = _subscribers.Values.Select(handler => handler(message));
        await Task.WhenAll(tasks);

        // Also notify the broker for implicit subscriptions
        await _broker.NotifyImplicitSubscribersAsync(StreamId, message, cancellationToken);
    }

    /// <inheritdoc/>
    public Task<IStreamSubscriptionHandle> SubscribeAsync(
        Func<T, Task> onNext,
        CancellationToken cancellationToken = default)
    {
        var subscriptionId = Guid.NewGuid();
        _subscribers[subscriptionId] = onNext;

        var handle = new StreamSubscriptionHandle(
            StreamId,
            subscriptionId,
            () =>
            {
                _subscribers.TryRemove(subscriptionId, out _);
                return Task.CompletedTask;
            });

        return Task.FromResult<IStreamSubscriptionHandle>(handle);
    }

    /// <summary>
    /// Internal class for managing stream subscriptions.
    /// </summary>
    private class StreamSubscriptionHandle : IStreamSubscriptionHandle
    {
        private readonly Func<Task> _unsubscribeAction;
        private bool _disposed;

        public StreamSubscriptionHandle(
            StreamId streamId,
            Guid subscriptionId,
            Func<Task> unsubscribeAction)
        {
            StreamId = streamId;
            SubscriptionId = subscriptionId;
            _unsubscribeAction = unsubscribeAction ?? throw new ArgumentNullException(nameof(unsubscribeAction));
            IsActive = true;
        }

        public StreamId StreamId { get; }
        public Guid SubscriptionId { get; }
        public bool IsActive { get; private set; }

        public async Task UnsubscribeAsync(CancellationToken cancellationToken = default)
        {
            if (!IsActive)
                return;

            IsActive = false;
            await _unsubscribeAction();
        }

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            // Note: This uses GetAwaiter().GetResult() which could potentially deadlock
            // in certain synchronization contexts. Callers should prefer using
            // UnsubscribeAsync() directly in async contexts.
            UnsubscribeAsync().GetAwaiter().GetResult();
        }
    }
}
