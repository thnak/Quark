using Microsoft.Extensions.Hosting;
using Quark.Client;

namespace Quark.Extensions.DependencyInjection.SingletonStartupServices;

public class StartClusterClientHostedService(IClusterClient clusterClient) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return clusterClient.ConnectAsync(stoppingToken);
    }
}