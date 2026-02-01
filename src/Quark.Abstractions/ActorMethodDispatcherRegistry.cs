// Copyright (c) Quark Framework. All rights reserved.

using System.Collections.Concurrent;

namespace Quark.Abstractions;

/// <summary>
/// Registry for AOT-safe actor method dispatchers.
/// Populated at compile-time by the ActorSourceGenerator.
/// Follows the same pattern as StreamConsumerDispatcherRegistry.
/// </summary>
public static class ActorMethodDispatcherRegistry
{
    private static readonly ConcurrentDictionary<string, IActorMethodDispatcher> Dispatchers = new();

    /// <summary>
    /// Registers a dispatcher for a specific actor type.
    /// Called by generated code at module initialization.
    /// </summary>
    /// <param name="actorTypeName">The fully qualified name of the actor type.</param>
    /// <param name="dispatcher">The dispatcher instance.</param>
    public static void RegisterDispatcher(string actorTypeName, IActorMethodDispatcher dispatcher)
    {
        if (string.IsNullOrEmpty(actorTypeName))
            throw new ArgumentNullException(nameof(actorTypeName));
        
        if (dispatcher == null)
            throw new ArgumentNullException(nameof(dispatcher));

        Dispatchers.TryAdd(actorTypeName, dispatcher);
    }

    /// <summary>
    /// Gets a dispatcher for the specified actor type name.
    /// </summary>
    /// <param name="actorTypeName">The fully qualified name of the actor type.</param>
    /// <returns>The dispatcher, or null if not found.</returns>
    public static IActorMethodDispatcher? GetDispatcher(string actorTypeName)
    {
        if (string.IsNullOrEmpty(actorTypeName))
            return null;
            
        Dispatchers.TryGetValue(actorTypeName, out var dispatcher);
        return dispatcher;
    }

    /// <summary>
    /// Gets all registered actor type names.
    /// Useful for debugging and diagnostics.
    /// </summary>
    public static IReadOnlyCollection<string> GetRegisteredActorTypes()
    {
        return Dispatchers.Keys.ToList();
    }

#if DEBUG
    /// <summary>
    /// Clears all registered dispatchers. 
    /// WARNING: For testing purposes only. Do not call this in production code.
    /// </summary>
    internal static void Clear()
    {
        Dispatchers.Clear();
    }
#endif
}
