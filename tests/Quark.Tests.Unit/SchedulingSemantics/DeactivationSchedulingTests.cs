using Microsoft.Extensions.Logging.Abstractions;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;
using Quark.Runtime;
using Xunit;

namespace Quark.Tests.Unit.SchedulingSemantics;

/// <summary>
///     Pins the deactivation-scheduling contract in <c>wiki/Lifecycle-and-Failure-Semantics.md</c>
///     — deactivation is itself a mailbox item, so already-queued work drains first; nothing can
///     be enqueued after; and a post-deactivation call activates a fresh instance.
/// </summary>
public sealed class DeactivationSchedulingTests : IAsyncDisposable
{
    private readonly SchedulingSemanticsFixture _fixture = new();

    public ValueTask DisposeAsync() => _fixture.DisposeAsync();

    // Guarantee 8: work already queued ahead of a deactivation request drains before
    // deactivation runs; once deactivation completes, nothing can be posted to the old
    // activation, and the next client call gets a fresh one.
    [Fact]
    public async Task Guarantee8_QueuedWorkDrainsFirst_ThenNoFurtherPosts_ThenFreshActivation()
    {
        ISchedulingGrain grain = _fixture.Client.GetGrain<ISchedulingGrain>("deactivation-scheduling");
        await grain.NoOpAsync(); // force activation

        var grainId = new GrainId(new GrainType("SchedulingGrain"), "deactivation-scheduling");
        Assert.True(_fixture.ActivationTable.TryGetActivation(grainId, out GrainActivation? oldActivation));

        // t1 occupies the mailbox; t2/t3 and the deactivation request all queue up behind it,
        // in that order.
        Task t1 = grain.BlockThenRecordAsync(1);
        await WaitUntilAsync(() => Task.FromResult(_fixture.EntryLog.Snapshot().Contains(1)));
        Task t2 = grain.RecordAsync(2);
        Task t3 = grain.RecordAsync(3);
        Task tDestroy = grain.SelfDestructAsync();

        _fixture.Gate.Release();
        await Task.WhenAll(t1, t2, t3, tDestroy).WaitAsync(TimeSpan.FromSeconds(5));

        // The queued calls must have drained (in order) before deactivation tore anything down.
        int[] order = oldActivation!.GetOrCreateHolder<SchedulingState>().Value.Order.ToArray();
        Assert.Equal([1, 2, 3], order);

        await WaitUntilAsync(() => Task.FromResult(oldActivation.ActivationStatus == GrainActivationStatus.Inactive));

        // Nothing can be posted to the old, deactivated activation directly.
        await Assert.ThrowsAnyAsync<Exception>(
            () => oldActivation.PostAsync(() => ValueTask.CompletedTask).AsTask());

        // A call through the client after deactivation activates a fresh instance.
        await grain.NoOpAsync();
        Assert.True(_fixture.ActivationTable.TryGetActivation(grainId, out GrainActivation? newActivation));
        Assert.NotSame(oldActivation, newActivation);
    }

    // Guarantee 9: DelayDeactivation(TimeSpan) defers idle-collection eligibility past the
    // deadline it sets, and has no effect once that deadline has passed.
    [Fact]
    public void Guarantee9_DelayDeactivation_DefersIdleCollectionEligibility()
    {
        var grainId = new GrainId(new GrainType("DelayTest"), "1");
        var activation = new GrainActivation(grainId, grainId.Type, isReentrant: false,
            new NullServiceProvider(), NullLogger<GrainActivation>.Instance, SimpleActivationScheduler.Instance);

        DateTimeOffset now = DateTimeOffset.UtcNow;
        Assert.True(activation.IsDeactivationAllowed(now), "No delay has been set yet.");

        activation.DelayDeactivation(TimeSpan.FromMinutes(5));

        Assert.False(activation.IsDeactivationAllowed(now),
            "Deactivation should be deferred immediately after DelayDeactivation.");
        Assert.False(activation.IsDeactivationAllowed(now + TimeSpan.FromMinutes(4)),
            "Deactivation should still be deferred before the deadline.");
        Assert.True(activation.IsDeactivationAllowed(now + TimeSpan.FromMinutes(6)),
            "Deactivation should be allowed again once the deadline has passed.");
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> conditionAsync, int timeoutMs = 2000)
    {
        long deadline = Environment.TickCount64 + timeoutMs;
        while (!await conditionAsync() && Environment.TickCount64 < deadline)
        {
            await Task.Delay(5);
        }
    }

    private sealed class NullServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}
