using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Quark.Client;

namespace Quark.Examples.ClientOnly;

public class DemoClientService : BackgroundService
{
    private readonly IClusterClient _client;
    private readonly ILogger<DemoClientService> _logger;

    public DemoClientService(IClusterClient client, ILogger<DemoClientService> logger)
    {
        _client = client;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
        _logger.LogInformation("Checking cluster status...");

        try
        {
            var silos = await _client.ClusterMembership.GetActiveSilosAsync(stoppingToken);

            if (silos.Count == 0)
            {
                _logger.LogWarning("No active silos found");
                return;
            }

            _logger.LogInformation($"Connected to cluster with {silos.Count} silo(s)");
            foreach (var silo in silos)
            {
                _logger.LogInformation($"  Silo: {silo.SiloId} @ {silo.Address}:{silo.Port}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to connect to cluster");
        }

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }
}