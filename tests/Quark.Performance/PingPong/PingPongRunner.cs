using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quark.Client;
using Quark.Core.Abstractions.Identity;
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
        string mode = cli.Bare ? "Bare (no DI/scope resolution)" : cli.Reentrant ? "Reentrant" : "Non-reentrant";

        Console.WriteLine("=== Ping-Pong Throughput Benchmark ===");
        Console.WriteLine($"  Pairs: {cli.Pairs}, Duration: {cli.DurationSeconds}s, Mode: {mode}");
        Console.WriteLine("  Note: reported msg/s is 2x the raw grain-call count, approximating Akka's");
        Console.WriteLine("  one-way-tell convention (ping leg + pong leg per round trip).");
        if (cli.Bare)
        {
            Console.WriteLine("  Bare mode: bypasses LocalGrainCallInvoker/GrainScopeBinder entirely -- posts");
            Console.WriteLine("  directly to a bare, [Reentrant]-scheduled GrainActivation backed by ONE shared");
            Console.WriteLine("  behavior instance (no per-call DI scope, no ResolveService, no behavior");
            Console.WriteLine("  construction). Experimental upper-bound estimate for removing DI resolution");
            Console.WriteLine("  entirely -- not a supported dispatch path. See design spec §15.");
        }
        else if (cli.Reentrant)
        {
            Console.WriteLine("  Reentrant mode: PostAsync calls the work item inline (see");
            Console.WriteLine("  GrainActivation.PostAsync) -- bypasses the mailbox channel and its forced-async");
            Console.WriteLine("  completion signal entirely. Compare against a non-reentrant run at the same");
            Console.WriteLine("  --pairs/--duration to measure that gap end-to-end.");
        }
        Console.WriteLine();

        long[] counter = [0];
        var pairs = new (IPingable ping, IPingable pong)[cli.Pairs];

        await using TestCluster? cluster = cli.Bare ? null : await TestCluster.CreateAsync(options =>
        {
            options.InitialSilosCount = 1;
            options.ConfigureSiloServices = services =>
            {
                services.AddQuarkRuntime();
                // NOT services.AddQuarkDiagnostics(listener) -- confirmed circular-DI bug, see
                // docs/superpowers/specs/2026-07-08-astro-sim-benchmark-design.md section 5.
                services.AddSingleton<IQuarkDiagnosticListener>(new BenchmarkDiagnosticListener());
                services.AddGrainBehavior<IPingPongGrain, PingPongGrainBehavior>();
                services.AddGrainBehavior<IReentrantPingPongGrain, ReentrantPingPongGrainBehavior>();
            };
            options.ConfigureClientServices = services =>
            {
                services.AddLocalClusterClient();
                services.AddGrainProxy<IPingPongGrain, PingPongGrainProxy>();
                services.AddGrainProxy<IReentrantPingPongGrain, ReentrantPingPongGrainProxy>();
            };
        });

        var bareActivations = new List<GrainActivation>();

        if (cli.Bare)
        {
            // Minimal root DI, just to satisfy GrainActivation's constructor -- no grain behaviors,
            // no scopes, no ResolveService ever runs on the per-call path below.
            IServiceProvider bareRoot = new ServiceCollection().AddLogging().BuildServiceProvider();
            ILogger<GrainActivation> logger = bareRoot.GetRequiredService<ILogger<GrainActivation>>();
            var behavior = new ReentrantPingPongGrainBehavior();
            var grainType = new GrainType("BarePingPongGrain");

            for (int i = 0; i < cli.Pairs; i++)
            {
                var pingActivation = new GrainActivation(
                    GrainId.Create(grainType, $"ping-{i}"), grainType, isReentrant: true, bareRoot, logger);
                var pongActivation = new GrainActivation(
                    GrainId.Create(grainType, $"pong-{i}"), grainType, isReentrant: true, bareRoot, logger);
                bareActivations.Add(pingActivation);
                bareActivations.Add(pongActivation);
                pairs[i] = (new BareActivationPingable(pingActivation, behavior), new BareActivationPingable(pongActivation, behavior));
            }
        }
        else
        {
            for (int i = 0; i < cli.Pairs; i++)
            {
                pairs[i] = cli.Reentrant
                    ? (cluster!.Client.GetGrain<IReentrantPingPongGrain>($"ping-{i}"),
                       cluster.Client.GetGrain<IReentrantPingPongGrain>($"pong-{i}"))
                    : (cluster!.Client.GetGrain<IPingPongGrain>($"ping-{i}"),
                       cluster.Client.GetGrain<IPingPongGrain>($"pong-{i}"));
            }
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(cli.DurationSeconds));
        var totalSw = Stopwatch.StartNew();

        var pairTasks = new Task[cli.Pairs];
        for (int i = 0; i < cli.Pairs; i++)
        {
            (IPingable ping, IPingable pong) = pairs[i];
            pairTasks[i] = Task.Run(() => RunPairAsync(ping, pong, cts.Token, counter));
        }

        Task reportTask = Task.Run(async () =>
        {
            long lastCount = 0;
            double lastElapsed = 0;

            while (!cts.IsCancellationRequested)
            {
                await Task.Delay(1000);
                long count = Interlocked.Read(ref counter[0]);
                double elapsed = totalSw.Elapsed.TotalSeconds;
                if (elapsed <= 0)
                {
                    continue;
                }

                long intervalDelta = count - lastCount;
                double intervalSeconds = elapsed - lastElapsed;
                double instantaneous = intervalSeconds > 0 ? 2 * intervalDelta / intervalSeconds : 0;

                // Cumulative avg is dragged down by JIT/thread-pool warm-up in the first ~1-2s and takes
                // many seconds to visually converge even after the real (instantaneous) rate has already
                // flattened out — report both so a slow-looking cumulative curve isn't mistaken for an
                // ongoing ramp. See docs/superpowers/specs/2026-07-08-pingpong-benchmark-design.md §11.
                Console.WriteLine($"  t={elapsed:F0}s  {2 * count / elapsed:N0} msg/s (x2, cumulative avg)  |  {instantaneous:N0} msg/s (x2, last {intervalSeconds:F1}s)");

                lastCount = count;
                lastElapsed = elapsed;
            }
        });

        await Task.WhenAll(pairTasks);
        totalSw.Stop();
        await reportTask;

        foreach (GrainActivation activation in bareActivations)
        {
            await activation.DisposeAsync();
        }

        long totalCalls = Interlocked.Read(ref counter[0]);
        double totalSeconds = totalSw.Elapsed.TotalSeconds;

        Console.WriteLine();
        Console.WriteLine("=== Ping-Pong Complete ===");
        Console.WriteLine($"  Pairs: {cli.Pairs}");
        Console.WriteLine($"  Duration: {totalSeconds:F1}s");
        Console.WriteLine($"  Raw grain calls: {totalCalls:N0}");
        Console.WriteLine($"  Raw call rate: {totalCalls / totalSeconds:N0} calls/s");
        Console.WriteLine($"  Akka-comparable rate (x2): {2 * totalCalls / totalSeconds:N0} msg/s");
    }

    private static async Task RunPairAsync(IPingable ping, IPingable pong, CancellationToken ct, long[] counter)
    {
        IPingable[] targets = [pong, ping];
        long i = 0;
        while (!ct.IsCancellationRequested)
        {
            await targets[i % 2].PingAsync();
            Interlocked.Increment(ref counter[0]);
            i++;
        }
    }

    /// <summary>
    ///     Experimental --bare mode: posts directly to a bare <see cref="GrainActivation"/> backed by a
    ///     single shared behavior instance, bypassing <see cref="LocalGrainCallInvoker"/> and
    ///     <see cref="GrainScopeBinder"/> entirely -- no per-call DI scope, no ResolveService, no behavior
    ///     construction. Measures the ceiling if per-call DI resolution were removed. See design spec §15.
    /// </summary>
    private sealed class BareActivationPingable(GrainActivation activation, ReentrantPingPongGrainBehavior behavior) : IPingable
    {
        public ValueTask PingAsync() => activation.PostAsync(() => behavior.PingAsync());
    }
}

internal sealed class PingPongCliArgs
{
    public int Pairs { get; private init; } = Environment.ProcessorCount;
    public double DurationSeconds { get; private init; } = 10;
    public bool Reentrant { get; private init; }
    public bool Bare { get; private init; }

    public static PingPongCliArgs Parse(string[] args)
    {
        int pairs = Environment.ProcessorCount;
        double duration = 10;
        bool reentrant = false;
        bool bare = false;

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
                case "--reentrant":
                    reentrant = true;
                    break;
                case "--bare":
                    bare = true;
                    break;
            }
        }

        return new PingPongCliArgs { Pairs = pairs, DurationSeconds = duration, Reentrant = reentrant, Bare = bare };
    }
}
