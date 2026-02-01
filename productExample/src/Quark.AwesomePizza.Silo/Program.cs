using Quark.Extensions.DependencyInjection;
using Quark.Hosting;

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
        ConfigureApp(app);

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

        // Register core services
        applicationBuilder.UseQuark(configure: options => { options.SiloId = siloId; }, siloConfigure: builder =>
        {
            builder.WithGrpcTransport();
            builder.WithRedisClustering(connectionString: redisHost);
            builder.WithStreaming();

            builder.WithServerlessActors();
        });
        var services = applicationBuilder.Services;


        services.AddLogging();
    }

    private static void ConfigureApp(WebApplication app)
    {
        var config = app.Services.GetRequiredService<QuarkSiloOptions>();


        // Display startup banner
        Console.WriteLine("╔══════════════════════════════════════════════════════════╗");
        Console.WriteLine("║       Awesome Pizza - Quark Silo Host                    ║");
        Console.WriteLine("║       Clean Architecture with WebSlimBuilder             ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════╝");
        Console.WriteLine();
        Console.WriteLine($"🏭 Silo ID: {config.SiloId}");
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