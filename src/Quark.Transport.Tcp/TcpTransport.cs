using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Quark.Transport.Abstractions;

namespace Quark.Transport.Tcp;

/// <summary>
///     TCP transport factory. Creates TCP listeners and outbound TCP connections.
///     When <see cref="TcpTransportOptions.Tls" /> is configured, connections are wrapped in
///     <see cref="SslStream" /> during connect/accept.
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
        ApplySocketOptions(socket, _options);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
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

        if (_options.Tls is { } tls)
        {
            var networkStream = new NetworkStream(socket, ownsSocket: false);
            var sslStream = new SslStream(networkStream, leaveInnerStreamOpen: false,
                BuildRemoteCallback(tls));

            string targetHost = endPoint is IPEndPoint ip ? ip.Address.ToString() : endPoint.ToString()!;

            var authOptions = new SslClientAuthenticationOptions
            {
                TargetHost = targetHost,
                ClientCertificates = tls.LocalCertificate is not null
                    ? [tls.LocalCertificate]
                    : null,
            };
            tls.OnAuthenticateAsClient?.Invoke(authOptions);

            await sslStream.AuthenticateAsClientAsync(authOptions, cancellationToken).ConfigureAwait(false);
            _logger.LogDebug("TLS handshake complete (client) to {EndPoint}.", endPoint);
            return new TcpTransportConnection(socket, sslStream, _options, _logger);
        }

        return new TcpTransportConnection(socket, _options, _logger);
    }

    internal static void ApplySocketOptions(Socket socket, TcpTransportOptions options)
    {
        socket.NoDelay = !options.EnableNagle;
        socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, options.KeepAlive);
        socket.ReceiveBufferSize = options.ReceiveBufferSize;
        socket.SendBufferSize = options.SendBufferSize;
    }

    internal static RemoteCertificateValidationCallback? BuildRemoteCallback(TlsOptions tls)
        => tls.RemoteCertificateMode switch
        {
            RemoteCertificateMode.AllowAny => (_, _, _, _) => true,
            RemoteCertificateMode.NoCertificate => null,
            _ => null, // default: OS validation
        };
}
