using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Quark.Core.Abstractions.Identity;
using Quark.Runtime;
using Xunit;

namespace Quark.Tests.Unit.SchedulingSemantics;

/// <summary>
///     Validates the <see cref="ArenaScheduler"/>'s async-resume (spill-to-ThreadPool-on-await) behavior:
///     a worker never blocks <em>inside</em> a turn that awaits, so a non-reentrant call chain deeper
///     than the worker count still makes progress instead of starving the pool. This is exactly the
///     bounded-worker reentrancy-deadlock shape (issue #167): under a blocking drain, worker 0 draining
///     A0 would block awaiting A1, worker 1 would block awaiting A2, and with only N workers a chain of
///     depth &gt; N deadlocks — every worker parked inside a turn, no worker left to run the next link.
///     With async resume the awaiting drain suspends and frees its worker, which then runs the next
///     link, so the chain completes regardless of depth (it completes even with a single worker).
/// </summary>
public sealed class ArenaSchedulerAsyncResumeTests
{
    [Theory]
    [InlineData(1)]  // single worker: blocking would deadlock at the very first nested await
    [InlineData(2)]  // depth (8) far exceeds worker count: blocking would deadlock at depth 2
    public async Task NonReentrantChainDeeperThanWorkers_CompletesWithoutDeadlock(int workerCount)
    {
        const int depth = 8;

        var services = new ServiceCollection();
        services.AddLogging();

        var options = new SiloRuntimeOptions
        {
            ClusterId = "test",
            ServiceId = "arena-scheduler-async-resume",
            SiloName = "silo0",
            SchedulerMaxConcurrentActivations = workerCount,
            SchedulerKind = SchedulerKind.ArenaV2,
        };

        await using var scheduler = new ArenaScheduler(options);
        await using ServiceProvider root = services.BuildServiceProvider();

        var chain = new GrainActivation[depth];
        for (int i = 0; i < depth; i++)
        {
            chain[i] = new GrainActivation(
                new GrainId(new GrainType("ChainGrain"), $"link-{i}"),
                new GrainType("ChainGrain"),
                isReentrant: false, // non-reentrant: nested calls MUST go through the scheduler, not inline
                root,
                NullLogger<GrainActivation>.Instance,
                scheduler);
        }

        var executed = new bool[depth];
        var order = new ConcurrentQueue<int>();

        // Each link records itself, then awaits a call into the next link — a genuine cross-activation
        // await from inside a worker drain. The leaf just returns.
        Func<ValueTask> MakeWork(int level) => async () =>
        {
            executed[level] = true;
            order.Enqueue(level);
            if (level + 1 < depth)
                await chain[level + 1].PostAsync(MakeWork(level + 1));
        };

        // If async resume is broken (worker blocks inside the awaiting turn), this chain deadlocks and
        // the wait times out. A comfortable timeout keeps the failure a clean assertion, not a hang.
        await chain[0].PostAsync(MakeWork(0)).AsTask().WaitAsync(TimeSpan.FromSeconds(15));

        for (int i = 0; i < depth; i++)
            Assert.True(executed[i], $"link {i} never executed (chain stalled at depth {i})");

        Assert.Equal(Enumerable.Range(0, depth), order.ToArray());

        foreach (GrainActivation link in chain)
            await link.DisposeAsync();
    }
}
