using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Quark.Client;

namespace Quark.Client.Tcp;

/// <summary>
///     Hosted service that (1) applies deferred grain-proxy registrations and (2) connects the
///     <see cref="TcpGatewayClusterClient" /> at host startup.
/// </summary>
internal sealed class TcpClientStartupService : IHostedService
{
    private readonly TcpGatewayClusterClient _client;
    private readonly IServiceProvider _services;

    public TcpClientStartupService(TcpGatewayClusterClient client, IServiceProvider services)
    {
        _client = client;
        _services = services;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var proxyRegistry = _services.GetService<GrainProxyFactoryRegistry>();
        var interfaceRegistry = _services.GetService<GrainInterfaceTypeRegistry>();
        if (proxyRegistry is not null && interfaceRegistry is not null)
        {
            foreach (ClientServiceCollectionExtensions.IProxyRegistration reg
                in _services.GetServices<ClientServiceCollectionExtensions.IProxyRegistration>())
            {
                reg.Apply(proxyRegistry, interfaceRegistry);
            }
        }

        await _client.Connect().ConfigureAwait(false);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _client.Close().ConfigureAwait(false);
    }
}
