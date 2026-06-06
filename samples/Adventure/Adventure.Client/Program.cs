using Adventure.GrainInterfaces;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Quark.Client;
using Quark.Client.Tcp;
using Quark.Core;
using Quark.Core.Abstractions.Hosting;

var host = Host.CreateDefaultBuilder(args)
    .UseQuarkClient(client =>
    {
        client.UseLocalhostGateway();
        client.Services.AddGrainProxy<IPlayerGrain, PlayerGrainProxy>();
    })
    .Build();

await host.StartAsync();

var grainFactory = host.Services.GetRequiredService<IGrainFactory>();

Console.Write("Enter your name: ");
var name = Console.ReadLine()?.Trim();
if (string.IsNullOrEmpty(name)) name = "Adventurer";

var playerKey = Guid.NewGuid();
var player = grainFactory.GetGrain<IPlayerGrain>(playerKey);
await player.SetInfoAsync(name);

Console.WriteLine(await player.DescribeAsync());
Console.WriteLine("Commands: look, go <dir>, take <item>, drop <item>, inv, kill <monster>, quit");
Console.WriteLine();

while (true)
{
    Console.Write("> ");
    var input = Console.ReadLine()?.Trim() ?? "";
    if (input.Equals("quit", StringComparison.OrdinalIgnoreCase)) break;
    if (string.IsNullOrEmpty(input)) continue;

    var parts = input.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
    var verb = parts[0].ToLowerInvariant();
    var arg  = parts.Length > 1 ? parts[1] : "";

    var response = verb switch
    {
        "look"  => await player.DescribeAsync(),
        "go"    => await player.GoAsync(arg),
        "take"  => await player.PickUpAsync(arg),
        "drop"  => await player.DropAsync(arg),
        "inv"   => await player.GetInventoryAsync(),
        "kill"  => await player.KillAsync(arg),
        _       => "Unknown command. Try: look, go <dir>, take <item>, drop <item>, inv, kill <monster>, quit"
    };

    Console.WriteLine(response);
}

await host.StopAsync();
