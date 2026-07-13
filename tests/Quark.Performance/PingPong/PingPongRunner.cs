using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Quark.Client;
using Quark.Core.Abstractions.Identity;
using Quark.Diagnostics;
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

        string schedulerName = cli.Bare ? "SimpleActivationScheduler" : cli.SchedulerV2 ? "ArenaScheduler (V2)" : "ActivationScheduler (legacy)";

        Console.WriteLine("=== Ping-Pong Throughput Benchmark ===");
        Console.WriteLine($"  Pairs: {cli.Pairs}, Duration: {cli.DurationSeconds}s, Mode: {mode}");
        Console.WriteLine($"  Scheduler: {schedulerName}");
        Console.WriteLine($"  Scheduler workers: {cli.SchedulerWorkers}{(cli.Bare ? " (ignored -- --bare bypasses the scheduler entirely)" : "")}");
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

        // One padded counter PER PAIR, not one shared counter -- a single Interlocked.Increment target
        // hammered from every pair's thread becomes the bottleneck itself at high call rates (measured:
        // a shared counter caps aggregate throughput and can even regress as more threads contend for it,
        // while padded per-thread counters scale cleanly). Padded to a full cache line so adjacent pairs'
        // counters can't false-share. See design spec §16.
        var counters = new PaddedCounter[cli.Pairs];
        var pairs = new (IPingable ping, IPingable pong)[cli.Pairs];

        await using TestCluster? cluster = cli.Bare ? null : await TestCluster.CreateAsync(options =>
        {
            options.InitialSilosCount = 1;
            options.ConfigureSiloServices = services =>
            {
                services.AddQuarkRuntime();
                services.Configure<SiloRuntimeOptions>(o => o.SchedulerMaxConcurrentActivations = cli.SchedulerWorkers);

                // AddQuarkRuntime() TryAdd-registers SimpleActivationScheduler (unbounded Task.Run
                // per activation) as the default IActivationScheduler -- see the "KNOWN HAZARD" remark
                // on RuntimeServiceCollectionExtensions.AddQuarkRuntime: the sharded ActivationScheduler
                // under investigation here is NOT actually the runtime's default dispatch path, because
                // of a documented reentrancy deadlock for grain-to-grain nested calls. That hazard needs
                // a nested PostAsync from inside a worker's own drain; PingPong's volley is entirely
                // client-driven (RunPairAsync calls the grain proxy from an external Task, never from
                // inside another grain's behavior method), so it cannot trigger that deadlock -- safe to
                // force ActivationScheduler here so --scheduler-workers has any effect at all. A plain
                // AddSingleton after AddQuarkRuntime() wins DI's last-registration-wins resolution over
                // the TryAdd default. See docs/superpowers/specs/2026-07-12-scheduler-sweep-scaling-investigation.md.
                // --v2 selects the next-generation ArenaScheduler (dedicated worker threads,
                // per-worker work-stealing deques — see
                // docs/superpowers/specs/2026-07-12-next-gen-scheduler-design.md). Same
                // last-registration-wins override as the legacy path below.
                if (cli.SchedulerV2)
                {
                    services.AddSingleton<IActivationScheduler>(sp => new ArenaScheduler(
                        sp.GetRequiredService<IOptions<SiloRuntimeOptions>>().Value,
                        sp.GetService<IQuarkDiagnosticListener>()));
                }
                else
                {
                    services.AddSingleton<IActivationScheduler>(sp => new ActivationScheduler(
                        sp.GetRequiredService<IOptions<SiloRuntimeOptions>>().Value,
                        sp.GetService<IQuarkDiagnosticListener>()));
                }

                services.AddQuarkDiagnostics(new BenchmarkDiagnosticListener());
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
                    GrainId.Create(grainType, $"ping-{i}"), grainType, isReentrant: true, bareRoot, logger,
                    SimpleActivationScheduler.Instance);
                var pongActivation = new GrainActivation(
                    GrainId.Create(grainType, $"pong-{i}"), grainType, isReentrant: true, bareRoot, logger,
                    SimpleActivationScheduler.Instance);
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
            int pairIndex = i;
            pairTasks[i] = Task.Run(() => RunPairAsync(ping, pong, cts.Token, counters, pairIndex));
        }

        Task reportTask = Task.Run(async () =>
        {
            long lastCount = 0;
            double lastElapsed = 0;

            while (!cts.IsCancellationRequested)
            {
                await Task.Delay(1000);
                long count = SumCounters(counters);
                double elapsed = totalSw.Elapsed.TotalSeconds;
                if (elapsed <= 0)
                {
                    continue;
                }

                long intervalDelta = count - lastCount;
                double intervalSeconds = elapsed - lastElapsed;
                double instantaneous = intervalSeconds > 0 ? intervalDelta / intervalSeconds : 0;

                // Cumulative avg is dragged down by JIT/thread-pool warm-up in the first ~1-2s and takes
                // many seconds to visually converge even after the real (instantaneous) rate has already
                // flattened out — report both so a slow-looking cumulative curve isn't mistaken for an
                // ongoing ramp. See docs/superpowers/specs/2026-07-08-pingpong-benchmark-design.md §11.
                Console.WriteLine($"  t={elapsed:F0}s  {count / elapsed:N0} calls/s (cumulative avg)  |  {instantaneous:N0} calls/s (last {intervalSeconds:F1}s)");

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

        long totalCalls = SumCounters(counters);
        double totalSeconds = totalSw.Elapsed.TotalSeconds;

        Console.WriteLine();
        Console.WriteLine("=== Ping-Pong Complete ===");
        Console.WriteLine($"  Pairs: {cli.Pairs}");
        Console.WriteLine($"  Duration: {totalSeconds:F1}s");
        Console.WriteLine($"  Raw grain calls: {totalCalls:N0}");
        Console.WriteLine($"  Call rate: {totalCalls / totalSeconds:N0} calls/s");
    }

    private static async Task RunPairAsync(IPingable ping, IPingable pong, CancellationToken ct, PaddedCounter[] counters, int pairIndex)
    {
        IPingable[] targets = [pong, ping];
        long i = 0;
        long local = 0;
        while (!ct.IsCancellationRequested)
        {
            await targets[i % 2].PingAsync();
            local++;
            i++;

            // Every pair writes only its OWN padded slot -- never touched by another thread -- so a
            // Volatile.Write (a plain fenced store, no LOCK-prefixed instruction) is enough for the
            // reporting thread to see progress; no Interlocked op or cross-core contention here, unlike
            // the single shared counter this replaced. See design spec §16.
            Volatile.Write(ref counters[pairIndex].Value, local);
        }
    }

    private static long SumCounters(PaddedCounter[] counters)
    {
        long total = 0;
        for (int i = 0; i < counters.Length; i++)
        {
            total += Volatile.Read(ref counters[i].Value);
        }

        return total;
    }

    /// <summary>
    ///     Padded to a full cache line (64 bytes on virtually all current hardware) so adjacent pairs'
    ///     counters in the same array never share a cache line -- eliminates false sharing between pairs
    ///     even though each slot is written by exactly one thread.
    /// </summary>
    [StructLayout(LayoutKind.Explicit, Size = 64)]
    private struct PaddedCounter
    {
        [FieldOffset(0)]
        public long Value;
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
    public int SchedulerWorkers { get; private init; } = Environment.ProcessorCount;
    public double DurationSeconds { get; private init; } = 10;
    public bool Reentrant { get; private init; }
    public bool Bare { get; private init; }
    public bool SchedulerV2 { get; private init; }

    public static PingPongCliArgs Parse(string[] args)
    {
        int pairs = Environment.ProcessorCount;
        int schedulerWorkers = Environment.ProcessorCount;
        double duration = 10;
        bool reentrant = false;
        bool bare = false;
        bool schedulerV2 = false;

        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--pairs" when i + 1 < args.Length:
                    pairs = int.Parse(args[++i]);
                    break;
                case "--scheduler-workers" when i + 1 < args.Length:
                    schedulerWorkers = int.Parse(args[++i]);
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
                case "--v2":
                    schedulerV2 = true;
                    break;
            }
        }

        return new PingPongCliArgs
        {
            Pairs = pairs,
            SchedulerWorkers = schedulerWorkers,
            DurationSeconds = duration,
            Reentrant = reentrant,
            Bare = bare,
            SchedulerV2 = schedulerV2,
        };
    }
}
