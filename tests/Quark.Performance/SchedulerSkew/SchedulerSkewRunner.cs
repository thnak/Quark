using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Quark.Client;
using Quark.Diagnostics.Abstractions;
using Quark.Performance.PingPong;
using Quark.Runtime;
using Quark.Testing.Harness;

namespace Quark.Performance.SchedulerSkew;

/// <summary>
///     Reproduces the "few hot activations against a large configured N" scenario from issue #164:
///     a small, fixed number of continuously-volleying "hot"
///     ping/pong pairs (<c>--hot-pairs</c>) run against an independently configured, much larger
///     <see cref="SiloRuntimeOptions.SchedulerMaxConcurrentActivations" /> (<c>--scheduler-workers</c>).
///     Unlike <c>PingPong --pairs N --scheduler-workers M</c> (which reports only aggregate calls/s),
///     this runner also reports ready-queue wait-time percentiles via
///     <see cref="SchedulerSkewDiagnosticListener" /> -- a sharper signal of idle-worker sweep/park
///     overhead per hop than throughput alone, since throughput conflates every other per-call cost
///     too. Reuses the existing <see cref="IPingPongGrain" />/<see cref="PingPongGrainBehavior" />/
///     <see cref="PingPongGrainProxy" /> workload -- only the CLI surface, worker-count decoupling,
///     and instrumentation are new. See
///     docs/superpowers/specs/2026-07-12-scheduler-sweep-scaling-investigation.md.
/// </summary>
public static class SchedulerSkewRunner
{
    public static async Task RunAsync(string[] args)
    {
        SchedulerSkewCliArgs cli = SchedulerSkewCliArgs.Parse(args);

        Console.WriteLine("=== Scheduler Skew Benchmark ===");
        Console.WriteLine($"  Hot pairs: {cli.HotPairs}, Scheduler workers: {cli.SchedulerWorkers}, Duration: {cli.DurationSeconds}s");
        Console.WriteLine($"  {cli.HotPairs} pairs ({cli.HotPairs * 2} activations) against {cli.SchedulerWorkers} configured shards/workers --");
        Console.WriteLine($"  most of the {cli.SchedulerWorkers} shards sit permanently empty for this run's duration.");
        Console.WriteLine();

        using var listener = new SchedulerSkewDiagnosticListener();

        await using TestCluster cluster = await TestCluster.CreateAsync(options =>
        {
            options.InitialSilosCount = 1;
            options.ConfigureSiloServices = services =>
            {
                services.AddQuarkRuntime();
                services.Configure<SiloRuntimeOptions>(o => o.SchedulerMaxConcurrentActivations = cli.SchedulerWorkers);

                // Force the sharded ActivationScheduler in place of AddQuarkRuntime()'s default
                // SimpleActivationScheduler fallback -- see the matching comment in PingPongRunner.cs
                // for why that default exists (a documented reentrancy deadlock for nested grain-to-
                // grain calls) and why it doesn't apply to this runner's client-driven volley shape.
                // A plain AddSingleton after AddQuarkRuntime() wins DI's last-registration-wins
                // resolution over AddQuarkRuntime()'s TryAdd default.
                services.AddSingleton<IActivationScheduler>(sp => new ActivationScheduler(
                    sp.GetRequiredService<IOptions<SiloRuntimeOptions>>().Value,
                    sp.GetService<IQuarkDiagnosticListener>()));

                // Deliberately NOT AddQuarkDiagnostics(listener) here: that helper's EnsureComposite
                // uses TryAddSingleton<IQuarkDiagnosticListener>, which loses to AddQuarkRuntime()'s own
                // TryAddSingleton<IQuarkDiagnosticListener>(NullDiagnosticListener.Instance) above when
                // (as here) AddQuarkRuntime() is called first -- the listener would silently never wire
                // up. A plain AddSingleton always appends a new descriptor and wins single-resolution
                // (GetService<T>/GetRequiredService<T>) regardless of call order, so it's used directly.
                services.AddSingleton<IQuarkDiagnosticListener>(listener);

                services.AddGrainBehavior<IPingPongGrain, PingPongGrainBehavior>();
            };
            options.ConfigureClientServices = services =>
            {
                services.AddLocalClusterClient();
                services.AddGrainProxy<IPingPongGrain, PingPongGrainProxy>();
            };
        });

        var counters = new PaddedCounter[cli.HotPairs];
        var pairs = new (IPingPongGrain ping, IPingPongGrain pong)[cli.HotPairs];
        for (int i = 0; i < cli.HotPairs; i++)
        {
            pairs[i] = (cluster.Client.GetGrain<IPingPongGrain>($"skew-ping-{i}"),
                        cluster.Client.GetGrain<IPingPongGrain>($"skew-pong-{i}"));
        }

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(cli.DurationSeconds));
        var totalSw = Stopwatch.StartNew();

        var pairTasks = new Task[cli.HotPairs];
        for (int i = 0; i < cli.HotPairs; i++)
        {
            (IPingPongGrain ping, IPingPongGrain pong) = pairs[i];
            int pairIndex = i;
            pairTasks[i] = Task.Run(() => RunPairAsync(ping, pong, cts.Token, counters, pairIndex));
        }

        await Task.WhenAll(pairTasks);
        totalSw.Stop();

        long totalCalls = SumCounters(counters);
        double totalSeconds = totalSw.Elapsed.TotalSeconds;

        Console.WriteLine("=== Scheduler Skew Complete ===");
        Console.WriteLine($"  Hot pairs: {cli.HotPairs}, Scheduler workers: {cli.SchedulerWorkers}");
        Console.WriteLine($"  Duration: {totalSeconds:F1}s");
        Console.WriteLine($"  Raw grain calls: {totalCalls:N0}");
        Console.WriteLine($"  Call rate: {totalCalls / totalSeconds:N0} calls/s");
        Console.WriteLine($"  Ready-queue wait time: {listener.WaitHistogram.Merge()}");
    }

    private static async Task RunPairAsync(IPingPongGrain ping, IPingPongGrain pong, CancellationToken ct, PaddedCounter[] counters, int pairIndex)
    {
        IPingPongGrain[] targets = [pong, ping];
        long i = 0;
        long local = 0;
        while (!ct.IsCancellationRequested)
        {
            await targets[i % 2].PingAsync();
            local++;
            i++;
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

    /// <summary>Padded to a full cache line -- see <c>PingPongRunner.PaddedCounter</c> for the rationale.</summary>
    [StructLayout(LayoutKind.Explicit, Size = 64)]
    private struct PaddedCounter
    {
        [FieldOffset(0)]
        public long Value;
    }
}

internal sealed class SchedulerSkewCliArgs
{
    public int HotPairs { get; private init; } = 2;
    public int SchedulerWorkers { get; private init; } = 128;
    public double DurationSeconds { get; private init; } = 10;

    public static SchedulerSkewCliArgs Parse(string[] args)
    {
        int hotPairs = 2;
        int schedulerWorkers = 128;
        double duration = 10;

        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--hot-pairs" when i + 1 < args.Length:
                    hotPairs = int.Parse(args[++i]);
                    break;
                case "--scheduler-workers" when i + 1 < args.Length:
                    schedulerWorkers = int.Parse(args[++i]);
                    break;
                case "--duration" when i + 1 < args.Length:
                    duration = double.Parse(args[++i]);
                    break;
            }
        }

        return new SchedulerSkewCliArgs { HotPairs = hotPairs, SchedulerWorkers = schedulerWorkers, DurationSeconds = duration };
    }
}
