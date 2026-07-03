using Xunit;

namespace Quark.Tests.Unit.FailureSemantics;

/// <summary>
///     Pins the documented contract for what happens when activation itself fails
///     (<c>wiki/Lifecycle-and-Failure-Semantics.md</c> — "When activation itself fails"),
///     verified against <c>LocalGrainCallInvoker.GetOrActivateAsync</c> →
///     <c>GrainActivationTable.RemoveIfFaulted</c>.
/// </summary>
public sealed class ActivationFailureSemanticsTests : IAsyncDisposable
{
    private readonly FailureSemanticsFixture _fixture = new();

    public ValueTask DisposeAsync() => _fixture.DisposeAsync();

    // Guarantee 6: a faulted activation (behavior ctor throws) is evicted from the activation
    // table, and propagates to the caller — so the next call attempts a fresh activation rather
    // than replaying the same cached failure forever.
    [Fact]
    public async Task Guarantee6_FaultedActivation_IsEvicted_NextCallGetsFreshActivation()
    {
        IFlakyActivationGrain grain = _fixture.Client.GetGrain<IFlakyActivationGrain>("flaky");

        _fixture.ActivationGate.ShouldFail = true;
        await Assert.ThrowsAsync<InvalidOperationException>(() => grain.PingAsync());
        int attemptsAfterFailure = _fixture.ActivationGate.AttemptCount;
        Assert.True(attemptsAfterFailure >= 1);

        // If the faulted entry were NOT evicted, GrainActivationTable.GetOrCreateAsync would keep
        // returning the same already-faulted Task forever, and this call would throw the exact
        // same cached exception even after the underlying cause is fixed.
        _fixture.ActivationGate.ShouldFail = false;
        int result = await grain.PingAsync();

        Assert.Equal(1, result);
        Assert.True(_fixture.ActivationGate.AttemptCount > attemptsAfterFailure,
            "Expected a new activation attempt (fresh construction) after the faulted entry was evicted.");
    }
}
