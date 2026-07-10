using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Quark.Client;
using Quark.Diagnostics.Abstractions;
using Quark.Performance.Shared;
using Quark.Runtime;
using Quark.Testing.Harness;

namespace Quark.Performance.Fairness;

/// <summary>
///     Measures scheduler fairness: one "hot" grain is hammered continuously by concurrent callers
///     while a handful of "cold" grains keep receiving light traffic. Reports cold-grain call
///     latency with vs. without the hot grain active — the delta is the starvation cost the
///     scheduler's drain-budget yield mechanism (<see cref="SiloRuntimeOptions.SchedulerDrainBudget" />)
///     is meant to bound.
/// </summary>
public static class FairnessRunner
{
    public static async Task RunAsync(string[] args)
    {
        FairnessCliArgs cli = FairnessCliArgs.Parse(args);

        Console.WriteLine("=== Fairness Benchmark ===");
        Console.WriteLine($"  Hot callers: {cli.HotCallers} ({cli.HotWorkMicroseconds}us/call), " +
                           $"Cold grains: {cli.ColdGrains} ({cli.ColdWorkMicroseconds}us/call, " +
                           $"{cli.ColdCallIntervalMs}ms between calls)");
        Console.WriteLine($"  Baseline: {cli.BaselineSeconds}s, Hot phase: {cli.DurationSeconds}s, " +
                           $"SchedulerDrainBudget: {cli.DrainBudget}");
        Console.WriteLine();

        var listener = new FairnessDiagnosticListener();

        await using TestCluster cluster = await TestCluster.CreateAsync(options =>
        {
            options.InitialSilosCount = 1;
            options.ConfigureSiloServices = services =>
            {
                services.AddQuarkRuntime();
                services.Configure<SiloRuntimeOptions>(o => o.SchedulerDrainBudget = cli.DrainBudget);
                // Direct singleton registration -- NOT services.AddQuarkDiagnostics(listener), which
                // has a known circular-DI bug (see docs/superpowers/specs/2026-07-08-astro-sim-benchmark-design.md).
                services.AddSingleton<IQuarkDiagnosticListener>(listener);
                services.AddGrainBehavior<IWorkGrain, WorkGrainBehavior>();
            };
            options.ConfigureClientServices = services =>
            {
                services.AddLocalClusterClient();
                services.AddGrainProxy<IWorkGrain, WorkGrainProxy>();
            };
        });

        var hotGrain = cluster.Client.GetGrain<IWorkGrain>("hot");
        var coldGrains = new IWorkGrain[cli.ColdGrains];
        for (int i = 0; i < cli.ColdGrains; i++)
        {
            coldGrains[i] = cluster.Client.GetGrain<IWorkGrain>($"cold-{i}");
        }

        using var coldBaselineHistogram = new LatencyHistogram();
        using var coldWithHotHistogram = new LatencyHistogram();
        var hotCounters = new PaddedCounter[cli.HotCallers];

        Console.WriteLine("--- Phase 1: baseline (cold grains only) ---");
        using (var baselineCts = new CancellationTokenSource(TimeSpan.FromSeconds(cli.BaselineSeconds)))
        {
            Task[] coldTasks = coldGrains.Select(g =>
                RunColdLoopAsync(g, cli.ColdWorkMicroseconds, cli.ColdCallIntervalMs, coldBaselineHistogram, baselineCts.Token))
                .ToArray();
            await Task.WhenAll(coldTasks);
        }
        Console.WriteLine($"  Baseline cold-grain latency: {coldBaselineHistogram.Merge()}");
        Console.WriteLine();

        Console.WriteLine("--- Phase 2: hot grain active ---");
        using var hotCts = new CancellationTokenSource(TimeSpan.FromSeconds(cli.DurationSeconds));
        var totalSw = Stopwatch.StartNew();

        var hotTasks = new Task[cli.HotCallers];
        for (int i = 0; i < cli.HotCallers; i++)
        {
            int workerIndex = i;
            hotTasks[i] = Task.Run(() => RunHotLoopAsync(hotGrain, cli.HotWorkMicroseconds, hotCts.Token, hotCounters, workerIndex));
        }

        Task[] coldWithHotTasks = coldGrains.Select(g =>
            RunColdLoopAsync(g, cli.ColdWorkMicroseconds, cli.ColdCallIntervalMs, coldWithHotHistogram, hotCts.Token))
            .ToArray();

        await Task.WhenAll(hotTasks);
        await Task.WhenAll(coldWithHotTasks);
        totalSw.Stop();

        long hotCalls = SumCounters(hotCounters);
        double hotSeconds = totalSw.Elapsed.TotalSeconds;

        Console.WriteLine();
        Console.WriteLine("=== Fairness Complete ===");
        Console.WriteLine($"  Hot grain: {hotCalls:N0} calls in {hotSeconds:F1}s = {hotCalls / hotSeconds:N0} calls/s");
        Console.WriteLine($"  Cold-grain latency, baseline (no hot):  {coldBaselineHistogram.Merge()}");
        Console.WriteLine($"  Cold-grain latency, with hot active:    {coldWithHotHistogram.Merge()}");
        Console.WriteLine($"  Scheduler drain-yield events: {listener.DrainYieldedCount:N0}");
        Console.WriteLine("  Re-run at a different --drain-budget to see the fairness/throughput tradeoff.");
    }

    private static async Task RunHotLoopAsync(
        IWorkGrain grain, int workMicroseconds, CancellationToken ct, PaddedCounter[] counters, int workerIndex)
    {
        long local = 0;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await grain.DoWorkAsync(workMicroseconds);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            local++;
            Volatile.Write(ref counters[workerIndex].Value, local);
        }
    }

    private static async Task RunColdLoopAsync(
        IWorkGrain grain, int workMicroseconds, int callIntervalMs, LatencyHistogram histogram, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            long start = Stopwatch.GetTimestamp();
            try
            {
                await grain.DoWorkAsync(workMicroseconds);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            histogram.Record(Stopwatch.GetElapsedTime(start).TotalMicroseconds);

            try
            {
                await Task.Delay(callIntervalMs, ct);
            }
            catch (OperationCanceledException)
            {
                break;
            }
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
}

internal sealed class FairnessCliArgs
{
    public int HotCallers { get; private init; } = Environment.ProcessorCount;
    public int HotWorkMicroseconds { get; private init; } = 200;
    public int ColdGrains { get; private init; } = 8;
    public int ColdWorkMicroseconds { get; private init; } = 10;
    public int ColdCallIntervalMs { get; private init; } = 50;
    public double BaselineSeconds { get; private init; } = 3;
    public double DurationSeconds { get; private init; } = 10;
    public int DrainBudget { get; private init; } = 64;

    public static FairnessCliArgs Parse(string[] args)
    {
        int hotCallers = Environment.ProcessorCount;
        int hotWorkUs = 200;
        int coldGrains = 8;
        int coldWorkUs = 10;
        int coldCallIntervalMs = 50;
        double baselineSeconds = 3;
        double duration = 10;
        int drainBudget = 64;

        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--hot-callers" when i + 1 < args.Length:
                    hotCallers = int.Parse(args[++i]);
                    break;
                case "--hot-work-us" when i + 1 < args.Length:
                    hotWorkUs = int.Parse(args[++i]);
                    break;
                case "--cold-grains" when i + 1 < args.Length:
                    coldGrains = int.Parse(args[++i]);
                    break;
                case "--cold-work-us" when i + 1 < args.Length:
                    coldWorkUs = int.Parse(args[++i]);
                    break;
                case "--cold-call-interval-ms" when i + 1 < args.Length:
                    coldCallIntervalMs = int.Parse(args[++i]);
                    break;
                case "--baseline-seconds" when i + 1 < args.Length:
                    baselineSeconds = double.Parse(args[++i]);
                    break;
                case "--duration" when i + 1 < args.Length:
                    duration = double.Parse(args[++i]);
                    break;
                case "--drain-budget" when i + 1 < args.Length:
                    drainBudget = int.Parse(args[++i]);
                    break;
            }
        }

        return new FairnessCliArgs
        {
            HotCallers = hotCallers,
            HotWorkMicroseconds = hotWorkUs,
            ColdGrains = coldGrains,
            ColdWorkMicroseconds = coldWorkUs,
            ColdCallIntervalMs = coldCallIntervalMs,
            BaselineSeconds = baselineSeconds,
            DurationSeconds = duration,
            DrainBudget = drainBudget,
        };
    }
}
