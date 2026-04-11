using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Quark.Transport.Abstractions;

namespace Quark.Transport.Tcp;

/// <summary>
/// TCP listener that accepts inbound connections.
/// </summary>
internal sealed class TcpTransportListener : ITransportListener
{
    private readonly Socket _serverSocket;
    private readonly ILogger _logger;
    private bool _stopped;

    internal TcpTransportListener(EndPoint endPoint, TcpTransportOptions options, ILogger logger)
    {
        _serverSocket = new Socket(endPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        _serverSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _serverSocket.Bind(endPoint);
        LocalEndPoint = _serverSocket.LocalEndPoint!;
        _logger = logger;
    }

    /// <inheritdoc/>
    public EndPoint LocalEndPoint { get; }

    /// <inheritdoc/>
    public Task BindAsync(CancellationToken cancellationToken = default)
    {
        _serverSocket.Listen(backlog: 512);
        _logger.LogInformation("TCP listener bound to {EndPoint}.", LocalEndPoint);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async ValueTask<ITransportConnection?> AcceptAsync(CancellationToken cancellationToken = default)
    {
        if (_stopped)
            return null;

        try
        {
            Socket client = await _serverSocket.AcceptAsync(cancellationToken).ConfigureAwait(false);
            client.NoDelay = true;
            _logger.LogDebug("TCP connection accepted from {RemoteEndPoint}.", client.RemoteEndPoint);
            return new TcpTransportConnection(client, _logger);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return null;
        }
        catch (SocketException ex) when (_stopped)
        {
            _logger.LogDebug(ex, "Accept loop terminated because listener was stopped.");
            return null;
        }
    }

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        _stopped = true;
        _serverSocket.Close();
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        _serverSocket.Dispose();
        return ValueTask.CompletedTask;
    }
}
