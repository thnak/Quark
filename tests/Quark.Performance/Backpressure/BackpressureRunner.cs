using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Quark.Client;
using Quark.Performance.Shared;
using Quark.Runtime;
using Quark.Testing.Harness;

namespace Quark.Performance.Backpressure;

/// <summary>
///     Exercises backpressure at two independent layers: the per-grain mailbox
///     (<see cref="SiloRuntimeOptions.MailboxCapacity"/>/<see cref="MailboxFullMode"/>) and the
///     scheduler's ready queue (<see cref="SiloRuntimeOptions.SchedulerReadyQueueCapacity"/>/
///     <see cref="SchedulerOverloadMode"/>). <c>--scope scheduler</c> targets many distinct,
///     mostly-idle grains rather than one hot grain: <see cref="ActivationScheduler.ScheduleAsync"/>
///     only runs the ready-queue capacity gate on an idle-&gt;scheduled transition (see
///     <c>TryMarkScheduled</c>), so a single grain already being drained bypasses the gate on every
///     subsequent post -- one hot grain alone can never trigger a scheduler-level rejection.
/// </summary>
public static class BackpressureRunner
{
    public static async Task RunAsync(string[] args)
    {
        BackpressureCliArgs cli = BackpressureCliArgs.Parse(args);

        Console.WriteLine("=== Backpressure Benchmark ===");
        Console.WriteLine($"  Scope: {cli.Scope}, Duration: {cli.DurationSeconds}s, Work: {cli.WorkMicroseconds}us/call");
        if (cli.Scope == BackpressureScope.Mailbox)
        {
            Console.WriteLine($"  MailboxCapacity: {cli.MailboxCapacity}, MailboxFullMode: {cli.MailboxFullMode}, Callers: {cli.Callers}");
        }
        else
        {
            Console.WriteLine($"  SchedulerReadyQueueCapacity: {cli.SchedulerReadyQueueCapacity}, SchedulerOverloadMode: {cli.SchedulerOverloadMode}, Grains: {cli.Grains}");
        }
        Console.WriteLine();

        await using TestCluster cluster = await TestCluster.CreateAsync(options =>
        {
            options.InitialSilosCount = 1;
            options.ConfigureSiloServices = services =>
            {
                services.AddQuarkRuntime();
                services.Configure<SiloRuntimeOptions>(o =>
                {
                    if (cli.Scope == BackpressureScope.Mailbox)
                    {
                        o.MailboxCapacity = cli.MailboxCapacity;
                        o.MailboxFullMode = cli.MailboxFullMode;
                    }
                    else
                    {
                        o.SchedulerReadyQueueCapacity = cli.SchedulerReadyQueueCapacity;
                        o.SchedulerOverloadMode = cli.SchedulerOverloadMode;
                    }
                });
                services.AddGrainBehavior<IWorkGrain, WorkGrainBehavior>();
            };
            options.ConfigureClientServices = services =>
            {
                services.AddLocalClusterClient();
                services.AddGrainProxy<IWorkGrain, WorkGrainProxy>();
            };
        });

        using var histogram = new LatencyHistogram();
        bool rejecting = cli.Scope == BackpressureScope.Mailbox
            ? cli.MailboxFullMode == MailboxFullMode.RejectWhenFull
            : cli.SchedulerOverloadMode == SchedulerOverloadMode.RejectWhenFull;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(cli.DurationSeconds));
        var totalSw = Stopwatch.StartNew();

        Task[] workerTasks;
        PaddedCounter[] acceptedCounters;
        PaddedCounter[] rejectedCounters;

        if (cli.Scope == BackpressureScope.Mailbox)
        {
            // One target grain hammered by many callers -- MailboxCapacity is inherently
            // a per-activation-mailbox concept.
            var target = cluster.Client.GetGrain<IWorkGrain>("target");
            acceptedCounters = new PaddedCounter[cli.Callers];
            rejectedCounters = new PaddedCounter[cli.Callers];
            workerTasks = new Task[cli.Callers];
            for (int i = 0; i < cli.Callers; i++)
            {
                int workerIndex = i;
                workerTasks[i] = Task.Run(() => RunWorkerAsync(target, cli.WorkMicroseconds, cts.Token, histogram, rejecting, acceptedCounters, rejectedCounters, workerIndex));
            }
        }
        else
        {
            // Many distinct, mostly-idle grains, one caller loop each -- see class doc for why a
            // single hot grain cannot exercise the scheduler ready-queue capacity gate.
            acceptedCounters = new PaddedCounter[cli.Grains];
            rejectedCounters = new PaddedCounter[cli.Grains];
            workerTasks = new Task[cli.Grains];
            for (int i = 0; i < cli.Grains; i++)
            {
                var target = cluster.Client.GetGrain<IWorkGrain>($"scheduler-target-{i}");
                int workerIndex = i;
                workerTasks[i] = Task.Run(() => RunWorkerAsync(target, cli.WorkMicroseconds, cts.Token, histogram, rejecting, acceptedCounters, rejectedCounters, workerIndex));
            }
        }

        await Task.WhenAll(workerTasks);
        totalSw.Stop();

        long accepted = SumCounters(acceptedCounters);
        long rejected = SumCounters(rejectedCounters);
        double totalSeconds = totalSw.Elapsed.TotalSeconds;

        Console.WriteLine("=== Backpressure Complete ===");
        Console.WriteLine($"  Duration: {totalSeconds:F1}s");
        Console.WriteLine($"  Accepted: {accepted:N0} ({accepted / totalSeconds:N0}/s)");
        if (rejecting)
        {
            Console.WriteLine($"  Rejected: {rejected:N0} ({rejected / totalSeconds:N0}/s)");
        }
        else
        {
            Console.WriteLine($"  Caller-side wait latency (the backpressure signal): {histogram.Merge()}");
        }
    }

    private static async Task RunWorkerAsync(
        IWorkGrain grain, int workMicroseconds, CancellationToken ct, LatencyHistogram histogram, bool rejecting,
        PaddedCounter[] acceptedCounters, PaddedCounter[] rejectedCounters, int workerIndex)
    {
        long accepted = 0;
        long rejected = 0;
        while (!ct.IsCancellationRequested)
        {
            long start = Stopwatch.GetTimestamp();
            try
            {
                await grain.DoWorkAsync(workMicroseconds);
                if (!rejecting)
                {
                    histogram.Record(Stopwatch.GetElapsedTime(start).TotalMicroseconds);
                }
                accepted++;
                Volatile.Write(ref acceptedCounters[workerIndex].Value, accepted);
            }
            catch (MailboxFullException)
            {
                rejected++;
                Volatile.Write(ref rejectedCounters[workerIndex].Value, rejected);
            }
            catch (SchedulerOverloadException)
            {
                rejected++;
                Volatile.Write(ref rejectedCounters[workerIndex].Value, rejected);
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

internal enum BackpressureScope
{
    Mailbox,
    Scheduler,
}

internal sealed class BackpressureCliArgs
{
    public BackpressureScope Scope { get; private init; } = BackpressureScope.Mailbox;
    public int MailboxCapacity { get; private init; } = 100;
    public MailboxFullMode MailboxFullMode { get; private init; } = MailboxFullMode.Wait;
    public int SchedulerReadyQueueCapacity { get; private init; } = 100;
    public SchedulerOverloadMode SchedulerOverloadMode { get; private init; } = SchedulerOverloadMode.Wait;
    public int Callers { get; private init; } = Environment.ProcessorCount * 4;
    public int Grains { get; private init; } = 64;
    public int WorkMicroseconds { get; private init; } = 500;
    public double DurationSeconds { get; private init; } = 10;

    public static BackpressureCliArgs Parse(string[] args)
    {
        BackpressureScope scope = BackpressureScope.Mailbox;
        int mailboxCapacity = 100;
        MailboxFullMode mailboxFullMode = MailboxFullMode.Wait;
        int schedulerReadyQueueCapacity = 100;
        SchedulerOverloadMode schedulerOverloadMode = SchedulerOverloadMode.Wait;
        int callers = Environment.ProcessorCount * 4;
        int grains = 64;
        int workUs = 500;
        double duration = 10;

        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--scope" when i + 1 < args.Length:
                    scope = Enum.Parse<BackpressureScope>(args[++i], ignoreCase: true);
                    break;
                case "--mailbox-capacity" when i + 1 < args.Length:
                    mailboxCapacity = int.Parse(args[++i]);
                    break;
                case "--mailbox-full-mode" when i + 1 < args.Length:
                    mailboxFullMode = Enum.Parse<MailboxFullMode>(args[++i], ignoreCase: true);
                    break;
                case "--scheduler-ready-queue-capacity" when i + 1 < args.Length:
                    schedulerReadyQueueCapacity = int.Parse(args[++i]);
                    break;
                case "--scheduler-overload-mode" when i + 1 < args.Length:
                    schedulerOverloadMode = Enum.Parse<SchedulerOverloadMode>(args[++i], ignoreCase: true);
                    break;
                case "--callers" when i + 1 < args.Length:
                    callers = int.Parse(args[++i]);
                    break;
                case "--grains" when i + 1 < args.Length:
                    grains = int.Parse(args[++i]);
                    break;
                case "--work-us" when i + 1 < args.Length:
                    workUs = int.Parse(args[++i]);
                    break;
                case "--duration" when i + 1 < args.Length:
                    duration = double.Parse(args[++i]);
                    break;
            }
        }

        return new BackpressureCliArgs
        {
            Scope = scope,
            MailboxCapacity = mailboxCapacity,
            MailboxFullMode = mailboxFullMode,
            SchedulerReadyQueueCapacity = schedulerReadyQueueCapacity,
            SchedulerOverloadMode = schedulerOverloadMode,
            Callers = callers,
            Grains = grains,
            WorkMicroseconds = workUs,
            DurationSeconds = duration,
        };
    }
}
