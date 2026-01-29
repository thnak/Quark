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
}
