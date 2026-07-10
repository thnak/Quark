using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Quark.Client;
using Quark.Performance.Shared;
using Quark.Runtime;
using Quark.Testing.Harness;

namespace Quark.Performance.MailboxContention;

/// <summary>
///     Measures contention among multiple independent grain mailboxes: varies grain count
///     (parallelism across independent, serialized mailboxes) and callers-per-grain (contention on
///     one grain's single serialized mailbox) independently, reporting throughput and per-call
///     latency percentiles for each combination.
/// </summary>
public static class MailboxContentionRunner
{
    public static async Task RunAsync(string[] args)
    {
        MailboxContentionCliArgs cli = MailboxContentionCliArgs.Parse(args);

        Console.WriteLine("=== Mailbox Contention Benchmark ===");
        Console.WriteLine($"  Grains: {cli.Grains}, Callers/grain: {cli.CallersPerGrain}, " +
                           $"Duration: {cli.DurationSeconds}s, Work: {cli.WorkMicroseconds}us/call");
        Console.WriteLine("  Increase --callers-per-grain to see contention cost on one serialized");
        Console.WriteLine("  mailbox; increase --grains to see independent-mailbox parallelism scale.");
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

        var grains = new IWorkGrain[cli.Grains];
        for (int i = 0; i < cli.Grains; i++)
        {
            grains[i] = cluster.Client.GetGrain<IWorkGrain>($"work-{i}");
        }

        int workerCount = cli.Grains * cli.CallersPerGrain;
        var counters = new PaddedCounter[workerCount];
        using var histogram = new LatencyHistogram();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(cli.DurationSeconds));
        var totalSw = Stopwatch.StartNew();

        var workerTasks = new Task[workerCount];
        int worker = 0;
        for (int g = 0; g < cli.Grains; g++)
        {
            IWorkGrain grain = grains[g];
            for (int c = 0; c < cli.CallersPerGrain; c++)
            {
                int workerIndex = worker++;
                workerTasks[workerIndex] = Task.Run(() => RunWorkerAsync(grain, cli.WorkMicroseconds, cts.Token, counters, workerIndex, histogram));
            }
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

                Console.WriteLine($"  t={elapsed:F0}s  {count / elapsed:N0} calls/s (cumulative avg)  |  {instantaneous:N0} calls/s (last {intervalSeconds:F1}s)");

                lastCount = count;
                lastElapsed = elapsed;
            }
        });

        await Task.WhenAll(workerTasks);
        totalSw.Stop();
        await reportTask;

        long totalCalls = SumCounters(counters);
        double totalSeconds = totalSw.Elapsed.TotalSeconds;

        Console.WriteLine();
        Console.WriteLine("=== Mailbox Contention Complete ===");
        Console.WriteLine($"  Grains: {cli.Grains}, Callers/grain: {cli.CallersPerGrain}");
        Console.WriteLine($"  Duration: {totalSeconds:F1}s");
        Console.WriteLine($"  Total calls: {totalCalls:N0}");
        Console.WriteLine($"  Call rate: {totalCalls / totalSeconds:N0} calls/s");
        Console.WriteLine($"  Latency: {histogram.Merge()}");
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
}

internal sealed class MailboxContentionCliArgs
{
    public int Grains { get; private init; } = Environment.ProcessorCount;
    public int CallersPerGrain { get; private init; } = 4;
    public double DurationSeconds { get; private init; } = 10;
    public int WorkMicroseconds { get; private init; } = 10;

    public static MailboxContentionCliArgs Parse(string[] args)
    {
        int grains = Environment.ProcessorCount;
        int callersPerGrain = 4;
        double duration = 10;
        int workUs = 10;

        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--grains" when i + 1 < args.Length:
                    grains = int.Parse(args[++i]);
                    break;
                case "--callers-per-grain" when i + 1 < args.Length:
                    callersPerGrain = int.Parse(args[++i]);
                    break;
                case "--duration" when i + 1 < args.Length:
                    duration = double.Parse(args[++i]);
                    break;
                case "--work-us" when i + 1 < args.Length:
                    workUs = int.Parse(args[++i]);
                    break;
            }
        }

        return new MailboxContentionCliArgs
        {
            Grains = grains,
            CallersPerGrain = callersPerGrain,
            DurationSeconds = duration,
            WorkMicroseconds = workUs,
        };
    }
}
