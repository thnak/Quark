// Copyright (c) Quark Framework. All rights reserved.

using System.Collections.Concurrent;
using Quark.Abstractions.Streaming;

namespace Quark.Core.Streaming;

/// <summary>
/// In-memory implementation of a stream handle.
/// Phase 8.5: Enhanced with backpressure and flow control support.
/// </summary>
/// <typeparam name="T">The type of messages in the stream.</typeparam>
internal class StreamHandle<T> : IStreamHandle<T>
{
    private readonly StreamBroker _broker;
    private readonly ConcurrentDictionary<Guid, Func<T, Task>> _subscribers = new();
    private readonly IStreamBackpressureStrategy<T> _backpressureStrategy;
    private readonly Task _processingTask;
    private readonly CancellationTokenSource _cts = new();

    public StreamHandle(StreamId streamId, StreamBroker broker, StreamBackpressureOptions? backpressureOptions = null)
    {
        StreamId = streamId;
        _broker = broker ?? throw new ArgumentNullException(nameof(broker));
        
        // Create backpressure strategy
        backpressureOptions ??= new StreamBackpressureOptions { Mode = BackpressureMode.None };
        _backpressureStrategy = BackpressureStrategyFactory.Create<T>(backpressureOptions);

        // Start background processing task if backpressure is enabled
        if (backpressureOptions.Mode != BackpressureMode.None)
        {
            _processingTask = Task.Run(() => ProcessMessagesAsync(_cts.Token));
        }
        else
        {
            _processingTask = Task.CompletedTask;
        }
    }

    /// <inheritdoc/>
    public StreamId StreamId { get; }

    /// <inheritdoc/>
    public StreamBackpressureMetrics? BackpressureMetrics => _backpressureStrategy.Metrics;

    /// <inheritdoc/>
    public async Task PublishAsync(T message, CancellationToken cancellationToken = default)
    {
        // With backpressure, enqueue message for async processing
        if (_backpressureStrategy is not NoBackpressureStrategy<T>)
        {
            await _backpressureStrategy.TryPublishAsync(message, cancellationToken);
        }
        else
        {
            // No backpressure - publish immediately (original behavior)
            await DeliverMessageAsync(message, cancellationToken);
        }
    }

    private async Task ProcessMessagesAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // Try to dequeue next message
                var message = await _backpressureStrategy.TryDequeueAsync(cancellationToken);
                
                if (message != null)
                {
                    await DeliverMessageAsync(message, cancellationToken);
                }
                else
                {
                    // No messages available, wait a bit
                    await Task.Delay(10, cancellationToken);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception)
            {
                // Log error but continue processing
                // In a production system, this should use proper logging
            }
        }
    }

    private async Task DeliverMessageAsync(T message, CancellationToken cancellationToken)
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
