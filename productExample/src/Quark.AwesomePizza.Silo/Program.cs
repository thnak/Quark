using Quark.Abstractions;
using Quark.AwesomePizza.Silo.BackgroundServices;
using Quark.AwesomePizza.Silo.Configs;
using Quark.Client;
using Quark.Extensions.DependencyInjection;

namespace Quark.AwesomePizza.Silo;

/// <summary>
/// Awesome Pizza Silo - Central actor host using WebApplication.CreateSlimBuilder.
/// This is the CENTRAL actor host where ALL actors live.
/// Uses clean architecture with proper DI and IClusterClient pattern.
/// </summary>
public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateSlimBuilder(args);

        // Configure services
        ConfigureServices(builder, builder.Configuration);

        var app = builder.Build();

        // Configure app
        await ConfigureApp(app);

        // Run the application
        await app.RunAsync();
    }

    private static void ConfigureServices(IHostApplicationBuilder applicationBuilder,
        IConfiguration configuration)
    {
        // Get configuration values
        var siloId = Environment.GetEnvironmentVariable("SILO_ID")
                     ?? configuration["Silo:Id"]
                     ?? $"silo-{Guid.NewGuid():N}";

        var redisHost = Environment.GetEnvironmentVariable("REDIS_HOST")
                        ?? configuration["Redis:Host"]
                        ?? "localhost";

        var redisPort = Environment.GetEnvironmentVariable("REDIS_PORT")
                        ?? configuration["Redis:Port"]
                        ?? "6379";

        var mqttHost = Environment.GetEnvironmentVariable("MQTT_HOST")
                       ?? configuration["Mqtt:Host"]
                       ?? "localhost";

        var mqttPort = int.Parse(Environment.GetEnvironmentVariable("MQTT_PORT")
                                 ?? configuration["Mqtt:Port"]
                                 ?? "1883");

        // Register core services
        applicationBuilder.UseQuark(configure: options => { options.SiloId = siloId; }, siloConfigure: builder =>
        {
            builder.WithGrpcTransport();
            builder.WithRedisClustering(connectionString: redisHost);
            builder.WithStreaming();
            
            builder.WithServerlessActors();
        });
        var services = applicationBuilder.Services;

        // Register MQTT service
        services.AddSingleton<MqttService>(sp =>
        {
            var actorFactory = sp.GetRequiredService<IActorFactory>();
            var activeActors = new Dictionary<string, IActor>(); // TODO: Share with cluster client
            return new MqttService(actorFactory, activeActors, mqttHost, mqttPort);
        });

        services.AddHostedService<MqttHostedService>();

        // Store configuration for later use
        services.AddSingleton(new SiloConfiguration
        {
            SiloId = siloId,
            RedisHost = redisHost,
            RedisPort = redisPort,
            MqttHost = mqttHost,
            MqttPort = mqttPort
        });
    }

    private static async Task ConfigureApp(WebApplication app)
    {
        var config = app.Services.GetRequiredService<SiloConfiguration>();
        var clusterClient = app.Services.GetRequiredService<IClusterClient>();

        // Connect to cluster (in-process for now)
        await clusterClient.ConnectAsync();

        // Display startup banner
        Console.WriteLine("╔══════════════════════════════════════════════════════════╗");
        Console.WriteLine("║       Awesome Pizza - Quark Silo Host                    ║");
        Console.WriteLine("║       Clean Architecture with WebSlimBuilder             ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════╝");
        Console.WriteLine();
        Console.WriteLine($"🏭 Silo ID: {config.SiloId}");
        Console.WriteLine($"🔌 Redis:   {config.RedisHost}:{config.RedisPort}");
        Console.WriteLine($"🔌 MQTT:    {config.MqttHost}:{config.MqttPort}");
        Console.WriteLine($"⚡ Clean Architecture: Enabled");
        Console.WriteLine($"🚀 Started at: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
        Console.WriteLine();
        Console.WriteLine("✅ Silo is ready - All actors live here!");
        Console.WriteLine("📋 Actor types: Order, Driver, Chef, Kitchen, Inventory, Restaurant");
        Console.WriteLine();
        Console.WriteLine("💡 Architecture:");
        Console.WriteLine("   • Silo = Central actor host (WebSlimBuilder + DI)");
        Console.WriteLine("   • Actors = Hosted in Silo, accessed via IClusterClient");
        Console.WriteLine("   • Gateway = Uses IClusterClient to call actors");
        Console.WriteLine("   • MQTT = Uses IClusterClient to update actors");
        Console.WriteLine();
    }
}