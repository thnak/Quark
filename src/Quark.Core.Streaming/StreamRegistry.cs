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
    /// IMPORTANT: This should be called during application startup, before any
    /// module initializers run that register stream subscriptions.
    /// </summary>
    /// <param name="broker">The broker to use for stream registrations.</param>
    public static void SetBroker(StreamBroker broker)
    {
        _globalBroker = broker ?? throw new ArgumentNullException(nameof(broker));
    }

    /// <summary>
    /// Registers an implicit subscription for a stream namespace.
    /// Called by the source generator during module initialization.
    /// 
    /// NOTE: If called before SetBroker(), the registration is silently ignored.
    /// Ensure SetBroker() is called during application startup before any
    /// stream subscriptions are registered.
    /// </summary>
    /// <param name="namespace">The stream namespace.</param>
    /// <param name="actorType">The actor type that subscribes to this namespace.</param>
    /// <param name="messageType">The message type for this stream.</param>
    public static void RegisterImplicitSubscription(string @namespace, Type actorType, Type messageType)
    {
        // If no broker is set yet, registration is skipped
        // TODO: Consider implementing a deferred registration queue for subscriptions
        // that arrive before the broker is initialized
        if (_globalBroker == null)
        {
            return;
        }

        _globalBroker.RegisterImplicitSubscription(@namespace, actorType, messageType);
    }

    /// <summary>
    /// Gets the global broker instance.
    /// </summary>
    public static StreamBroker? GetBroker() => _globalBroker;
}
