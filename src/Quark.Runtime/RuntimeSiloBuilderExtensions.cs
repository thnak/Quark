using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Quark.Core.Abstractions.Clustering;
using Quark.Core.Hosting;
using Quark.Runtime.Clustering;
using Quark.Transport.Tcp;

namespace Quark.Runtime;

/// <summary>
///     Silo builder extension methods that require Quark.Runtime services.
/// </summary>
public static class RuntimeSiloBuilderExtensions
{
    /// <summary>
    ///     Configures the silo for in-process multi-silo localhost clustering.
    ///     Silos that share the same <paramref name="clusterId" /> and <paramref name="serviceId" /> in the
    ///     same process are connected via a shared grain directory, router, and membership table.
    ///     Drop-in equivalent of Orleans' <c>UseLocalhostClustering()</c>.
    /// </summary>
    public static ISiloBuilder UseLocalhostClustering(
        this ISiloBuilder builder,
        int siloPort = 11111,
        int gatewayPort = 30000,
        string clusterId = "dev",
        string serviceId = "QuarkService")
    {
        string key = $"{clusterId}:{serviceId}";
        LocalhostClusterState state = SharedLocalhostCluster.GetOrCreate(key);

        // Replace the default single-node directory with the shared one.
        builder.Services.RemoveAll<IGrainDirectory>();
        builder.Services.AddSingleton<IGrainDirectory>(state.Directory);
        builder.Services.AddSingleton<InMemoryGrainDirectory>(state.Directory);

        // Wire up the shared membership and routing services.
        builder.Services.TryAddSingleton<ISiloRouter>(state.Router);
        builder.Services.TryAddSingleton<IMembershipTable>(state.MembershipTable);

        // Membership oracle runs the IAmAlive loop and detects dead silos.
        builder.Services.AddHostedService<MembershipOracle>();

        // Gateway pump accepts external TCP client connections on the gateway port.
        builder.Services.AddHostedService<GatewayMessagePump>();

        // Configure silo identity.
        builder.Configure<SiloRuntimeOptions>(o =>
        {
            o.ClusterId = clusterId;
            o.ServiceId = serviceId;
            o.SiloAddress = SiloAddress.Loopback(siloPort);
            o.GatewayAddress = SiloAddress.Loopback(gatewayPort);
        });

        return builder;
    }

    /// <summary>
    ///     Configures TLS for all silo-to-silo TCP connections.
    ///     Drop-in equivalent of Orleans' <c>UseTls()</c>.
    /// </summary>
    public static ISiloBuilder UseTls(this ISiloBuilder builder, Action<TlsOptions> configure)
    {
        builder.Configure<TcpTransportOptions>(o =>
        {
            o.Tls ??= new TlsOptions();
            configure(o.Tls);
        });
        return builder;
    }
}
