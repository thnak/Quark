using Quark.Transport.Abstractions;

namespace Quark.Transport.Tcp;

/// <summary>Options specific to the TCP transport.</summary>
public sealed class TcpTransportOptions : TransportOptions
{
    /// <summary>
    ///     Enable Nagle's algorithm. <c>false</c> (TCP_NODELAY on) by default for low-latency RPC.
    /// </summary>
    public bool EnableNagle { get; set; } = false;

    /// <summary>
    ///     Enable SO_KEEPALIVE. Default: <c>true</c>.
    /// </summary>
    public bool KeepAlive { get; set; } = true;

    /// <summary>
    ///     Optional TLS configuration. When set, all TCP connections are wrapped in <c>SslStream</c>.
    ///     Configure via <c>UseTls()</c> on the silo builder.
    /// </summary>
    public TlsOptions? Tls { get; set; }
}
