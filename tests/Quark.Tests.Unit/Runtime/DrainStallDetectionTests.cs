using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Quark.Core.Abstractions.Identity;
using Quark.Runtime;
using Xunit;

namespace Quark.Tests.Unit.Runtime;

/// <summary>
///     Regression coverage for the livelock found while debugging hung tests: a drain pass that
///     runs with an already-cancelled token while the mailbox still has queued work processes
///     nothing (TryRead short-circuits) yet still reports <c>HasMoreWork</c> (TryPeek ignores
///     cancellation). Left undetected, a scheduler that keeps rescheduling on that signal spins
///     forever without ever making progress. <see cref="GrainActivation.ConsecutiveEmptyDrains"/>
///     is the counter <see cref="Quark.Diagnostics.StuckGrainDetector"/> watches for this.
/// </summary>
public sealed class DrainStallDetectionTests
{
    private static readonly GrainType Type = new("StallProbe");

    private static GrainActivation Create() =>
        new(new GrainId(Type, "g"), Type, isReentrant: false,
            new ServiceCollection().BuildServiceProvider(),
            NullLogger<GrainActivation>.Instance);

    [Fact]
    public async Task DrainAsync_IncrementsConsecutiveEmptyDrains_WhileCancelledWithWorkQueued()
    {
        await using GrainActivation activation = Create();

        // Claim the drain lock before posting so the fallback SimpleActivationScheduler never
        // gets a chance to race this test's manual drain calls.
        Assert.True(activation.TryBeginDrain());
        _ = activation.PostAsync(() => ValueTask.CompletedTask);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        ActivationDrainResult result = await activation.DrainAsync(int.MaxValue, cts.Token);
        activation.CompleteDrain(result);
        Assert.Equal(0, result.ItemsProcessed);
        Assert.True(result.HasMoreWork);
        Assert.Equal(1, activation.ConsecutiveEmptyDrains);

        Assert.True(activation.TryBeginDrain());
        result = await activation.DrainAsync(int.MaxValue, cts.Token);
        activation.CompleteDrain(result);
        Assert.Equal(2, activation.ConsecutiveEmptyDrains);

        Assert.True(activation.TryBeginDrain());
        result = await activation.DrainAsync(int.MaxValue, cts.Token);
        activation.CompleteDrain(result);
        Assert.Equal(3, activation.ConsecutiveEmptyDrains);
    }

    [Fact]
    public async Task DrainAsync_ResetsConsecutiveEmptyDrains_OnceWorkActuallyDrains()
    {
        await using GrainActivation activation = Create();

        Assert.True(activation.TryBeginDrain());
        _ = activation.PostAsync(() => ValueTask.CompletedTask);

        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        ActivationDrainResult stalled = await activation.DrainAsync(int.MaxValue, cts.Token);
        activation.CompleteDrain(stalled);
        Assert.Equal(1, activation.ConsecutiveEmptyDrains);

        // A drain with a live token processes the stranded item and clears the stall counter.
        Assert.True(activation.TryBeginDrain());
        ActivationDrainResult recovered = await activation.DrainAsync(int.MaxValue, CancellationToken.None);
        activation.CompleteDrain(recovered);

        Assert.Equal(1, recovered.ItemsProcessed);
        Assert.Equal(0, activation.ConsecutiveEmptyDrains);
    }
}
