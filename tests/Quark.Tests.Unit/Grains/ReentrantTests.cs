using Microsoft.Extensions.Logging.Abstractions;
using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Identity;
using Quark.Runtime;
using Xunit;

namespace Quark.Tests.Unit.Grains;

public sealed class ReentrantTests
{
    private static GrainActivation MakeActivation(bool isReentrant)
    {
        var grainId = new GrainId(new GrainType("G"), "1");
        return new GrainActivation(grainId, grainId.Type, isReentrant,
            new NullServiceProvider(), NullLogger<GrainActivation>.Instance);
    }

    [Fact]
    public async Task NonReentrantGrain_SerializesDispatch()
    {
        await using var activation = MakeActivation(isReentrant: false);

        using var gate = new SemaphoreSlim(0, 1);
        var task2Reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var task1 = activation.PostAsync(async () =>
        {
            await gate.WaitAsync(TimeSpan.FromSeconds(5));
        });

        var task2 = activation.PostAsync(() =>
        {
            task2Reached.SetResult();
            return ValueTask.CompletedTask;
        });

        // Structural proof of serialization, no wall-clock timing: task2 must not even start
        // (let alone finish) while task1 is still blocked on the gate — a non-reentrant
        // activation processes one mailbox item at a time.
        Task raced = await Task.WhenAny(task2Reached.Task, Task.Delay(TimeSpan.FromMilliseconds(200)));
        Assert.NotSame(raced, task2Reached.Task);
        Assert.False(task1.IsCompleted);
        Assert.False(task2.IsCompleted);

        gate.Release();
        await Task.WhenAll(task1.AsTask(), task2.AsTask()).WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(task2Reached.Task.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task ReentrantGrain_AllowsConcurrentDispatch()
    {
        await using var activation = MakeActivation(isReentrant: true);

        using var gate = new SemaphoreSlim(0, 1);
        var task2Reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var task1 = activation.PostAsync(async () =>
        {
            await gate.WaitAsync(TimeSpan.FromSeconds(5));
        });

        var task2 = activation.PostAsync(() =>
        {
            task2Reached.SetResult();
            return ValueTask.CompletedTask;
        });

        await task2Reached.Task.WaitAsync(TimeSpan.FromSeconds(5));
        gate.Release();
        await Task.WhenAll(task1.AsTask(), task2.AsTask()).WaitAsync(TimeSpan.FromSeconds(5));
    }

    // Phase 5: verify that deactivation of a reentrant grain still runs through the
    // lifecycle path (RunDeactivationAsync fires the _completion signal) and is NOT
    // skipped by the Immediate-mode bypass. DisposeAsync completes only when
    // RunDeactivationAsync has set the TaskCompletionSource — so the fact that this
    // test finishes is the proof that the lifecycle barrier is preserved.
    [Fact]
    public async Task ReentrantGrain_DisposeAsyncCompletesLifecyclePath()
    {
        var activation = MakeActivation(isReentrant: true);
        await activation.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    }

    // Phase 5: verify that deactivation via Deactivate() (e.g. idle collection) always
    // goes through the scheduler drain path — independent of the Immediate-mode bypass —
    // and that DisposeAsync awaits the completion signal set by that drain.
    [Fact]
    public async Task ReentrantGrain_ExternalDeactivationCompletesLifecyclePath()
    {
        var activation = MakeActivation(isReentrant: true);
        activation.MarkActive();
        activation.Deactivate(DeactivationReason.ShuttingDown);
        await activation.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
    }

    private sealed class NullServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }
}
