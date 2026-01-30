using Quark.Networking.Abstractions;

namespace Quark.Client;

/// <summary>
/// Lightweight client gateway for connecting to Quark clusters.
/// Does not host actors locally - routes all calls to appropriate silos.
/// </summary>
public interface IClusterClient : IDisposable
{
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
    /// Gets a type-safe proxy for invoking methods on a remote actor.
    /// The proxy provides compile-time type checking and IntelliSense support.
    /// </summary>
    /// <typeparam name="TProxy">The proxy interface type (e.g., ICounterActorProxy).</typeparam>
    /// <param name="actorId">The unique identifier of the actor instance.</param>
    /// <returns>A proxy instance that implements the specified interface.</returns>
    TProxy GetActorProxy<TProxy>(string actorId) where TProxy : class;
}
