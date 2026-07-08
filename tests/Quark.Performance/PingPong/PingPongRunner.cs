using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Quark.Client;
using Quark.Diagnostics.Abstractions;
using Quark.Performance.AstroSim;
using Quark.Runtime;
using Quark.Testing.Harness;

namespace Quark.Performance.PingPong;

public static class PingPongRunner
{
    public static async Task RunAsync(string[] args)
    {
        PingPongCliArgs cli = PingPongCliArgs.Parse(args);
        var listener = new BenchmarkDiagnosticListener();

        Console.WriteLine("=== Ping-Pong Throughput Benchmark ===");
        Console.WriteLine($"  Pairs: {cli.Pairs}, Duration: {cli.DurationSeconds}s");
        Console.WriteLine("  Note: reported msg/s is 2x the raw grain-call count, approximating Akka's");
        Console.WriteLine("  one-way-tell convention (ping leg + pong leg per round trip).\n");

        await using TestCluster cluster = await TestCluster.CreateAsync(options =>
        {
            options.InitialSilosCount = 1;
            options.ConfigureSiloServices = services =>
            {
                services.AddQuarkRuntime();
                // NOT services.AddQuarkDiagnostics(listener) -- confirmed circular-DI bug, see
                // docs/superpowers/specs/2026-07-08-astro-sim-benchmark-design.md section 5.
                services.AddSingleton<IQuarkDiagnosticListener>(listener);
                services.AddGrainBehavior<IPingPongGrain, PingPongGrainBehavior>();
            };
            options.ConfigureClientServices = services =>
            {
                services.AddLocalClusterClient();
                services.AddGrainProxy<IPingPongGrain, PingPongGrainProxy>();
            };
        });

        var pairs = new (IPingPongGrain ping, IPingPongGrain pong)[cli.Pairs];
        for (int i = 0; i < cli.Pairs; i++)
        {
            pairs[i] = (
                cluster.Client.GetGrain<IPingPongGrain>($"ping-{i}"),
                cluster.Client.GetGrain<IPingPongGrain>($"pong-{i}"));
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(cli.DurationSeconds));
        long startCount = listener.Count;
        var totalSw = Stopwatch.StartNew();

        var pairTasks = new Task[cli.Pairs];
        for (int i = 0; i < cli.Pairs; i++)
        {
            (IPingPongGrain ping, IPingPongGrain pong) = pairs[i];
            pairTasks[i] = Task.Run(() => RunPairAsync(ping, pong, cts.Token));
        }

        Task reportTask = Task.Run(async () =>
        {
            while (!cts.IsCancellationRequested)
            {
                await Task.Delay(1000);
                long delta = listener.Count - startCount;
                double elapsed = totalSw.Elapsed.TotalSeconds;
                if (elapsed > 0)
                {
                    Console.WriteLine($"  t={elapsed:F0}s  {2 * delta / elapsed:N0} msg/s (x2, cumulative avg)");
                }
            }
        });

        await Task.WhenAll(pairTasks);
        totalSw.Stop();
        await reportTask;

        long totalCalls = listener.Count - startCount;
        double totalSeconds = totalSw.Elapsed.TotalSeconds;

        Console.WriteLine();
        Console.WriteLine("=== Ping-Pong Complete ===");
        Console.WriteLine($"  Pairs: {cli.Pairs}");
        Console.WriteLine($"  Duration: {totalSeconds:F1}s");
        Console.WriteLine($"  Raw grain calls: {totalCalls:N0}");
        Console.WriteLine($"  Raw call rate: {totalCalls / totalSeconds:N0} calls/s");
        Console.WriteLine($"  Akka-comparable rate (x2): {2 * totalCalls / totalSeconds:N0} msg/s");
    }

    private static async Task RunPairAsync(IPingPongGrain ping, IPingPongGrain pong, CancellationToken ct)
    {
        IPingPongGrain[] targets = [pong, ping];
        long i = 0;
        while (!ct.IsCancellationRequested)
        {
            await targets[i % 2].PingAsync();
            i++;
        }
    }
}

internal sealed class PingPongCliArgs
{
    public int Pairs { get; private init; } = Environment.ProcessorCount;
    public double DurationSeconds { get; private init; } = 10;

    public static PingPongCliArgs Parse(string[] args)
    {
        int pairs = Environment.ProcessorCount;
        double duration = 10;

        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--pairs" when i + 1 < args.Length:
                    pairs = int.Parse(args[++i]);
                    break;
                case "--duration" when i + 1 < args.Length:
                    duration = double.Parse(args[++i]);
                    break;
            }
        }

        return new PingPongCliArgs { Pairs = pairs, DurationSeconds = duration };
    }
}
