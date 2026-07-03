using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;
using Quark.Core.Abstractions.Timers;
using Quark.Runtime;
using Xunit;

namespace Quark.Tests.Unit.FailureSemantics;

/// <summary>
///     Pins the documented contract for timer callbacks and deactivation-time failures
///     (<c>wiki/Lifecycle-and-Failure-Semantics.md</c>), verified against
///     <c>GrainTimer.OnFire</c> and <c>GrainActivation.RunDeactivationAsync</c>.
/// </summary>
public sealed class TimerAndDeactivationFailureSemanticsTests : IAsyncDisposable
{
    private readonly FailureSemanticsFixture _fixture = new();

    public ValueTask DisposeAsync() => _fixture.DisposeAsync();

    // Guarantee 5: registered timers keep ticking after an unrelated behavior-method throw
    // on the same activation.
    [Fact]
    public async Task Guarantee5_TimerKeepsTicking_AfterUnrelatedMethodThrow()
    {
        ITimerLifecycleGrain grain = _fixture.Client.GetGrain<ITimerLifecycleGrain>("t5");
        await grain.StartTimerAsync(timerThrows: false);

        _fixture.Clock.Advance(TimeSpan.FromMilliseconds(10));
        await WaitUntilAsync(async () => await grain.GetFireCountAsync() >= 1);

        await Assert.ThrowsAsync<InvalidOperationException>(() => grain.ThrowAsync("unrelated failure"));

        _fixture.Clock.Advance(TimeSpan.FromMilliseconds(10));
        await WaitUntilAsync(async () => await grain.GetFireCountAsync() >= 2);

        Assert.True(await grain.GetFireCountAsync() >= 2);
    }

    // Guarantee 8: a throwing timer callback is not suppressed silently, but the timer itself
    // is not stopped — it keeps firing on schedule.
    [Fact]
    public async Task Guarantee8_ThrowingTimerCallback_DoesNotStopTheTimer()
    {
        ITimerLifecycleGrain grain = _fixture.Client.GetGrain<ITimerLifecycleGrain>("t8");
        await grain.StartTimerAsync(timerThrows: true);

        _fixture.Clock.Advance(TimeSpan.FromMilliseconds(10));
        await WaitUntilAsync(async () => await grain.GetFireCountAsync() >= 1);

        _fixture.Clock.Advance(TimeSpan.FromMilliseconds(10));
        await WaitUntilAsync(async () => await grain.GetFireCountAsync() >= 2);

        Assert.True(await grain.GetFireCountAsync() >= 2,
            "Timer should keep firing on schedule even though every callback throws.");
    }

    // Guarantee 7: an OnDeactivateAsync throw is caught (doesn't propagate to the caller that
    // triggered deactivation), timers are disposed, and IManagedActivationMemory<T>.Destroy
    // still runs.
    [Fact]
    public async Task Guarantee7_OnDeactivateAsyncThrow_IsCaught_TimersDisposed_ManagedHolderDestroyed()
    {
        ITimerLifecycleGrain grain = _fixture.Client.GetGrain<ITimerLifecycleGrain>("t7");
        await grain.StartTimerAsync(timerThrows: false);

        var grainId = new GrainId(new GrainType("TimerLifecycleGrain"), "t7");
        Assert.True(_fixture.ActivationTable.TryGetActivation(grainId, out GrainActivation? activation));
        IGrainTimer timerHandle = activation!.GetOrCreateHolder<TimerLifecycleState>().Value.Timer!;

        // OnDeactivateAsync unconditionally throws in TimerLifecycleBehavior — this call must
        // still complete without propagating that exception to the caller.
        await grain.SelfDestructAsync();

        await WaitUntilAsync(() => Task.FromResult(activation.ActivationStatus == GrainActivationStatus.Inactive));

        Assert.True(_fixture.DeactivationTracker.ManagedHolderDestroyed,
            "IManagedActivationMemory<T>.Destroy should still run despite the OnDeactivateAsync throw.");
        Assert.Throws<ObjectDisposedException>(() => timerHandle.Change(TimeSpan.Zero, TimeSpan.Zero));
    }

    private static async Task WaitUntilAsync(Func<Task<bool>> conditionAsync, int timeoutMs = 2000)
    {
        long deadline = Environment.TickCount64 + timeoutMs;
        while (!await conditionAsync() && Environment.TickCount64 < deadline)
        {
            await Task.Delay(5);
        }
    }
}
