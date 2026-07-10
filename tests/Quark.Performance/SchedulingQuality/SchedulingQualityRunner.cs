using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Quark.Client;
using Quark.Diagnostics.Abstractions;
using Quark.Performance.Shared;
using Quark.Runtime;
using Quark.Testing.Harness;

namespace Quark.Performance.SchedulingQuality;

/// <summary>
///     Measures scheduling quality: how long work waits in the scheduler's ready queue before a
///     worker services it, and how long/how much each drain pass processes, as the number of
///     activations and scheduler workers (<see cref="SiloRuntimeOptions.SchedulerMaxConcurrentActivations" />)
///     vary. A single dispatcher round-robins calls across all activations without awaiting each one
///     to completion before firing the next, so many calls stay concurrently in flight -- exactly
///     what exercises worker concurrency; a purely sequential await-then-dispatch loop never would.
/// </summary>
public static class SchedulingQualityRunner
{
    public static async Task RunAsync(string[] args)
    {
        SchedulingQualityCliArgs cli = SchedulingQualityCliArgs.Parse(args);

        Console.WriteLine("=== Scheduling Quality Benchmark ===");
        Console.WriteLine($"  Activations: {cli.Activations}, Scheduler workers: {cli.SchedulerWorkers}");
        Console.WriteLine($"  Dispatch interval: {cli.DispatchIntervalMs}ms, Work: {cli.WorkMicroseconds}us/call, Duration: {cli.DurationSeconds}s");
        Console.WriteLine();

        using var listener = new SchedulingQualityDiagnosticListener();

        await using TestCluster cluster = await TestCluster.CreateAsync(options =>
        {
            options.InitialSilosCount = 1;
            options.ConfigureSiloServices = services =>
            {
                services.AddQuarkRuntime();
                services.Configure<SiloRuntimeOptions>(o => o.SchedulerMaxConcurrentActivations = cli.SchedulerWorkers);
                services.AddSingleton<IQuarkDiagnosticListener>(listener);
                services.AddGrainBehavior<IWorkGrain, WorkGrainBehavior>();
            };
            options.ConfigureClientServices = services =>
            {
                services.AddLocalClusterClient();
                services.AddGrainProxy<IWorkGrain, WorkGrainProxy>();
            };
        });

        var grains = new IWorkGrain[cli.Activations];
        for (int i = 0; i < cli.Activations; i++)
        {
            grains[i] = cluster.Client.GetGrain<IWorkGrain>($"activation-{i}");
        }

        var inFlight = new ConcurrentBag<Task>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(cli.DurationSeconds));

        int dispatchIndex = 0;
        while (!cts.IsCancellationRequested)
        {
            IWorkGrain target = grains[dispatchIndex % grains.Length];
            dispatchIndex++;
            inFlight.Add(Task.Run(() => target.DoWorkAsync(cli.WorkMicroseconds).AsTask()));

            try
            {
                await Task.Delay(cli.DispatchIntervalMs, cts.Token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        // Let already-dispatched work drain before reporting.
        await Task.WhenAll(inFlight.ToArray());

        Console.WriteLine("=== Scheduling Quality Complete ===");
        Console.WriteLine($"  Dispatched calls: {dispatchIndex:N0}");
        Console.WriteLine($"  Ready-queue wait time:  {listener.WaitHistogram.Merge()}");
        Console.WriteLine($"  Drain duration:         {listener.DrainDurationHistogram.Merge()}");
        Console.WriteLine($"  Average items/drain:    {listener.AverageItemsPerDrain:N2}");
    }
}

internal sealed class SchedulingQualityCliArgs
{
    public int Activations { get; private init; } = 64;
    public int SchedulerWorkers { get; private init; } = Environment.ProcessorCount;
    public int DispatchIntervalMs { get; private init; } = 20;
    public int WorkMicroseconds { get; private init; } = 20;
    public double DurationSeconds { get; private init; } = 10;

    public static SchedulingQualityCliArgs Parse(string[] args)
    {
        int activations = 64;
        int schedulerWorkers = Environment.ProcessorCount;
        int dispatchIntervalMs = 20;
        int workUs = 20;
        double duration = 10;

        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--activations" when i + 1 < args.Length:
                    activations = int.Parse(args[++i]);
                    break;
                case "--scheduler-workers" when i + 1 < args.Length:
                    schedulerWorkers = int.Parse(args[++i]);
                    break;
                case "--dispatch-interval-ms" when i + 1 < args.Length:
                    dispatchIntervalMs = int.Parse(args[++i]);
                    break;
                case "--work-us" when i + 1 < args.Length:
                    workUs = int.Parse(args[++i]);
                    break;
                case "--duration" when i + 1 < args.Length:
                    duration = double.Parse(args[++i]);
                    break;
            }
        }

        return new SchedulingQualityCliArgs
        {
            Activations = activations,
            SchedulerWorkers = schedulerWorkers,
            DispatchIntervalMs = dispatchIntervalMs,
            WorkMicroseconds = workUs,
            DurationSeconds = duration,
        };
    }
}
