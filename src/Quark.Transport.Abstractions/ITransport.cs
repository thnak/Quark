using System.Net;

namespace Quark.Transport.Abstractions;

/// <summary>
///     Factory that creates listeners and outbound connections for a specific transport type
///     (e.g. TCP, QUIC, in-process).
/// </summary>
public interface ITransport
{
    /// <summary>Human-readable name for this transport (e.g. "tcp", "quic", "inproc").</summary>
    string Name { get; }

    /// <summary>
    ///     Creates a listener bound to <paramref name="endPoint" />.
    ///     Call <see cref="ITransportListener.BindAsync" /> before accepting connections.
    /// </summary>
    ITransportListener CreateListener(EndPoint endPoint);

    /// <summary>Opens an outbound connection to <paramref name="endPoint" />.</summary>
    Task<ITransportConnection> ConnectAsync(
        EndPoint endPoint,
        CancellationToken cancellationToken = default);
}
