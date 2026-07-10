using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Quark.Client;
using Quark.Performance.Shared;
using Quark.Runtime;
using Quark.Testing.Harness;

namespace Quark.Performance.CoreScalability;

/// <summary>
///     Measures scalability by core/parallelism count: at each step, exactly P independent grains
///     (one caller each, so no mailbox contention within a step -- see MailboxContention for that
///     concern) run concurrently for a fixed window, reporting aggregate throughput, throughput/core,
///     scaling efficiency relative to the P=1 baseline, and latency percentiles. Reveals where
///     throughput stops scaling linearly with added parallelism (scheduler/thread-pool/hardware
///     saturation) rather than just reporting one aggregate number at a fixed concurrency.
/// </summary>
public static class CoreScalabilityRunner
{
    public static async Task RunAsync(string[] args)
    {
        CoreScalabilityCliArgs cli = CoreScalabilityCliArgs.Parse(args);
        int[] steps = BuildSteps(cli);

        Console.WriteLine("=== Core Scalability Benchmark ===");
        Console.WriteLine($"  Steps: {string.Join(", ", steps)}");
        Console.WriteLine($"  Duration/step: {cli.DurationPerStepSeconds}s, Work: {cli.WorkMicroseconds}us/call, " +
                           $"Environment.ProcessorCount: {Environment.ProcessorCount}");
        Console.WriteLine("  Each step runs P independent grains, one dedicated caller each -- no");
        Console.WriteLine("  same-grain contention within a step. Efficiency is normalized to the P=1");
        Console.WriteLine("  baseline's calls/s/core; falling efficiency marks where scaling saturates.");
        Console.WriteLine();

        await using TestCluster cluster = await TestCluster.CreateAsync(options =>
        {
            options.InitialSilosCount = 1;
            options.ConfigureSiloServices = services =>
            {
                services.AddQuarkRuntime();
                services.AddGrainBehavior<IWorkGrain, WorkGrainBehavior>();
            };
            options.ConfigureClientServices = services =>
            {
                services.AddLocalClusterClient();
                services.AddGrainProxy<IWorkGrain, WorkGrainProxy>();
            };
        });

        int maxParallelism = steps[^1];
        var grains = new IWorkGrain[maxParallelism];
        for (int i = 0; i < maxParallelism; i++)
        {
            grains[i] = cluster.Client.GetGrain<IWorkGrain>($"scale-{i}");
        }

        var results = new List<StepResult>(steps.Length);
        double baselineThroughputPerCore = 0;

        foreach (int p in steps)
        {
            StepResult result = await RunStepAsync(grains, p, cli.DurationPerStepSeconds, cli.WorkMicroseconds);
            if (p == steps[0])
            {
                baselineThroughputPerCore = result.ThroughputPerCore;
            }

            double efficiency = baselineThroughputPerCore > 0
                ? result.ThroughputPerCore / baselineThroughputPerCore * 100.0
                : 100.0;
            result = result with { EfficiencyPercent = efficiency };
            results.Add(result);

            Console.WriteLine($"  P={p,-4} {result.ThroughputPerSecond,14:N0} calls/s   " +
                               $"{result.ThroughputPerCore,10:N0} calls/s/core   efficiency={efficiency,6:N1}%   " +
                               $"{result.Latency}");
        }

        Console.WriteLine();
        Console.WriteLine("=== Core Scalability Complete ===");
        Console.WriteLine($"{"P",-6}{"calls/s",16}{"calls/s/core",16}{"efficiency",14}{"p50(us)",12}{"p99(us)",12}{"p999(us)",12}");
        foreach (StepResult r in results)
        {
            Console.WriteLine($"{r.Parallelism,-6}{r.ThroughputPerSecond,16:N0}{r.ThroughputPerCore,16:N0}" +
                               $"{r.EfficiencyPercent,13:N1}%{r.Latency.P50,12:N1}{r.Latency.P99,12:N1}{r.Latency.P999,12:N1}");
        }
    }

    private static async Task<StepResult> RunStepAsync(IWorkGrain[] grains, int parallelism, double durationSeconds, int workMicroseconds)
    {
        var counters = new PaddedCounter[parallelism];
        using var histogram = new LatencyHistogram();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(durationSeconds));
        var sw = Stopwatch.StartNew();

        var workerTasks = new Task[parallelism];
        for (int i = 0; i < parallelism; i++)
        {
            IWorkGrain grain = grains[i];
            int workerIndex = i;
            workerTasks[i] = Task.Run(() => RunWorkerAsync(grain, workMicroseconds, cts.Token, counters, workerIndex, histogram));
        }

        await Task.WhenAll(workerTasks);
        sw.Stop();

        long totalCalls = SumCounters(counters);
        double elapsedSeconds = sw.Elapsed.TotalSeconds;
        double throughput = totalCalls / elapsedSeconds;

        return new StepResult(parallelism, throughput, throughput / parallelism, histogram.Merge(), 0);
    }

    private static async Task RunWorkerAsync(
        IWorkGrain grain, int workMicroseconds, CancellationToken ct,
        PaddedCounter[] counters, int workerIndex, LatencyHistogram histogram)
    {
        long local = 0;
        while (!ct.IsCancellationRequested)
        {
            long start = Stopwatch.GetTimestamp();
            await grain.DoWorkAsync(workMicroseconds);
            histogram.Record(Stopwatch.GetElapsedTime(start).TotalMicroseconds);

            local++;
            Volatile.Write(ref counters[workerIndex].Value, local);
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

    /// <summary>Builds the ascending, duplicate-free parallelism sequence to sweep.</summary>
    private static int[] BuildSteps(CoreScalabilityCliArgs cli)
    {
        var steps = new List<int>();
        int min = Math.Max(1, cli.MinParallelism);
        int max = Math.Max(min, cli.MaxParallelism);

        if (cli.StepMode == StepMode.Doubling)
        {
            int p = min;
            while (p < max)
            {
                steps.Add(p);
                p *= 2;
            }
        }
        else
        {
            int step = Math.Max(1, cli.Step);
            for (int p = min; p < max; p += step)
            {
                steps.Add(p);
            }
        }

        if (steps.Count == 0 || steps[^1] != max)
        {
            steps.Add(max);
        }

        return steps.ToArray();
    }

    private readonly record struct StepResult(int Parallelism, double ThroughputPerSecond, double ThroughputPerCore, Percentiles Latency, double EfficiencyPercent);
}

internal enum StepMode
{
    Doubling,
    Linear,
}

internal sealed class CoreScalabilityCliArgs
{
    public int MinParallelism { get; private init; } = 1;
    public int MaxParallelism { get; private init; } = Environment.ProcessorCount;
    public StepMode StepMode { get; private init; } = StepMode.Doubling;
    public int Step { get; private init; } = Math.Max(1, Environment.ProcessorCount / 8);
    public double DurationPerStepSeconds { get; private init; } = 3;
    public int WorkMicroseconds { get; private init; } = 10;

    public static CoreScalabilityCliArgs Parse(string[] args)
    {
        int minParallelism = 1;
        int maxParallelism = Environment.ProcessorCount;
        StepMode stepMode = StepMode.Doubling;
        int step = Math.Max(1, Environment.ProcessorCount / 8);
        double durationPerStep = 3;
        int workUs = 10;

        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--min-parallelism" when i + 1 < args.Length:
                    minParallelism = int.Parse(args[++i]);
                    break;
                case "--max-parallelism" when i + 1 < args.Length:
                    maxParallelism = int.Parse(args[++i]);
                    break;
                case "--step-mode" when i + 1 < args.Length:
                    stepMode = Enum.Parse<StepMode>(args[++i], ignoreCase: true);
                    break;
                case "--step" when i + 1 < args.Length:
                    step = int.Parse(args[++i]);
                    break;
                case "--duration-per-step" when i + 1 < args.Length:
                    durationPerStep = double.Parse(args[++i]);
                    break;
                case "--work-us" when i + 1 < args.Length:
                    workUs = int.Parse(args[++i]);
                    break;
            }
        }

        return new CoreScalabilityCliArgs
        {
            MinParallelism = minParallelism,
            MaxParallelism = maxParallelism,
            StepMode = stepMode,
            Step = step,
            DurationPerStepSeconds = durationPerStep,
            WorkMicroseconds = workUs,
        };
    }
}
