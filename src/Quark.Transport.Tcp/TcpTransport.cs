using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Quark.Transport.Abstractions;

namespace Quark.Transport.Tcp;

/// <summary>
///     TCP transport factory. Creates TCP listeners and outbound TCP connections.
/// </summary>
public sealed class TcpTransport : ITransport
{
    private readonly ILogger<TcpTransport> _logger;
    private readonly TcpTransportOptions _options;

    /// <summary>Initialises the transport with the specified options.</summary>
    public TcpTransport(TcpTransportOptions options, ILogger<TcpTransport> logger)
    {
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "tcp";

    /// <inheritdoc />
    public ITransportListener CreateListener(EndPoint endPoint)
    {
        return new TcpTransportListener(endPoint, _options, _logger);
    }

    /// <inheritdoc />
    public async Task<ITransportConnection> ConnectAsync(
        EndPoint endPoint,
        CancellationToken cancellationToken = default)
    {
        Socket socket = new(endPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        socket.NoDelay = true;

        using var cts =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(_options.ConnectTimeout);

        try
        {
            await socket.ConnectAsync(endPoint, cts.Token).ConfigureAwait(false);
        }
        catch
        {
            socket.Dispose();
            throw;
        }

        _logger.LogDebug("TCP outbound connection established to {EndPoint}.", endPoint);
        return new TcpTransportConnection(socket, _logger);
    }
}
