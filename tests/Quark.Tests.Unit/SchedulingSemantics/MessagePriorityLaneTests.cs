using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Quark.Core.Abstractions.Identity;
using Quark.Core.Abstractions.Scheduling;
using Quark.Runtime;
using Xunit;

namespace Quark.Tests.Unit.SchedulingSemantics;

/// <summary>
///     Validates the per-actor message-priority lanes added to <see cref="GrainActivation"/>'s mailbox:
///     the drain reads the highest-priority non-empty lane first, so a higher-priority message jumps
///     ahead of lower-priority messages already queued for the same grain, while messages within one
///     lane preserve arrival order (FIFO). The lanes live in the shared mailbox, so this holds for any
///     scheduler; the test drives it through the arena scheduler.
/// </summary>
public sealed class MessagePriorityLaneTests
{
    [Fact]
    public async Task HigherPriorityMessages_DrainAhead_LowerOnesQueuedFirst_FifoWithinLane()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var options = new SiloRuntimeOptions
        {
            ClusterId = "test",
            ServiceId = "message-priority-lanes",
            SiloName = "silo0",
            SchedulerMaxConcurrentActivations = 2,
            SchedulerKind = SchedulerKind.ArenaV2,
        };

        await using var scheduler = new ArenaScheduler(options);
        await using ServiceProvider root = services.BuildServiceProvider();

        var activation = new GrainActivation(
            new GrainId(new GrainType("PriorityGrain"), "p"),
            new GrainType("PriorityGrain"),
            isReentrant: false,
            root,
            NullLogger<GrainActivation>.Instance,
            scheduler);

        var order = new List<string>();
        void Record(string label)
        {
            lock (order) order.Add(label);
        }

        using var started = new ManualResetEventSlim(false);
        using var gate = new ManualResetEventSlim(false);

        // A blocker occupies the single drain synchronously, so everything posted below lands in the
        // mailbox lanes (they enqueue synchronously — unbounded lanes) before the drain reads any of it.
        Task blockerTask = activation.PostAsync(() =>
        {
            Record("blocker");
            started.Set();
            gate.Wait(TimeSpan.FromSeconds(10));
            return default;
        }).AsTask();

        Assert.True(started.Wait(TimeSpan.FromSeconds(10)), "blocker never started draining");

        // Post out of priority order, and two at Normal to check FIFO within a lane. Each PostAsync
        // enqueues synchronously to its lane before suspending on completion, so all five are queued
        // behind the blocker before the gate is released.
        Func<ValueTask> Make(string label) => () => { Record(label); return default; };

        Task low = activation.PostAsync(Make("low"), MessagePriority.Low).AsTask();
        Task urgent = activation.PostAsync(Make("urgent"), MessagePriority.Urgent).AsTask();
        Task normal1 = activation.PostAsync(Make("normal-1"), MessagePriority.Normal).AsTask();
        Task high = activation.PostAsync(Make("high"), MessagePriority.High).AsTask();
        Task normal2 = activation.PostAsync(Make("normal-2"), MessagePriority.Normal).AsTask();

        gate.Set();
        await Task.WhenAll(blockerTask, low, urgent, normal1, high, normal2).WaitAsync(TimeSpan.FromSeconds(15));

        List<string> drained;
        lock (order) drained = order.ToList();

        // blocker ran first (it was already draining), then strict priority order across lanes with
        // FIFO within the Normal lane (normal-1 posted before normal-2).
        Assert.Equal(
            new[] { "blocker", "urgent", "high", "normal-1", "normal-2", "low" },
            drained);

        await activation.DisposeAsync();
    }
}
