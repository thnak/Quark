using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Quark.Core.Abstractions.Identity;
using Quark.Runtime;
using Xunit;

namespace Quark.Tests.Unit.SchedulingSemantics;

/// <summary>
///     Invariant stress tests for the next-generation <see cref="ArenaScheduler"/> (Phase 1) — the
///     direct counterpart of <see cref="ActivationSchedulerConcurrencyStressTests"/>, run against the
///     dedicated-worker-thread / work-stealing-deque scheduler instead of the sharded-ready-queue one.
///     These pin the core P1 invariants from
///     docs/superpowers/specs/2026-07-12-next-gen-scheduler-design.md:
///     <list type="bullet">
///         <item>I1 (single turn): a non-reentrant activation never has two turns executing at once,
///         even while its mailbox is fed by many concurrent producers and drained by a stealing pool.</item>
///         <item>I4 (no lost wake): every posted work item is executed exactly once — nothing is
///         stranded by a missed empty→non-empty wake, including the single-worker park/unpark path.</item>
///     </list>
/// </summary>
public sealed class ArenaSchedulerConcurrencyStressTests
{
    [Fact]
    public async Task ManyConcurrentProducers_AcrossManyActivations_NoWorkIsLost()
    {
        const int workerCount = 2; // deliberately small -- forces real cross-worker stealing
        const int activationCount = 64;
        const int postsPerActivation = 25;

        var services = new ServiceCollection();
        services.AddLogging();

        var options = new SiloRuntimeOptions
        {
            ClusterId = "test",
            ServiceId = "arena-scheduler-stress",
            SiloName = "silo0",
            SchedulerMaxConcurrentActivations = workerCount,
            SchedulerKind = SchedulerKind.ArenaV2,
        };

        await using var scheduler = new ArenaScheduler(options);
        await using ServiceProvider root = services.BuildServiceProvider();

        var activations = new GrainActivation[activationCount];
        for (int i = 0; i < activationCount; i++)
        {
            activations[i] = new GrainActivation(
                new GrainId(new GrainType("StressGrain"), $"stress-{i}"),
                new GrainType("StressGrain"),
                isReentrant: false,
                root,
                NullLogger<GrainActivation>.Instance,
                scheduler);
        }

        var completedCounts = new int[activationCount];
        var currentlyExecuting = new int[activationCount];
        var overlapDetected = new ConcurrentBag<int>();

        var postTasks = new List<Task>(activationCount * postsPerActivation);
        for (int a = 0; a < activationCount; a++)
        {
            int activationIndex = a;
            for (int p = 0; p < postsPerActivation; p++)
            {
                postTasks.Add(Task.Run(() => activations[activationIndex].PostAsync(() =>
                {
                    // I1: two turns of the same activation must never overlap.
                    if (Interlocked.CompareExchange(ref currentlyExecuting[activationIndex], 1, 0) != 0)
                        overlapDetected.Add(activationIndex);

                    Interlocked.Exchange(ref currentlyExecuting[activationIndex], 0);
                    Interlocked.Increment(ref completedCounts[activationIndex]);

                    return ValueTask.CompletedTask;
                }).AsTask()));
            }
        }

        await Task.WhenAll(postTasks).WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Empty(overlapDetected);
        for (int i = 0; i < activationCount; i++)
        {
            Assert.True(completedCounts[i] == postsPerActivation,
                $"Activation {i} completed {completedCounts[i]}/{postsPerActivation} posts -- work was lost.");
        }

        foreach (GrainActivation activation in activations)
            await activation.DisposeAsync();
    }

    [Fact]
    public async Task TwoConcurrentProducers_SingleWorkerNoFollowUpTraffic_NoWorkIsLost()
    {
        // The lost-wakeup-prone shape: two producers enqueue into an all-idle scheduler with no later
        // traffic to rescue a missed wake. workerCount=1 removes every escape hatch (one worker, one
        // idle-stack slot, no unrelated wake), so the push-then-recheck double-check is the only thing
        // standing between correctness and a stranded item.
        const int iterations = 300;

        for (int iteration = 0; iteration < iterations; iteration++)
        {
            var services = new ServiceCollection();
            services.AddLogging();

            var options = new SiloRuntimeOptions
            {
                ClusterId = "test",
                ServiceId = "arena-scheduler-single-worker",
                SiloName = "silo0",
                SchedulerMaxConcurrentActivations = 1,
                SchedulerKind = SchedulerKind.ArenaV2,
            };

            await using var scheduler = new ArenaScheduler(options);
            await using ServiceProvider root = services.BuildServiceProvider();

            var a1 = new GrainActivation(
                new GrainId(new GrainType("StressGrain"), $"single-worker-a-{iteration}"),
                new GrainType("StressGrain"),
                isReentrant: false,
                root,
                NullLogger<GrainActivation>.Instance,
                scheduler);
            var a2 = new GrainActivation(
                new GrainId(new GrainType("StressGrain"), $"single-worker-b-{iteration}"),
                new GrainType("StressGrain"),
                isReentrant: false,
                root,
                NullLogger<GrainActivation>.Instance,
                scheduler);

            int completed1 = 0;
            int completed2 = 0;

            Task t1 = Task.Run(() => a1.PostAsync(() =>
            {
                Interlocked.Increment(ref completed1);
                return ValueTask.CompletedTask;
            }).AsTask());
            Task t2 = Task.Run(() => a2.PostAsync(() =>
            {
                Interlocked.Increment(ref completed2);
                return ValueTask.CompletedTask;
            }).AsTask());

            await Task.WhenAll(t1, t2).WaitAsync(TimeSpan.FromSeconds(2));

            Assert.True(completed1 == 1 && completed2 == 1,
                $"Iteration {iteration}: work was stranded (completed1={completed1}, completed2={completed2}) -- " +
                "no follow-up traffic exists to rescue a missed wake.");

            await a1.DisposeAsync();
            await a2.DisposeAsync();
        }
    }

    [Fact]
    public async Task SingleWorker_RepeatedIdleParkCycles_NeverStrandsWork()
    {
        // Exercises the idle-stack push -> double-check -> park -> wake path in isolation. workerCount=1
        // means every idle transition drives the full sequence, and PostAsync awaits each item's own
        // completion, so the single worker has completed at least one park-or-find-more cycle between
        // each post with no wall-clock delay needed to force the ordering.
        const int iterations = 300;

        for (int iteration = 0; iteration < iterations; iteration++)
        {
            var services = new ServiceCollection();
            services.AddLogging();

            var options = new SiloRuntimeOptions
            {
                ClusterId = "test",
                ServiceId = "arena-scheduler-wake-signal",
                SiloName = "silo0",
                SchedulerMaxConcurrentActivations = 1,
                SchedulerKind = SchedulerKind.ArenaV2,
            };

            await using var scheduler = new ArenaScheduler(options);
            await using ServiceProvider root = services.BuildServiceProvider();

            var activation = new GrainActivation(
                new GrainId(new GrainType("StressGrain"), $"idle-park-{iteration}"),
                new GrainType("StressGrain"),
                isReentrant: false,
                root,
                NullLogger<GrainActivation>.Instance,
                scheduler);

            int completed = 0;

            for (int post = 0; post < 3; post++)
            {
                await activation.PostAsync(() =>
                {
                    Interlocked.Increment(ref completed);
                    return ValueTask.CompletedTask;
                }).AsTask().WaitAsync(TimeSpan.FromSeconds(2));
            }

            Assert.True(completed == 3,
                $"Iteration {iteration}: expected 3 completions, got {completed} -- work was stranded " +
                "in a single-worker idle-park cycle.");

            await activation.DisposeAsync();
        }
    }
}
