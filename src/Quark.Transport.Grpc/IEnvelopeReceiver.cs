using Quark.Networking.Abstractions;

namespace Quark.Transport.Grpc;

/// <summary>
/// Interface for components that can receive envelopes from the gRPC transport layer.
/// Decouples QuarkTransportService from the concrete GrpcQuarkTransport implementation.
/// </summary>
public interface IEnvelopeReceiver
{
    /// <summary>
    /// Notifies the receiver of an incoming envelope.
    /// </summary>
    /// <param name="envelope">The received envelope.</param>
    void OnEnvelopeReceived(QuarkEnvelope envelope);
}
