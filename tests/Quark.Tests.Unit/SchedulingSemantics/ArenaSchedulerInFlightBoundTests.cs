using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Quark.Core.Abstractions.Identity;
using Quark.Runtime;
using Xunit;

namespace Quark.Tests.Unit.SchedulingSemantics;

/// <summary>
///     Validates the <see cref="ArenaScheduler"/>'s per-worker in-flight backpressure
///     (<see cref="SiloRuntimeOptions.SchedulerMaxInFlightDrainsPerWorker"/>): under a flood of turns
///     that all suspend on an await, a worker holds at most <c>maxInFlight</c> suspended drains before
///     it stops taking on more, so silo-wide concurrent suspensions never exceed
///     <c>workers × maxInFlight</c> — yet every turn still completes once unblocked (the cap applies
///     backpressure, it does not lose work).
/// </summary>
public sealed class ArenaSchedulerInFlightBoundTests
{
    [Fact]
    public async Task SuspendingDrains_AreBoundedPerWorker_AndAllStillComplete()
    {
        const int workerCount = 2;
        const int maxInFlight = 4;
        const int activationCount = 40; // far more than the workers × cap = 8 the scheduler will suspend at once
        int cap = workerCount * maxInFlight;

        var services = new ServiceCollection();
        services.AddLogging();

        var options = new SiloRuntimeOptions
        {
            ClusterId = "test",
            ServiceId = "arena-scheduler-inflight-bound",
            SiloName = "silo0",
            SchedulerMaxConcurrentActivations = workerCount,
            SchedulerMaxInFlightDrainsPerWorker = maxInFlight,
            SchedulerKind = SchedulerKind.ArenaV2,
        };

        await using var scheduler = new ArenaScheduler(options);
        scheduler.EnableStatsForTesting();
        await using ServiceProvider root = services.BuildServiceProvider();

        var activations = new GrainActivation[activationCount];
        for (int i = 0; i < activationCount; i++)
        {
            activations[i] = new GrainActivation(
                new GrainId(new GrainType("GatedGrain"), $"gated-{i}"),
                new GrainType("GatedGrain"),
                isReentrant: false,
                root,
                NullLogger<GrainActivation>.Instance,
                scheduler);
        }

        // Every turn awaits this gate, so each drain that starts suspends (spills) and counts against its
        // worker's in-flight budget until the gate is released.
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = 0;
        var completionTasks = new Task[activationCount];

        for (int i = 0; i < activationCount; i++)
        {
            completionTasks[i] = activations[i].PostAsync(async () =>
            {
                await gate.Task;
                Interlocked.Increment(ref completed);
            }).AsTask();
        }

        // Wait until backpressure is fully engaged: both workers filled to the cap (8 suspended).
        DateTime deadline = DateTime.UtcNow.AddSeconds(10);
        while (scheduler.PeakInFlightForTesting() < cap && DateTime.UtcNow < deadline)
            Thread.Sleep(1);

        Assert.Equal(cap, scheduler.PeakInFlightForTesting());   // reached the cap...
        Assert.Equal(0, Volatile.Read(ref completed));           // ...and everything is held behind the gate

        // Release the gate — the suspended drains complete, freeing slots, and the queued remainder
        // drains behind them. All work must complete; the cap only delayed it, never dropped it.
        gate.SetResult();
        await Task.WhenAll(completionTasks).WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Equal(activationCount, Volatile.Read(ref completed));
        Assert.True(scheduler.PeakInFlightForTesting() <= cap,
            $"peak in-flight {scheduler.PeakInFlightForTesting()} exceeded the bound {cap}");

        foreach (GrainActivation activation in activations)
            await activation.DisposeAsync();
    }
}
