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
            (_, list) =>
            {
                list.Add(subscription);
                return list;
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
            // Create or get the actor instance
            // For implicit subscriptions, we use the stream key as the actor ID
            var actorId = streamId.Key;
            
            // Use reflection to call GetOrCreateActor<TActorType>
            var method = _actorFactory.GetType()
                .GetMethod(nameof(IActorFactory.GetOrCreateActor))
                ?.MakeGenericMethod(actorType);
            
            if (method == null)
                return;

            var actor = method.Invoke(_actorFactory, new object[] { actorId });
            
            if (actor == null)
                return;

            // Check if actor implements IStreamConsumer<T>
            var consumerInterface = actor.GetType()
                .GetInterfaces()
                .FirstOrDefault(i => i.IsGenericType && 
                                     i.GetGenericTypeDefinition() == typeof(IStreamConsumer<>) &&
                                     i.GetGenericArguments()[0].IsAssignableFrom(typeof(T)));
            
            if (consumerInterface != null)
            {
                var onStreamMessageMethod = consumerInterface.GetMethod(nameof(IStreamConsumer<T>.OnStreamMessageAsync));
                if (onStreamMessageMethod != null)
                {
                    var task = onStreamMessageMethod.Invoke(actor, new object?[] { message, streamId, cancellationToken }) as Task;
                    if (task != null)
                        await task;
                }
            }
        }
        catch
        {
            // Log error in production - for now, swallow
        }
    }

    private record StreamSubscription(string Namespace, Type ActorType, Type MessageType);
}
