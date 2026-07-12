using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Quark.Core.Abstractions.Identity;
using Quark.Runtime;
using Xunit;

namespace Quark.Tests.Unit.SchedulingSemantics;

/// <summary>
///     Reproduces the bounded-worker-pool reentrancy deadlock described in
///     <see cref="ActivationScheduler" />'s class remarks and GitHub issue #167:
///     <c>RunWorkerAsync</c> holds a worker's slot for the entire duration of a drain, including
///     any nested cross-activation call made from inside it. With N external callers each nesting
///     a <c>PostAsync</c> call into one of a handful of shared "hot" targets, and
///     <c>SchedulerMaxConcurrentActivations == callerCount</c>, every worker ends up blocked
///     awaiting a target's reply that no free worker exists to service -- a genuine circular
///     resource-exhaustion deadlock, not a rare timing race. Isolated, no TCP/DI involved,
///     matching the original repro that root-caused this in commit c1f3751.
///     A shared start gate (not a wall-clock delay) forces all callers to reach their nested call
///     simultaneously -- deterministically guaranteeing every worker is occupied at once, rather
///     than relying on scheduling luck, matching this suite's no-timing-based-synchronization
///     standard (see e.g. <see cref="ActivationSchedulerConcurrencyStressTests" />'s third test).
///     See docs/superpowers/specs/2026-07-12-scheduler-reentrancy-deadlock-fix.md.
/// </summary>
public sealed class ActivationSchedulerReentrancyDeadlockTests
{
    [Fact]
    public async Task ManyCallers_NestedCallIntoFewSharedTargets_DoesNotDeadlock()
    {
        const int workerCount = 4;
        const int callerCount = 4; // == workerCount: every worker gets consumed by a blocked caller
        const int hotTargetCount = 2; // few shared targets -- fan-in

        var services = new ServiceCollection();
        services.AddLogging();

        var options = new SiloRuntimeOptions
        {
            ClusterId = "test",
            ServiceId = "reentrancy-deadlock",
            SiloName = "silo0",
            SchedulerMaxConcurrentActivations = workerCount,
        };

        // Not `await using` -- if the scheduler is genuinely deadlocked, its own DisposeAsync
        // unconditionally awaits the stuck worker tasks (cancellation only stops idle workers from
        // parking again / gates new schedule attempts; it doesn't unstick a worker already blocked
        // deep inside a nested PostAsync().WaitAsync(), which has no cancellation wiring at all) --
        // so disposing here would itself hang forever against the unfixed scheduler. Dispose
        // explicitly, only on the success path below.
        var scheduler = new ActivationScheduler(options);
        ServiceProvider root = services.BuildServiceProvider();

        var callers = new GrainActivation[callerCount];
        for (int i = 0; i < callerCount; i++)
        {
            callers[i] = new GrainActivation(
                new GrainId(new GrainType("CallerGrain"), $"caller-{i}"),
                new GrainType("CallerGrain"),
                isReentrant: false,
                root,
                NullLogger<GrainActivation>.Instance,
                scheduler);
        }

        var hotTargets = new GrainActivation[hotTargetCount];
        for (int i = 0; i < hotTargetCount; i++)
        {
            hotTargets[i] = new GrainActivation(
                new GrainId(new GrainType("HotTargetGrain"), $"hot-{i}"),
                new GrainType("HotTargetGrain"),
                isReentrant: false,
                root,
                NullLogger<GrainActivation>.Instance,
                scheduler);
        }

        var completed = new bool[callerCount];

        // All callerCount worker items must reach this gate before any proceeds to its nested
        // call -- guarantees every worker is simultaneously mid-drain (about to nest-call) at the
        // moment any nested call is issued, deterministically forcing the fan-in-exhausts-N-workers
        // precondition instead of depending on scheduling timing.
        var startGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int arrived = 0;

        var callerTasks = new Task[callerCount];
        for (int c = 0; c < callerCount; c++)
        {
            int callerIndex = c;
            GrainActivation target = hotTargets[c % hotTargetCount];
            callerTasks[c] = Task.Run(() => callers[callerIndex].PostAsync(async () =>
            {
                if (Interlocked.Increment(ref arrived) == callerCount)
                    startGate.SetResult();
                await startGate.Task.ConfigureAwait(false);

                // Nested cross-activation call from inside a drain -- the exact shape that
                // triggers the deadlock: this worker's slot stays occupied until target's own
                // turn completes.
                await target.PostAsync(() => ValueTask.CompletedTask).ConfigureAwait(false);
                completed[callerIndex] = true;
            }).AsTask());
        }

        // Generous but bounded timeout: comfortably longer than SchedulerStallThreshold's default
        // (3s) so the overflow-capacity rescue has time to kick in, but far shorter than "hangs
        // forever" -- this must fail (timeout) against the unfixed scheduler.
        await Task.WhenAll(callerTasks).WaitAsync(TimeSpan.FromSeconds(15));

        Assert.All(completed, Assert.True);

        foreach (GrainActivation caller in callers) await caller.DisposeAsync();
        foreach (GrainActivation target in hotTargets) await target.DisposeAsync();
        await scheduler.DisposeAsync();
        await root.DisposeAsync();
    }
}
