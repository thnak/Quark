// Copyright (c) Quark Framework. All rights reserved.

using System.Collections.Concurrent;
using Quark.Abstractions;
using Quark.Abstractions.Streaming;

namespace Quark.Core.Streaming;

/// <summary>
/// Central broker for managing stream subscriptions and message delivery.
/// Handles both explicit subscriptions and implicit actor activations.
/// </summary>
public class StreamBroker
{
    private readonly ConcurrentDictionary<string, List<StreamSubscription>> _implicitSubscriptions = new();
    private readonly IActorFactory? _actorFactory;

    public StreamBroker(IActorFactory? actorFactory = null)
    {
        _actorFactory = actorFactory;
    }

    /// <summary>
    /// Registers an implicit subscription for a stream namespace.
    /// </summary>
    /// <param name="namespace">The stream namespace.</param>
    /// <param name="actorType">The actor type that subscribes to this namespace.</param>
    /// <param name="messageType">The message type for this stream.</param>
    public void RegisterImplicitSubscription(string @namespace, Type actorType, Type messageType)
    {
        if (string.IsNullOrWhiteSpace(@namespace))
            throw new ArgumentException("Namespace cannot be null or empty.", nameof(@namespace));
        
        if (actorType == null)
            throw new ArgumentNullException(nameof(actorType));
        
        if (messageType == null)
            throw new ArgumentNullException(nameof(messageType));

        var subscription = new StreamSubscription(@namespace, actorType, messageType);
        
        _implicitSubscriptions.AddOrUpdate(
            @namespace,
            _ => new List<StreamSubscription> { subscription },
            (_, existingList) =>
            {
                lock (existingList)
                {
                    existingList.Add(subscription);
                }
                return existingList;
            });
    }

    /// <summary>
    /// Notifies all implicit subscribers when a message is published to a stream.
    /// </summary>
    internal async Task NotifyImplicitSubscribersAsync<T>(
        StreamId streamId,
        T message,
        CancellationToken cancellationToken = default)
    {
        if (!_implicitSubscriptions.TryGetValue(streamId.Namespace, out var subscriptions))
            return;

        var tasks = new List<Task>();
        
        foreach (var subscription in subscriptions)
        {
            // Only process if message type matches
            if (!subscription.MessageType.IsAssignableFrom(typeof(T)))
                continue;

            // For now, we'll use a simple activation strategy
            // In a full implementation, this would use the placement logic
            tasks.Add(ActivateAndNotifyActorAsync(subscription.ActorType, streamId, message, cancellationToken));
        }

        await Task.WhenAll(tasks);
    }

    private async Task ActivateAndNotifyActorAsync<T>(
        Type actorType,
        StreamId streamId,
        T message,
        CancellationToken cancellationToken)
    {
        if (_actorFactory == null)
            return;

        try
        {
            // Use AOT-safe dispatcher instead of reflection
            var dispatcher = StreamConsumerDispatcherRegistry.GetDispatcher(actorType);
            
            if (dispatcher == null)
            {
                // TODO: Add logging infrastructure and log warning
                // Dispatcher not found - likely the actor doesn't have [QuarkStream] attribute
                // or the source generator hasn't run yet
                return;
            }

            // Create or get the actor instance using the stream key as the actor ID
            var actorId = streamId.Key;
            
            // Use the dispatcher to activate and notify the actor - NO REFLECTION
            await dispatcher.ActivateAndNotifyAsync(_actorFactory, actorId, message, streamId, cancellationToken);
        }
        catch (Exception ex)
        {
            // TODO: Add logging infrastructure and log errors
            // For now, we swallow exceptions to prevent one failing actor
            // from breaking the entire stream delivery mechanism
            // In production, this should be logged with: actor type, stream ID, message type, and exception details
            _ = ex; // Acknowledge the variable to avoid unused warnings
        }
    }

    private record StreamSubscription(string Namespace, Type ActorType, Type MessageType);
}
