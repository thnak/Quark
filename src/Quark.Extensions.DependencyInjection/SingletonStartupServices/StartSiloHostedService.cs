using Microsoft.Extensions.Hosting;
using Quark.Hosting;

namespace Quark.Extensions.DependencyInjection.SingletonStartupServices;

public class StartSiloHostedService(IQuarkSilo quarkSilo) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return quarkSilo.StartAsync(stoppingToken);
    }
}