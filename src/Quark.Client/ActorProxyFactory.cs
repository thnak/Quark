using Quark.Abstractions;

namespace Quark.Client;

/// <summary>
/// Factory for creating type-safe actor proxies.
/// The actual implementation is generated at compile-time by the ProxySourceGenerator.
/// </summary>
internal static partial class ActorProxyFactory
{
    /// <summary>
    /// Creates a proxy instance for the specified actor interface.
    /// This method is implemented by the source generator.
    /// </summary>
    /// <typeparam name="TActorInterface">The actor interface type.</typeparam>
    /// <param name="client">The cluster client to use for communication.</param>
    /// <param name="actorId">The unique identifier of the actor instance.</param>
    /// <returns>A proxy instance implementing the actor interface.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no proxy factory is registered for the specified actor interface type.
    /// This typically means the source generator did not run or the interface does not inherit from IQuarkActor.
    /// </exception>
    public static TActorInterface CreateProxy<TActorInterface>(IClusterClient client, string actorId)
        where TActorInterface : class, IQuarkActor
    {
        throw new InvalidOperationException(
            $"No proxy factory registered for actor interface type '{typeof(TActorInterface).FullName}'. " +
            "Ensure the interface inherits from IQuarkActor and the ProxySourceGenerator is properly referenced.");
    }
}
