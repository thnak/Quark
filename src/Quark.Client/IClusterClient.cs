using Quark.Networking.Abstractions;

namespace Quark.Client;

/// <summary>
/// Lightweight client gateway for connecting to Quark clusters.
/// Does not host actors locally - routes all calls to appropriate silos.
/// </summary>
public interface IClusterClient : IDisposable
{
    /// <summary>
    /// Gets the local silo ID if this client is co-located with a silo, null otherwise.
    /// When non-null, calls to actors on this silo can be optimized to avoid network overhead.
    /// </summary>
    string? LocalSiloId { get; }

    /// <summary>
    /// Gets the cluster membership provider.
    /// </summary>
    IQuarkClusterMembership ClusterMembership { get; }

    /// <summary>
    /// Gets the transport layer for communication.
    /// </summary>
    IQuarkTransport Transport { get; }

    /// <summary>
    /// Connects to the cluster.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task ConnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Disconnects from the cluster.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task DisconnectAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends an envelope to an actor in the cluster.
    /// Routes the call to the appropriate silo based on consistent hashing.
    /// </summary>
    /// <param name="envelope">The envelope to send.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The response envelope.</returns>
    Task<QuarkEnvelope> SendAsync(QuarkEnvelope envelope, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a type-safe proxy for an actor in the cluster.
    /// The proxy provides strongly-typed method calls that are routed to the appropriate silo.
    /// </summary>
    /// <typeparam name="TActorInterface">The actor interface type. Must inherit from IQuarkActor or be registered via QuarkActorContext.</typeparam>
    /// <param name="actorId">The unique identifier of the actor instance.</param>
    /// <returns>A proxy instance implementing the actor interface.</returns>
    /// <remarks>
    /// This method returns a generated proxy that implements the specified actor interface.
    /// Method calls on the proxy are automatically serialized to Protobuf messages and
    /// sent to the cluster using SendAsync.
    /// </remarks>
    TActorInterface GetActor<TActorInterface>(string actorId) where TActorInterface : class;
}
