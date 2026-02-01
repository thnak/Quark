using Quark.Abstractions;

namespace Quark.Client;

/// <summary>
/// Factory for creating type-safe actor proxies.
/// Proxy factories must be registered using RegisterProxyFactory before use.
/// The ProxySourceGenerator generates a registration class that calls RegisterAll()
/// to register all proxy factories for an assembly.
/// </summary>
/// <remarks>
/// Example registration (auto-generated):
/// <code>
/// Quark.Generated.MyAssemblyActorProxyFactoryRegistration.RegisterAll();
/// </code>
/// 
/// This enables AOT-compatible, type-safe actor proxies with zero reflection.
/// </remarks>
public static class ActorProxyFactory
{
    /// <summary>
    /// Registry of actor interface types to their corresponding proxy factory functions.
    /// </summary>
    private static readonly Dictionary<Type, Func<IClusterClient, string, IQuarkActor>> ActorFac = new();

    /// <summary>
    /// Creates a type-safe actor proxy for the specified actor interface and ID.
    /// </summary>
    /// <param name="client">The cluster client to use for communication.</param>
    /// <param name="actorId">The unique identifier of the actor instance.</param>
    /// <typeparam name="TActorProxy">The actor interface type.</typeparam>
    /// <returns>A proxy instance implementing the actor interface.</returns>
    /// <exception cref="ArgumentNullException">Thrown when client is null.</exception>
    /// <exception cref="ArgumentException">Thrown when actorId is null or whitespace.</exception>
    /// <exception cref="InvalidOperationException">Thrown when no proxy factory is registered for the type.</exception>
    public static TActorProxy CreateProxy<TActorProxy>(IClusterClient client, string actorId)
    {
        if (client == null)
        {
            throw new ArgumentNullException(nameof(client));
        }

        if (actorId == null)
        {
            throw new ArgumentNullException(nameof(actorId));
        }

        if (string.IsNullOrWhiteSpace(actorId))
        {
            throw new ArgumentException("Actor ID cannot be empty or whitespace.", nameof(actorId));
        }

        if (ActorFac.TryGetValue(typeof(TActorProxy), out var factory))
        {
            return (TActorProxy)(object)factory(client, actorId);
        }

        throw new InvalidOperationException(
            $"No proxy factory registered for actor interface type {typeof(TActorProxy).FullName}. " +
            "Ensure you have called the RegisterAll() method from your assembly's generated registration class.");
    }

    /// <summary>
    /// Registers a proxy factory for the specified actor interface type.
    /// </summary>
    /// <typeparam name="TActorProxy">The actor interface type.</typeparam>
    /// <param name="factory">Factory function that creates proxy instances.</param>
    public static void RegisterProxyFactory<TActorProxy>(Func<IClusterClient, string, IQuarkActor> factory)
    {
        ActorFac[typeof(TActorProxy)] = factory;
    }
}