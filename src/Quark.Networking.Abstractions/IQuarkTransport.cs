using Quark.Abstractions.Clustering;

namespace Quark.Networking.Abstractions;

/// <summary>
///     Provides transport-level communication for Quark actor invocations.
///     Uses bi-directional gRPC streaming for efficient communication.
/// </summary>
public interface IQuarkTransport : IDisposable
{
    /// <summary>
    ///     Gets the local silo ID.
    /// </summary>
    string LocalSiloId { get; }

    /// <summary>
    ///     Gets the local endpoint this transport is listening on.
    /// </summary>
    string LocalEndpoint { get; }

    /// <summary>
    ///     Sends an envelope to a remote silo.
    ///     Uses bi-directional streaming - maintains one stream per connection.
    /// </summary>
    /// <param name="targetSiloId">The target silo ID.</param>
    /// <param name="envelope">The envelope to send.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The response envelope.</returns>
    Task<QuarkEnvelope> SendAsync(
        string targetSiloId,
        QuarkEnvelope envelope,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Establishes a connection to a remote silo.
    ///     Opens a bi-directional gRPC stream.
    /// </summary>
    /// <param name="siloInfo">The remote silo information.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task ConnectAsync(SiloInfo siloInfo, CancellationToken cancellationToken = default);

    /// <summary>
    ///     Disconnects from a remote silo.
    ///     Closes the bi-directional stream.
    /// </summary>
    /// <param name="siloId">The silo ID to disconnect from.</param>
    Task DisconnectAsync(string siloId);

    /// <summary>
    ///     Starts the transport and begins listening for incoming connections.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    ///     Stops the transport and closes all connections.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    ///     Event raised when an envelope is received from a remote silo.
    /// </summary>
    event EventHandler<QuarkEnvelope>? EnvelopeReceived;
}