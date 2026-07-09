using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;
using Quark.Runtime;
using Xunit;

namespace Quark.Tests.Unit.SchedulingSemantics;

/// <summary>
///     Stress-tests the ConcurrentQueue-per-worker ready queue's correctness under many concurrent
///     producers and a small consumer worker count -- specifically, that the cross-shard steal sweep
///     never loses a scheduled activation's work under contention. This is the risk introduced by
///     swapping Channel&lt;T&gt; for ConcurrentQueue&lt;T&gt;
///     (docs/superpowers/specs/2026-07-09-work-stealing-scheduler-design.md) that the rest of the
///     SchedulingSemantics suite doesn't directly exercise: those tests check ordering and fairness
///     with a handful of named grains, not raw concurrent-producer throughput.
///     A second, more targeted test below covers the single-shard/no-rescue-sweep case: with sustained
///     multi-shard traffic, a missed wake-up is almost always rescued by an unrelated wake elsewhere,
///     which the general stress test above does not reliably rule out.
/// </summary>
public sealed class ActivationSchedulerConcurrencyStressTests
{
    [Fact]
    public async Task ManyConcurrentProducers_AcrossManyActivations_NoWorkIsLost()
    {
        const int workerCount = 2; // deliberately small -- forces real cross-shard stealing
        const int activationCount = 64;
        const int postsPerActivation = 25;

        var services = new ServiceCollection();
        services.AddLogging();

        var options = new SiloRuntimeOptions
        {
            ClusterId = "test",
            ServiceId = "scheduler-stress",
            SiloName = "silo0",
            SchedulerMaxConcurrentActivations = workerCount,
        };

        await using var scheduler = new ActivationScheduler(options);
        services.AddSingleton<IActivationScheduler>(scheduler);
        await using ServiceProvider root = services.BuildServiceProvider();

        var activations = new GrainActivation[activationCount];
        for (int i = 0; i < activationCount; i++)
        {
            activations[i] = new GrainActivation(
                new GrainId(new GrainType("StressGrain"), $"stress-{i}"),
                new GrainType("StressGrain"),
                isReentrant: false,
                root,
                NullLogger<GrainActivation>.Instance);
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
    public async Task TwoConcurrentProducers_SingleShardNoFollowUpTraffic_NoWorkIsLost()
    {
        // Targets the specific interleaving the Task 1 code review identified: two producers
        // enqueueing to an all-idle scheduler with no later, unrelated traffic to trigger a
        // rescuing full sweep. The general stress test above (64 activations, 2 workers, 1600
        // sustained posts) does NOT reliably reproduce this -- verified empirically by reverting
        // the wake-gate fix and running it 50 times against the buggy code with zero failures,
        // because sustained multi-shard traffic almost always produces an unrelated wake that
        // rescues a missed transition on another shard. Forcing workerCount=1 (exactly one shard,
        // so any two producers unconditionally collide) and issuing only one burst of two posts
        // (no follow-up traffic at all) removes that escape hatch.
        const int iterations = 300;

        for (int iteration = 0; iteration < iterations; iteration++)
        {
            var services = new ServiceCollection();
            services.AddLogging();

            var options = new SiloRuntimeOptions
            {
                ClusterId = "test",
                ServiceId = "scheduler-stress-single-shard",
                SiloName = "silo0",
                SchedulerMaxConcurrentActivations = 1,
            };

            await using var scheduler = new ActivationScheduler(options);
            services.AddSingleton<IActivationScheduler>(scheduler);
            await using ServiceProvider root = services.BuildServiceProvider();

            var a1 = new GrainActivation(
                new GrainId(new GrainType("StressGrain"), $"single-shard-a-{iteration}"),
                new GrainType("StressGrain"),
                isReentrant: false,
                root,
                NullLogger<GrainActivation>.Instance);
            var a2 = new GrainActivation(
                new GrainId(new GrainType("StressGrain"), $"single-shard-b-{iteration}"),
                new GrainType("StressGrain"),
                isReentrant: false,
                root,
                NullLogger<GrainActivation>.Instance);

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
                "no follow-up traffic exists to trigger a rescuing full sweep.");

            await a1.DisposeAsync();
            await a2.DisposeAsync();
        }
    }
}
