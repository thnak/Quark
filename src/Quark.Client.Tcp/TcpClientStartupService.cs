using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Quark.Client.Tcp;

/// <summary>
///     Hosted service that (1) applies deferred grain-proxy and observer registrations and
///     (2) connects the <see cref="TcpGatewayClusterClient" /> at host startup.
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
        GrainProxyFactoryRegistry? proxyRegistry = _services.GetService<GrainProxyFactoryRegistry>();
        GrainInterfaceTypeRegistry? interfaceRegistry = _services.GetService<GrainInterfaceTypeRegistry>();
        if (proxyRegistry is not null && interfaceRegistry is not null)
        {
            foreach (ClientServiceCollectionExtensions.IProxyRegistration reg
                in _services.GetServices<ClientServiceCollectionExtensions.IProxyRegistration>())
            {
                reg.Apply(proxyRegistry, interfaceRegistry);
            }
        }

        ObserverProxyFactoryRegistry? observerProxyRegistry = _services.GetService<ObserverProxyFactoryRegistry>();
        if (observerProxyRegistry is not null)
        {
            foreach (ClientServiceCollectionExtensions.IObserverProxyRegistration reg
                in _services.GetServices<ClientServiceCollectionExtensions.IObserverProxyRegistration>())
            {
                reg.Apply(observerProxyRegistry);
            }
        }

        ObserverTransportDispatcherRegistry? observerDispatcherRegistry = _services.GetService<ObserverTransportDispatcherRegistry>();
        if (observerDispatcherRegistry is not null)
        {
            foreach (TcpClientBuilderExtensions.IObserverDispatcherRegistration reg
                in _services.GetServices<TcpClientBuilderExtensions.IObserverDispatcherRegistration>())
            {
                reg.Apply(observerDispatcherRegistry);
            }
        }

        await _client.Connect().ConfigureAwait(false);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _client.Close().ConfigureAwait(false);
    }
}
