using System.IO.Pipelines;
using System.Net;

namespace Quark.Transport.Abstractions;

/// <summary>
/// Represents an active full-duplex transport connection between two nodes.
/// The connection exposes a <see cref="System.IO.Pipelines.IDuplexPipe"/> for framed message I/O.
/// </summary>
public interface ITransportConnection : IAsyncDisposable
{
    /// <summary>Unique identifier for this connection (for logging/diagnostics).</summary>
    string ConnectionId { get; }

    /// <summary>Local endpoint of this connection.</summary>
    EndPoint? LocalEndPoint { get; }

    /// <summary>Remote endpoint of this connection.</summary>
    EndPoint? RemoteEndPoint { get; }

    /// <summary>Duplex pipe for reading/writing framed messages.</summary>
    IDuplexPipe Transport { get; }

    /// <summary>
    /// Starts the connection's send/receive loops.
    /// Completes when the connection closes (gracefully or due to error).
    /// </summary>
    Task ExecuteAsync(CancellationToken cancellationToken = default);

    /// <summary>Initiates a graceful close.</summary>
    Task CloseAsync(CancellationToken cancellationToken = default);

    /// <summary>Fires when the connection is closed, carrying an optional error.</summary>
    Task Completion { get; }
}
