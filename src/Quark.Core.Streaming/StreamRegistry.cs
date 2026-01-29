// Copyright (c) Quark Framework. All rights reserved.

using System.Collections.Concurrent;

namespace Quark.Core.Streaming;

/// <summary>
/// Global registry for stream-to-actor mappings.
/// Used by the source generator to register implicit subscriptions.
/// </summary>
public static class StreamRegistry
{
    private static StreamBroker? _globalBroker;
    private static readonly ConcurrentQueue<DeferredRegistration> _deferredRegistrations = new();

    /// <summary>
    /// Sets the global stream broker instance.
    /// IMPORTANT: This should be called during application startup, before any
    /// module initializers run that register stream subscriptions.
    /// If called after registrations, any deferred registrations will be processed immediately.
    /// </summary>
    /// <param name="broker">The broker to use for stream registrations.</param>
    public static void SetBroker(StreamBroker broker)
    {
        _globalBroker = broker ?? throw new ArgumentNullException(nameof(broker));
        
        // Process any deferred registrations that arrived before the broker was set
        while (_deferredRegistrations.TryDequeue(out var registration))
        {
            _globalBroker.RegisterImplicitSubscription(
                registration.Namespace,
                registration.ActorType,
                registration.MessageType);
        }
    }

    /// <summary>
    /// Registers an implicit subscription for a stream namespace.
    /// Called by the source generator during module initialization.
    /// 
    /// If the broker is not yet set, the registration is queued and will be
    /// processed when SetBroker() is called.
    /// </summary>
    /// <param name="namespace">The stream namespace.</param>
    /// <param name="actorType">The actor type that subscribes to this namespace.</param>
    /// <param name="messageType">The message type for this stream.</param>
    public static void RegisterImplicitSubscription(string @namespace, Type actorType, Type messageType)
    {
        if (_globalBroker == null)
        {
            // Broker not yet set - defer registration until SetBroker is called
            _deferredRegistrations.Enqueue(new DeferredRegistration(@namespace, actorType, messageType));
            return;
        }

        _globalBroker.RegisterImplicitSubscription(@namespace, actorType, messageType);
    }

    /// <summary>
    /// Gets the global broker instance.
    /// </summary>
    public static StreamBroker? GetBroker() => _globalBroker;

    private record DeferredRegistration(string Namespace, Type ActorType, Type MessageType);
}
