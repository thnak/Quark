// Copyright (c) Quark Framework. All rights reserved.

namespace Quark.Core.Streaming;

/// <summary>
/// Global registry for stream-to-actor mappings.
/// Used by the source generator to register implicit subscriptions.
/// </summary>
public static class StreamRegistry
{
    private static StreamBroker? _globalBroker;

    /// <summary>
    /// Sets the global stream broker instance.
    /// </summary>
    /// <param name="broker">The broker to use for stream registrations.</param>
    public static void SetBroker(StreamBroker broker)
    {
        _globalBroker = broker ?? throw new ArgumentNullException(nameof(broker));
    }

    /// <summary>
    /// Registers an implicit subscription for a stream namespace.
    /// Called by the source generator during module initialization.
    /// </summary>
    /// <param name="namespace">The stream namespace.</param>
    /// <param name="actorType">The actor type that subscribes to this namespace.</param>
    /// <param name="messageType">The message type for this stream.</param>
    public static void RegisterImplicitSubscription(string @namespace, Type actorType, Type messageType)
    {
        // If no broker is set yet, we'll need to defer registration
        // For now, we'll just skip if broker is not available
        if (_globalBroker == null)
        {
            // In a production system, we'd queue these registrations
            return;
        }

        _globalBroker.RegisterImplicitSubscription(@namespace, actorType, messageType);
    }

    /// <summary>
    /// Gets the global broker instance.
    /// </summary>
    public static StreamBroker? GetBroker() => _globalBroker;
}
