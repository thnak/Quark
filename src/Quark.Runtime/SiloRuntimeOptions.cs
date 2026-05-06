using System.Net;

namespace Quark.Runtime;

/// <summary>
///     Configuration options for a Quark silo instance.
/// </summary>
public sealed class SiloRuntimeOptions
{
    /// <summary>
    ///     Logical cluster identifier.  All silos in the same cluster must share the same value.
    ///     Default: <c>"QuarkCluster"</c>.
    /// </summary>
    public string ClusterId { get; set; } = "QuarkCluster";

    /// <summary>
    ///     Service identifier.  Distinguishes multiple services sharing one cluster.
    ///     Default: <c>"QuarkService"</c>.
    /// </summary>
    public string ServiceId { get; set; } = "QuarkService";

    /// <summary>
    ///     Human-readable name for this silo instance (used in logs/diagnostics).
    ///     Default: machine host name.
    /// </summary>
    public string SiloName { get; set; } = Dns.GetHostName();

    /// <summary>
    ///     The TCP endpoint this silo advertises for grain-to-grain traffic.
    ///     Default: loopback on port 11111.
    /// </summary>
    public SiloAddress SiloAddress { get; set; } = SiloAddress.Loopback(11111);

    /// <summary>
    ///     The TCP endpoint used for the gateway (client-facing).
    ///     Default: loopback on port 30000.
    /// </summary>
    public SiloAddress GatewayAddress { get; set; } = SiloAddress.Loopback(30000);
}
