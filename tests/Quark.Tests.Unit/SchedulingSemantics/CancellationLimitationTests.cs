using Quark.Core.Abstractions.Identity;
using Xunit;

namespace Quark.Tests.Unit.SchedulingSemantics;

/// <summary>
///     Pins the documented cancellation limitation in
///     <c>wiki/Lifecycle-and-Failure-Semantics.md</c> — caller cancellation tokens are not
///     propagated into queued mailbox work today. This test should be updated when
///     <see href="https://github.com/thnak/Quark/issues/37">#37</see> (GrainCancellationToken)
///     lands.
/// </summary>
public sealed class CancellationLimitationTests : IAsyncDisposable
{
    private readonly SchedulingSemanticsFixture _fixture = new();

    public ValueTask DisposeAsync() => _fixture.DisposeAsync();

    // Guarantee 10: cancelling a caller's token while its call is already sitting in the
    // mailbox queue (not yet dequeued) does not stop it from running — the mailbox has no way
    // to observe that cancellation once the work is posted.
    [Fact]
    public async Task Guarantee10_CancellingWhileQueued_DoesNotPreventTheWorkFromRunning()
    {
        var grainId = new GrainId(new GrainType("SchedulingGrain"), "cancellation");
        ISchedulingGrain grain = _fixture.Client.GetGrain<ISchedulingGrain>("cancellation");
        await grain.NoOpAsync(); // force activation

        // Occupies the mailbox so the call below sits queued (not yet dequeued) while its
        // token is cancelled.
        Task blocker = grain.BlockThenRecordAsync(1);
        await WaitUntilAsync(() => Task.FromResult(_fixture.EntryLog.Snapshot().Contains(1)));

        using var cts = new CancellationTokenSource();
        // Bypass the hand-written proxy (which doesn't expose a CancellationToken parameter)
        // and call the invoker directly, the same way the generated proxies do.
        Task queued = _fixture.CallInvoker.InvokeVoidAsync(
            grainId, new SchedulingGrain_RecordInvokable(42), cts.Token).AsTask();

        await cts.CancelAsync(); // cancel while `queued` is still sitting behind `blocker`

        _fixture.Gate.Release();
        await Task.WhenAll(blocker, queued).WaitAsync(TimeSpan.FromSeconds(5));

        int[] order = await grain.GetOrderAsync();
        Assert.Equal([1, 42], order);
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
