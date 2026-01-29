// Copyright (c) Quark Framework. All rights reserved.

using System.Collections.Concurrent;

namespace Quark.Abstractions.Streaming;

/// <summary>
/// Registry for AOT-safe stream consumer dispatchers.
/// Populated at compile-time by the StreamSourceGenerator.
/// </summary>
public static class StreamConsumerDispatcherRegistry
{
    private static readonly ConcurrentDictionary<Type, IStreamConsumerDispatcher> _dispatchers = new();

    /// <summary>
    /// Registers a dispatcher for a specific actor type.
    /// Called by generated code at module initialization.
    /// </summary>
    /// <param name="actorType">The actor type.</param>
    /// <param name="dispatcher">The dispatcher instance.</param>
    public static void RegisterDispatcher(Type actorType, IStreamConsumerDispatcher dispatcher)
    {
        if (actorType == null)
            throw new ArgumentNullException(nameof(actorType));
        
        if (dispatcher == null)
            throw new ArgumentNullException(nameof(dispatcher));

        _dispatchers.TryAdd(actorType, dispatcher);
    }

    /// <summary>
    /// Gets a dispatcher for the specified actor type.
    /// </summary>
    /// <param name="actorType">The actor type.</param>
    /// <returns>The dispatcher, or null if not found.</returns>
    public static IStreamConsumerDispatcher? GetDispatcher(Type actorType)
    {
        _dispatchers.TryGetValue(actorType, out var dispatcher);
        return dispatcher;
    }

    /// <summary>
    /// Clears all registered dispatchers. 
    /// WARNING: For testing purposes only. Do not call this in production code.
    /// </summary>
#if DEBUG
    internal static void Clear()
    {
        _dispatchers.Clear();
    }
#endif
}
