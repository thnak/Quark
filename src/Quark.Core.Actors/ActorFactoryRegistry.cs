using Microsoft.Extensions.DependencyInjection;
using Quark.Abstractions;

namespace Quark.Core.Actors;

/// <summary>
///     Registry for actor factory methods and type mappings.
///     This class is populated by source generators at compile-time.
///     Provides both Type-based and string-based actor lookups for AOT compatibility.
/// </summary>
public static class ActorFactoryRegistry
{
    private static readonly Dictionary<Type, Func<string, IActorFactory?, IServiceScope?, IActor>> Factories = new();
    private static readonly Dictionary<string, Type> ActorTypesByName = new();
    private static readonly Dictionary<Type, string> ActorNamesByType = new();

    /// <summary>
    ///     Registers an actor factory method with its type name.
    /// </summary>
    /// <typeparam name="TActor">The actor type to register.</typeparam>
    /// <param name="actorTypeName">The unique actor type name used in remote calls.</param>
    /// <param name="factory">The factory function to create actor instances.</param>
    /// <exception cref="InvalidOperationException">Thrown when duplicate actor type name is registered.</exception>
    public static void RegisterFactory<TActor>(
        string actorTypeName,
        Func<string, IActorFactory?, IServiceScope?, TActor> factory)
        where TActor : IActor
    {
        var actorType = typeof(TActor);
        
        // Register factory by Type
        Factories[actorType] = (id, f, s) => factory(id, f, s);
        
        // Register bidirectional name ↔ Type mapping
        if (ActorTypesByName.TryGetValue(actorTypeName, out var existingType))
        {
            throw new InvalidOperationException(
                $"Actor type name '{actorTypeName}' is already registered for type '{existingType.FullName}'. " +
                $"Cannot register it again for type '{actorType.FullName}'. " +
                "Use [Actor(Name = \"UniqueName\")] attribute to specify a unique name.");
        }
        
        ActorTypesByName[actorTypeName] = actorType;
        ActorNamesByType[actorType] = actorTypeName;
    }

    /// <summary>
    ///     Creates an actor using the registered factory method.
    /// </summary>
    /// <typeparam name="TActor">The actor type to create.</typeparam>
    /// <param name="actorId">The unique actor instance ID.</param>
    /// <param name="actorFactory">Optional actor factory for spawning children.</param>
    /// <param name="serviceScope">Optional DI service scope.</param>
    /// <returns>The created actor instance.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no factory is registered for the type.</exception>
    public static TActor CreateActor<TActor>(string actorId, IActorFactory? actorFactory, IServiceScope? serviceScope)
        where TActor : IActor
    {
        var actorType = typeof(TActor);

        if (Factories.TryGetValue(actorType, out var factory))
            return (TActor)factory(actorId, actorFactory, serviceScope);

        throw new InvalidOperationException(
            $"No factory registered for actor type {actorType.Name}. " +
            "Ensure the actor is marked with [Actor] attribute for source generation.");
    }

    /// <summary>
    ///     Gets the actor Type for a given actor type name.
    /// </summary>
    /// <param name="actorTypeName">The actor type name from QuarkEnvelope.</param>
    /// <returns>The actor Type, or null if not found.</returns>
    public static Type? GetActorType(string actorTypeName)
    {
        return ActorTypesByName.TryGetValue(actorTypeName, out var type) ? type : null;
    }

    /// <summary>
    ///     Gets the actor type name for a given actor Type.
    /// </summary>
    /// <param name="actorType">The actor Type.</param>
    /// <returns>The actor type name, or null if not found.</returns>
    public static string? GetActorTypeName(Type actorType)
    {
        return ActorNamesByType.TryGetValue(actorType, out var name) ? name : null;
    }

    /// <summary>
    ///     Creates an actor using the registered factory method (non-generic version for runtime dispatch).
    /// </summary>
    /// <param name="actorTypeName">The actor type name.</param>
    /// <param name="actorId">The unique actor instance ID.</param>
    /// <param name="actorFactory">Optional actor factory for spawning children.</param>
    /// <param name="serviceScope">Optional DI service scope.</param>
    /// <returns>The created actor instance.</returns>
    /// <exception cref="InvalidOperationException">Thrown when no factory is registered for the type.</exception>
    public static IActor CreateActorByName(
        string actorTypeName, 
        string actorId, 
        IActorFactory? actorFactory, 
        IServiceScope? serviceScope)
    {
        // Look up the type by name
        if (!ActorTypesByName.TryGetValue(actorTypeName, out var actorType))
        {
            throw new InvalidOperationException(
                $"No actor type registered with name '{actorTypeName}'. " +
                "Ensure the actor is marked with [Actor] attribute for source generation.");
        }

        // Look up the factory function
        if (Factories.TryGetValue(actorType, out var factory))
        {
            return factory(actorId, actorFactory, serviceScope);
        }

        throw new InvalidOperationException(
            $"No factory registered for actor type {actorType.Name}. " +
            "Ensure the actor is marked with [Actor] attribute for source generation.");
    }

    /// <summary>
    ///     Gets all registered actor types.
    /// </summary>
    /// <returns>A read-only collection of registered actor type names and their types.</returns>
    public static IReadOnlyDictionary<string, Type> GetAllActorTypes()
    {
        return ActorTypesByName;
    }
}