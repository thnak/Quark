using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Quark.Client;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Hosting;
using Quark.Runtime;
using Quark.Transport.Tcp;

namespace Quark.Client.Tcp;

/// <summary>
///     Extension methods for wiring up the TCP gateway client on an <see cref="IClientBuilder" />.
/// </summary>
public static class TcpClientBuilderExtensions
{
    /// <summary>
    ///     Configures the client to connect to a gateway silo on <c>localhost:{gatewayPort}</c>.
    ///     Drop-in equivalent of Orleans' <c>UseLocalhostClustering()</c> on the client side.
    /// </summary>
    public static IClientBuilder UseLocalhostGateway(
        this IClientBuilder builder,
        int gatewayPort = 30000)
        => builder.UseTcpGateway(o => o.GatewayEndpoint = new IPEndPoint(IPAddress.Loopback, gatewayPort));

    /// <summary>
    ///     Configures the client to connect to a gateway silo using the provided endpoint configuration.
    /// </summary>
    public static IClientBuilder UseTcpGateway(
        this IClientBuilder builder,
        Action<TcpGatewayClientOptions> configure)
    {
        IServiceCollection services = builder.Services;

        services.Configure(configure);

        services.TryAddSingleton<GrainProxyFactoryRegistry>();
        services.TryAddSingleton<GrainInterfaceTypeRegistry>();

        services.AddTcpTransport();

        services.RemoveAll<LocalClusterClient>();
        services.RemoveAll<LocalGrainFactory>();
        services.RemoveAll<IClusterClient>();
        services.RemoveAll<IGrainFactory>();

        services.TryAddSingleton<MessageSerializer>();
        services.TryAddSingleton<GrainMessageSerializer>();
        services.TryAddSingleton<TcpGatewayConnection>();
        services.TryAddSingleton<TcpGatewayCallInvoker>();
        services.TryAddSingleton<TcpGatewayGrainFactory>();
        services.TryAddSingleton<TcpGatewayClusterClient>();
        services.TryAddSingleton<IClusterClient>(sp => sp.GetRequiredService<TcpGatewayClusterClient>());
        services.TryAddSingleton<IGrainFactory>(sp => sp.GetRequiredService<TcpGatewayGrainFactory>());
        services.AddHostedService<TcpClientStartupService>();

        return builder;
    }
}
