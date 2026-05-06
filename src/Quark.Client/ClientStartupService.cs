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
        var proxyRegistry = _services.GetService(typeof(GrainProxyFactoryRegistry)) as GrainProxyFactoryRegistry;
        var interfaceRegistry = _services.GetService(typeof(GrainInterfaceTypeRegistry)) as GrainInterfaceTypeRegistry;

        if (proxyRegistry is null || interfaceRegistry is null)
        {
            return Task.CompletedTask;
        }

        IEnumerable<object> registrations = (IEnumerable<object>?)_services.GetService(
            typeof(IEnumerable<ClientServiceCollectionExtensions.IProxyRegistration>)) ?? [];

        foreach (ClientServiceCollectionExtensions.IProxyRegistration? reg in registrations
                     .Cast<ClientServiceCollectionExtensions.IProxyRegistration>())
        {
            reg.Apply(proxyRegistry, interfaceRegistry);
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
