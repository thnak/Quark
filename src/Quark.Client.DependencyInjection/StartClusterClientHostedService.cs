using Microsoft.Extensions.Hosting;
using Quark.Client;

namespace Quark.Client.DependencyInjection;

/// <summary>
/// Background service that automatically connects the cluster client on startup.
/// </summary>
public class StartClusterClientHostedService : BackgroundService
{
    private readonly IClusterClient _clusterClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="StartClusterClientHostedService"/> class.
    /// </summary>
    /// <param name="clusterClient">The cluster client to start.</param>
    public StartClusterClientHostedService(IClusterClient clusterClient)
    {
        _clusterClient = clusterClient ?? throw new ArgumentNullException(nameof(clusterClient));
    }

    /// <inheritdoc />
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return _clusterClient.ConnectAsync(stoppingToken);
    }

    /// <inheritdoc />
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await _clusterClient.DisconnectAsync(cancellationToken);
        await base.StopAsync(cancellationToken);
    }
}
