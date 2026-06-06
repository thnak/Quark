using System.Net;

namespace Quark.Client.Tcp;

/// <summary>Options for the TCP gateway client connection.</summary>
public sealed class TcpGatewayClientOptions
{
    public IPEndPoint GatewayEndpoint { get; set; } = new(IPAddress.Loopback, 30000);
}
