using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Quark.Transport.Abstractions;

namespace Quark.Transport.Tcp;

/// <summary>
///     TCP listener that accepts inbound connections.
///     When <see cref="TcpTransportOptions.Tls" /> is configured, accepted connections are wrapped
///     in <see cref="SslStream" /> with server-side TLS authentication.
/// </summary>
internal sealed class TcpTransportListener : ITransportListener
{
    private readonly ILogger _logger;
    private readonly TcpTransportOptions _options;
    private readonly Socket _serverSocket;
    private bool _stopped;

    internal TcpTransportListener(EndPoint endPoint, TcpTransportOptions options, ILogger logger)
    {
        _options = options;
        _serverSocket = new Socket(endPoint.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
        _serverSocket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
        _serverSocket.Bind(endPoint);
        LocalEndPoint = _serverSocket.LocalEndPoint!;
        _logger = logger;
    }

    /// <inheritdoc />
    public EndPoint LocalEndPoint { get; }

    /// <inheritdoc />
    public Task BindAsync(CancellationToken cancellationToken = default)
    {
        _serverSocket.Listen(512);
        _logger.LogInformation("TCP listener bound to {EndPoint}.", LocalEndPoint);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async ValueTask<ITransportConnection?> AcceptAsync(CancellationToken cancellationToken = default)
    {
        if (_stopped)
        {
            return null;
        }

        Socket client;
        try
        {
            client = await _serverSocket.AcceptAsync(cancellationToken).ConfigureAwait(false);
            client.NoDelay = true;
            _logger.LogDebug("TCP connection accepted from {RemoteEndPoint}.", client.RemoteEndPoint);
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

        if (_options.Tls is { } tls)
        {
            var networkStream = new NetworkStream(client, ownsSocket: false);
            var sslStream = new SslStream(networkStream, leaveInnerStreamOpen: false,
                TcpTransport.BuildRemoteCallback(tls));

            var authOptions = new SslServerAuthenticationOptions
            {
                ServerCertificate = tls.LocalCertificate,
                ClientCertificateRequired = tls.RemoteCertificateMode == RemoteCertificateMode.RequireCertificate,
            };
            tls.OnAuthenticateAsServer?.Invoke(authOptions);

            try
            {
                await sslStream.AuthenticateAsServerAsync(authOptions, cancellationToken).ConfigureAwait(false);
                _logger.LogDebug("TLS handshake complete (server) from {RemoteEndPoint}.", client.RemoteEndPoint);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "TLS handshake failed from {RemoteEndPoint}.", client.RemoteEndPoint);
                await sslStream.DisposeAsync().ConfigureAwait(false);
                client.Dispose();
                return null;
            }

            return new TcpTransportConnection(client, sslStream, _logger);
        }

        return new TcpTransportConnection(client, _logger);
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken = default)
    {
        _stopped = true;
        _serverSocket.Close();
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync()
    {
        _serverSocket.Dispose();
        return ValueTask.CompletedTask;
    }
}
