using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
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