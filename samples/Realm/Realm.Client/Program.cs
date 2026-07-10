using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Quark.Client;
using Quark.Client.Tcp;
using Quark.Core;
using Quark.Core.Abstractions.Hosting;
using Realm.Common;
using Realm.Common.Dtos;
using Realm.GrainInterfaces;

BotDriverOptions options = BotDriverOptions.Parse(args);
Console.WriteLine(
    $"Realm bot driver: {options.PlayerCount} players @ {options.RateHz:0.##} moves/sec/bot " +
    $"for {options.Duration.TotalSeconds:0}s against gateway port {options.GatewayPort}...");

using IHost host = Host.CreateDefaultBuilder(args)
    .UseQuarkClient(client =>
    {
        client.UseLocalhostGateway(options.GatewayPort);
        client.Services.AddRealmGrainInterfacesGrainProxies();
    })
    .Build();

await host.StartAsync();
IClusterClient cluster = host.Services.GetRequiredService<IClusterClient>();

// Log every bot in through IWorldGrain first so we know (deterministically — LoginAsync is a
// pure hash of playerId) which map each bot lands on, before driving movement traffic.
IWorldGrain world = cluster.GetGrain<IWorldGrain>(RealmConstants.WorldKey);
var bots = new (string Id, IPlayerGrain Player, string MapId)[options.PlayerCount];
for (int i = 0; i < options.PlayerCount; i++)
{
    string id = $"bot-{Guid.NewGuid():N}";
    PlayerSpawn spawn = await world.LoginAsync(id);
    bots[i] = (id, cluster.GetGrain<IPlayerGrain>(id), spawn.MapId);
    await bots[i].Player.LoginAsync();
}

int distinctMaps = bots.Select(b => b.MapId).Distinct(StringComparer.Ordinal).Count();
Console.WriteLine($"Bots spread across {distinctMaps} map(s).");

Direction[] directions = [Direction.North, Direction.South, Direction.East, Direction.West];
var latenciesMs = new System.Collections.Concurrent.ConcurrentBag<double>();
long totalMoves = 0;
long failedMoves = 0;

using var cts = new CancellationTokenSource(options.Duration);
TimeSpan interval = TimeSpan.FromSeconds(1.0 / options.RateHz);

async Task RunBotAsync(IPlayerGrain player, int seed)
{
    var rng = new Random(seed);
    while (!cts.IsCancellationRequested)
    {
        Direction dir = directions[rng.Next(directions.Length)];
        long startedAt = Stopwatch.GetTimestamp();
        try
        {
            await player.MoveAsync(dir);
            Interlocked.Increment(ref totalMoves);
            latenciesMs.Add(Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
        }
        catch
        {
            Interlocked.Increment(ref failedMoves);
        }

        try { await Task.Delay(interval, cts.Token); }
        catch (OperationCanceledException) { break; }
    }
}

Stopwatch overall = Stopwatch.StartNew();
await Task.WhenAll(bots.Select((b, i) => RunBotAsync(b.Player, i)));
overall.Stop();

long totalAoiDeltas = 0;
foreach ((string _, IPlayerGrain player, string _) in bots)
{
    AoiStatus status = await player.GetAoiStatusAsync();
    totalAoiDeltas += status.ReceivedDeltaCount;
    await player.LogoutAsync();
}

double[] sorted = [.. latenciesMs.OrderBy(x => x)];
double Percentile(double pct) => sorted.Length == 0
    ? 0
    : sorted[(int)Math.Clamp(Math.Ceiling(pct / 100.0 * sorted.Length) - 1, 0, sorted.Length - 1)];

Console.WriteLine("--- Results ---");
Console.WriteLine($"Duration:         {overall.Elapsed.TotalSeconds:0.00}s");
Console.WriteLine($"Total moves:      {totalMoves} ({failedMoves} failed)");
Console.WriteLine($"Throughput:       {totalMoves / overall.Elapsed.TotalSeconds:0.0} moves/sec");
Console.WriteLine($"Move latency p50: {Percentile(50):0.00} ms");
Console.WriteLine($"Move latency p99: {Percentile(99):0.00} ms");
Console.WriteLine($"Move latency max: {(sorted.Length > 0 ? sorted[^1] : 0):0.00} ms");
Console.WriteLine($"AoI deltas recv:  {totalAoiDeltas} (avg {(bots.Length > 0 ? (double)totalAoiDeltas / bots.Length : 0):0.0}/bot)");

await host.StopAsync();

internal sealed class BotDriverOptions
{
    public int PlayerCount { get; private set; } = 20;
    public double RateHz { get; private set; } = 2.0;
    public TimeSpan Duration { get; private set; } = TimeSpan.FromSeconds(15);
    public int GatewayPort { get; private set; } = 30010;

    public static BotDriverOptions Parse(string[] args)
    {
        var options = new BotDriverOptions();
        for (int i = 0; i < args.Length - 1; i++)
        {
            switch (args[i])
            {
                case "--players":
                    options.PlayerCount = int.Parse(args[++i]);
                    break;
                case "--rate":
                    options.RateHz = double.Parse(args[++i]);
                    break;
                case "--duration":
                    options.Duration = TimeSpan.FromSeconds(double.Parse(args[++i]));
                    break;
                case "--gateway-port":
                    options.GatewayPort = int.Parse(args[++i]);
                    break;
            }
        }
        return options;
    }
}
