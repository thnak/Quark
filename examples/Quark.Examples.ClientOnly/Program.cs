using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Quark.Client;
using Quark.Client.DependencyInjection;

namespace Quark.Examples.ClientOnly;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);
        builder.Services.AddLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddConsole();
            logging.SetMinimumLevel(LogLevel.Information);
        });

        var redisHost = Environment.GetEnvironmentVariable("REDIS_HOST") ?? "localhost";

        Console.WriteLine("Client-Only Example - Connecting to cluster without hosting actors");
        Console.WriteLine($"Redis: {redisHost}");

        builder.Services.UseQuarkClient(
            configure: options =>
            {
                options.ClientId = "client-only-example";
                options.MaxRetries = 3;
            },
            clientBuilderConfigure: clientBuilder =>
            {
                clientBuilder.WithRedisClustering(connectionString: redisHost);
                clientBuilder.WithGrpcTransport();
            });

        builder.Services.AddHostedService<DemoClientService>();
        var app = builder.Build();
        await app.RunAsync();
    }
}

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
