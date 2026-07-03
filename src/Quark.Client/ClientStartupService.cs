using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Quark.Client;

/// <summary>
///     Hosted service that applies deferred proxy and interface-type registrations at startup.
///     Registered automatically when <c>AddLocalClusterClient()</c> is called.
/// </summary>
internal sealed class ClientStartupService : IHostedService
{
    private readonly IServiceProvider _services;

    public ClientStartupService(IServiceProvider services)
    {
        _services = services;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        var proxyRegistry = _services.GetService<GrainProxyFactoryRegistry>();
        var interfaceRegistry = _services.GetService<GrainInterfaceTypeRegistry>();

        if (proxyRegistry is null || interfaceRegistry is null)
        {
            return Task.CompletedTask;
        }

        foreach (ClientServiceCollectionExtensions.IProxyRegistration reg in _services.GetServices<ClientServiceCollectionExtensions.IProxyRegistration>())
        {
            reg.Apply(proxyRegistry, interfaceRegistry);
        }

        // Apply deferred observer proxy registrations.
        if (_services.GetService<ObserverProxyFactoryRegistry>() is { } observerProxyRegistry)
        {
            foreach (ClientServiceCollectionExtensions.IObserverProxyRegistration reg in _services.GetServices<ClientServiceCollectionExtensions.IObserverProxyRegistration>())
            {
                reg.Apply(observerProxyRegistry);
            }
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
