using Quark.Core.Abstractions.Identity;
using Quark.Runtime;
using Xunit;

namespace Quark.Tests.Unit.SchedulingSemantics;

/// <summary>
///     Pins the reentrancy contract in <c>wiki/Lifecycle-and-Failure-Semantics.md</c> — "Mailbox,
///     ordering, and backpressure" — for the timer-callback half not already covered elsewhere.
/// </summary>
/// <remarks>
///     Guarantee 6 ([Reentrant] behaviors execute calls immediately, allowing real interleaving)
///     and the plain-method half of guarantee 7 (non-reentrant behaviors never interleave) are
///     already pinned deterministically (gate-based, no wall-clock timing) in
///     <c>Quark.Tests.Unit.Grains.ReentrantTests</c> — that remains the canonical coverage; this
///     class only covers the timer-callback half of guarantee 7.
///
///     These tests read the fire count directly off the activation's state holder rather than
///     through a grain call: the grain is non-reentrant and the first tick is deliberately left
///     blocked in-flight, so a <c>GetTimerFireCountAsync()</c> call would itself queue up behind
///     it on the same mailbox and never return.
/// </remarks>
public sealed class ReentrancyAndTimerInterleaveTests : IAsyncDisposable
{
    private readonly SchedulingSemanticsFixture _fixture = new();

    public ValueTask DisposeAsync() => _fixture.DisposeAsync();

    // Guarantee 7 (timer half), Interleave = false: a due tick that arrives while the previous
    // tick's callback is still pending is silently suppressed — the fire count does not advance
    // no matter how many times the clock is advanced while the first tick is blocked.
    [Fact]
    public async Task Guarantee7_NonInterleavedTimer_SuppressesTicksWhilePreviousIsPending()
    {
        ISchedulingGrain grain = _fixture.Client.GetGrain<ISchedulingGrain>("timer-no-interleave");
        await grain.StartTimerAsync(interleave: false);

        var grainId = new GrainId(new GrainType("SchedulingGrain"), "timer-no-interleave");
        Assert.True(_fixture.ActivationTable.TryGetActivation(grainId, out GrainActivation? activation));
        SchedulingState state = activation!.GetOrCreateHolder<SchedulingState>().Value;

        _fixture.Clock.Advance(TimeSpan.FromMilliseconds(10));
        await WaitUntilAsync(() => state.TimerFireCount >= 1);

        // The first tick is now blocked on the gate (pending). Advancing further must not
        // increment the fire count — each due tick is suppressed rather than queued.
        for (int i = 0; i < 3; i++)
        {
            _fixture.Clock.Advance(TimeSpan.FromMilliseconds(10));
        }
        await Task.Delay(TimeSpan.FromMilliseconds(100));
        Assert.Equal(1, state.TimerFireCount);

        _fixture.Gate.Release();
        await grain.NoOpAsync(); // drains the mailbox up to and past the released tick
        Assert.Equal(1, state.TimerFireCount);
    }

    // Guarantee 7 (timer half), Interleave = true: every due tick is posted to the mailbox
    // regardless of whether the previous one is still pending — they queue up (the grain itself
    // is still non-reentrant, so they still run one at a time) instead of being dropped.
    [Fact]
    public async Task Guarantee7_InterleavedTimer_QueuesTicksInsteadOfDroppingThem()
    {
        ISchedulingGrain grain = _fixture.Client.GetGrain<ISchedulingGrain>("timer-interleave");
        await grain.StartTimerAsync(interleave: true);

        var grainId = new GrainId(new GrainType("SchedulingGrain"), "timer-interleave");
        Assert.True(_fixture.ActivationTable.TryGetActivation(grainId, out GrainActivation? activation));
        SchedulingState state = activation!.GetOrCreateHolder<SchedulingState>().Value;

        _fixture.Clock.Advance(TimeSpan.FromMilliseconds(10));
        await WaitUntilAsync(() => state.TimerFireCount >= 1);

        // The first tick is blocked on the gate, but three more due ticks are still posted
        // (queued behind it) rather than suppressed.
        _fixture.Clock.Advance(TimeSpan.FromMilliseconds(10));
        _fixture.Clock.Advance(TimeSpan.FromMilliseconds(10));
        _fixture.Clock.Advance(TimeSpan.FromMilliseconds(10));

        _fixture.Gate.Release();
        await WaitUntilAsync(() => state.TimerFireCount >= 4);
        Assert.True(state.TimerFireCount >= 4,
            "Expected the three queued ticks to run once the first tick's gate was released.");
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 2000)
    {
        long deadline = Environment.TickCount64 + timeoutMs;
        while (!condition() && Environment.TickCount64 < deadline)
        {
            await Task.Delay(5);
        }
    }
}
