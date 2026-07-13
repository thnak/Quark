using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Quark.Core.Abstractions.Identity;
using Quark.Runtime;
using Xunit;

namespace Quark.Tests.Unit.SchedulingSemantics;

/// <summary>
///     Validates that the next-generation <see cref="ArenaScheduler"/> actually exercises its two
///     defining mechanisms — from-worker local-deque routing and cross-worker work stealing — under the
///     one workload shape that is safe to do so in the Phase 1/2 blocking-drain model: fire-and-forget
///     fan-out. A source activation, while running on a worker thread, fires many fire-and-forget posts
///     to sink activations. Because the source does not block awaiting them, this never risks the
///     nested-await worker-starvation deadlock that a blocking non-reentrant call chain would.
///     <para>
///         The fan-out is raised from a <em>single</em> source drain, so every sink is pushed onto that
///         one worker's local deque. The remaining workers can only make progress by stealing — so a
///         correct run both completes all sinks exactly once and records a non-zero steal count spread
///         across more than one worker thread. This is the coverage AstroSim cannot provide: its grains
///         are <c>[Reentrant]</c>, so all its calls run inline and never enter the scheduler at all.
///     </para>
/// </summary>
public sealed class ArenaSchedulerStealingTests
{
    [Fact]
    public async Task FromWorkerFanOut_RoutesToLocalDeque_AndIsStolenAcrossWorkers()
    {
        const int workerCount = 4;
        const int sinkCount = 2000;

        var services = new ServiceCollection();
        services.AddLogging();

        var options = new SiloRuntimeOptions
        {
            ClusterId = "test",
            ServiceId = "arena-scheduler-stealing",
            SiloName = "silo0",
            SchedulerMaxConcurrentActivations = workerCount,
            SchedulerKind = SchedulerKind.ArenaV2,
        };

        await using var scheduler = new ArenaScheduler(options);
        scheduler.EnableStatsForTesting();
        await using ServiceProvider root = services.BuildServiceProvider();

        GrainActivation Make(string key) => new(
            new GrainId(new GrainType("StealGrain"), key),
            new GrainType("StealGrain"),
            isReentrant: false,
            root,
            NullLogger<GrainActivation>.Instance,
            scheduler);

        var source = Make("source");
        var sinks = new GrainActivation[sinkCount];
        for (int i = 0; i < sinkCount; i++)
            sinks[i] = Make($"sink-{i}");

        var completed = 0;
        var doubleRun = new ConcurrentBag<int>();
        var executed = new int[sinkCount];
        var sinkThreads = new ConcurrentDictionary<int, byte>();

        // The source's single turn runs on a worker thread and fires one fire-and-forget post per sink.
        // Each of those PostAsync calls schedules its sink from *inside* a worker drain, so it routes to
        // that worker's local deque — the from-worker path.
        await source.PostAsync(() =>
        {
            for (int i = 0; i < sinkCount; i++)
            {
                int index = i;
                _ = sinks[index].PostAsync(() =>
                {
                    if (Interlocked.Exchange(ref executed[index], 1) != 0)
                        doubleRun.Add(index);

                    sinkThreads.TryAdd(Environment.CurrentManagedThreadId, 0);

                    // A little work so a single worker cannot drain all 2000 sinks before its idle
                    // siblings steal any — makes the cross-worker spread deterministic, not a race.
                    Thread.SpinWait(200);

                    Interlocked.Increment(ref completed);
                    return ValueTask.CompletedTask;
                });
            }

            return ValueTask.CompletedTask;
        }).AsTask().WaitAsync(TimeSpan.FromSeconds(30));

        // Wait for the fanned-out sinks to drain.
        var spin = new SpinWait();
        DateTime deadline = DateTime.UtcNow.AddSeconds(30);
        while (Volatile.Read(ref completed) < sinkCount && DateTime.UtcNow < deadline)
            spin.SpinOnce();

        (long external, long local, _, long steals) = scheduler.StatsSnapshot();

        Assert.Equal(sinkCount, Volatile.Read(ref completed));       // no work lost
        Assert.Empty(doubleRun);                                     // I1: each sink ran exactly once
        Assert.Equal(sinkCount, local);                              // every sink post took the from-worker route
        Assert.Equal(1, external);                                   // only the source came from outside a worker
        Assert.True(steals > 0, $"expected cross-worker steals, got {steals}");
        Assert.True(sinkThreads.Count > 1,
            $"expected sinks to run across multiple worker threads, saw {sinkThreads.Count}");

        await source.DisposeAsync();
        foreach (GrainActivation sink in sinks)
            await sink.DisposeAsync();
    }
}
