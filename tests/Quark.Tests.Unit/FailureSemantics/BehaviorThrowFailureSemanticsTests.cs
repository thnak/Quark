using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;
using Quark.Runtime;
using Xunit;

namespace Quark.Tests.Unit.FailureSemantics;

/// <summary>
///     Pins the documented contract for what happens when a behavior method throws
///     (<c>wiki/Lifecycle-and-Failure-Semantics.md</c> — "When a behavior method throws"),
///     verified against <c>LocalGrainCallInvoker.InvokeAsync</c> and <c>GrainActivation</c>.
/// </summary>
public sealed class BehaviorThrowFailureSemanticsTests : IAsyncDisposable
{
    private readonly FailureSemanticsFixture _fixture = new();

    public ValueTask DisposeAsync() => _fixture.DisposeAsync();

    // Guarantee 1: the exception propagates to the caller with its original message.
    [Fact]
    public async Task Guarantee1_ExceptionPropagatesToCaller()
    {
        IFailureGrain grain = _fixture.Client.GetGrain<IFailureGrain>("g1");

        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => grain.ThrowAsync("boom"));

        Assert.Equal("boom", ex.Message);
    }

    // Guarantee 2: the activation survives a method throw — same GrainActivation instance
    // handles the next call, no deactivation is triggered.
    [Fact]
    public async Task Guarantee2_ActivationSurvivesAfterThrow()
    {
        IFailureGrain grain = _fixture.Client.GetGrain<IFailureGrain>("g2");
        await grain.SetAsync(1); // force activation before the throwing call
        var grainId = new GrainId(new GrainType("FailureGrain"), "g2");
        Assert.True(_fixture.ActivationTable.TryGetActivation(grainId, out GrainActivation? before));

        await Assert.ThrowsAsync<InvalidOperationException>(() => grain.ThrowAsync("boom"));

        Assert.True(_fixture.ActivationTable.TryGetActivation(grainId, out GrainActivation? after));
        Assert.Same(before, after);
        Assert.Equal(GrainActivationStatus.Active, after!.ActivationStatus);
    }

    // Guarantee 3: shell state mutations made before a throw are NOT rolled back.
    [Fact]
    public async Task Guarantee3_StateMutatedBeforeThrow_IsNotRolledBack()
    {
        IFailureGrain grain = _fixture.Client.GetGrain<IFailureGrain>("g3");

        await Assert.ThrowsAsync<InvalidOperationException>(() => grain.SetThenThrowAsync(42));

        int value = await grain.GetAsync();
        Assert.Equal(42, value);
    }

    // Guarantee 4: the mailbox keeps processing queued/subsequent calls after a throw.
    [Fact]
    public async Task Guarantee4_MailboxContinuesProcessingAfterThrow()
    {
        IFailureGrain grain = _fixture.Client.GetGrain<IFailureGrain>("g4");

        await Assert.ThrowsAsync<InvalidOperationException>(() => grain.ThrowAsync("boom"));
        await grain.SetAsync(7);
        int value = await grain.GetAsync();

        Assert.Equal(7, value);
    }
}
