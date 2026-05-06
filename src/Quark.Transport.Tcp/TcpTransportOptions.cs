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
}
