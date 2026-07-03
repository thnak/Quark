using Quark.Runtime;
using Xunit;

namespace Quark.Tests.Unit.SchedulingSemantics;

/// <summary>
///     Pins the mailbox ordering/capacity contract in <c>wiki/Lifecycle-and-Failure-Semantics.md</c>
///     — "Mailbox, ordering, and backpressure" — verified against <c>GrainActivation</c>'s
///     <c>Channel&lt;MailboxWorkItem&gt;</c>.
/// </summary>
public sealed class MailboxOrderingAndCapacityTests : IAsyncDisposable
{
    private readonly SchedulingSemanticsFixture _fixture = new();

    public ValueTask DisposeAsync() => _fixture.DisposeAsync();

    // Guarantee 1: FIFO, single-reader ordering — calls issued in program order (synchronously
    // enqueued on the calling thread, no concurrent Task.Run) execute in that same order.
    [Fact]
    public async Task Guarantee1_CallsExecute_InStrictEnqueueOrder()
    {
        ISchedulingGrain grain = _fixture.Client.GetGrain<ISchedulingGrain>("fifo");
        await grain.NoOpAsync(); // force activation first

        var calls = new List<Task>();
        for (int i = 0; i < 20; i++)
        {
            calls.Add(grain.RecordAsync(i));
        }
        await Task.WhenAll(calls);

        int[] order = await grain.GetOrderAsync();
        Assert.Equal(Enumerable.Range(0, 20), order);
    }

    // Guarantee 2: the default mailbox is unbounded — a burst far exceeding any reasonable
    // bound completes without a MailboxFullException.
    [Fact]
    public async Task Guarantee2_DefaultMailbox_IsUnbounded()
    {
        ISchedulingGrain grain = _fixture.Client.GetGrain<ISchedulingGrain>("unbounded");

        var calls = new List<Task>();
        for (int i = 0; i < 500; i++)
        {
            calls.Add(grain.NoOpAsync());
        }

        await Task.WhenAll(calls); // must not throw MailboxFullException
    }

    // Guarantee 3: a bounded mailbox in Wait mode makes the caller asynchronously wait for
    // space, rather than throwing — proven structurally via a gate, not a timing race.
    [Fact]
    public async Task Guarantee3_BoundedMailbox_WaitMode_CallerWaitsForSpace()
    {
        await using var fixture = new SchedulingSemanticsFixture(mailboxCapacity: 1, mailboxFullMode: MailboxFullMode.Wait);
        ISchedulingGrain grain = fixture.Client.GetGrain<ISchedulingGrain>("bounded-wait");
        await grain.NoOpAsync(); // force activation

        // t1 occupies the mailbox's single executing slot, blocked on the gate. Neither t1 nor
        // anything queued behind it can complete until the gate is released below.
        Task t1 = grain.BlockThenRecordAsync(1);
        await WaitUntilAsync(() => Task.FromResult(fixture.EntryLog.Snapshot().Contains(1)));

        // t2 fills the bounded queue's one remaining slot (the enqueue itself has room and
        // returns promptly; t2 as a whole only completes once t1 releases the mailbox).
        Task t2 = grain.NoOpAsync();
        await Task.Delay(TimeSpan.FromMilliseconds(50));

        // t3 has no room — with MailboxFullMode.Wait it must not throw, and must not complete
        // until the gate is released and t1's slot frees up.
        Task t3 = grain.NoOpAsync();
        await Task.Delay(TimeSpan.FromMilliseconds(100));
        Assert.False(t2.IsCompleted, "t2 should still be queued behind the blocked t1.");
        Assert.False(t3.IsCompleted, "Expected the caller to be waiting for mailbox space, not to have completed.");

        fixture.Gate.Release();
        await Task.WhenAll(t1, t2, t3).WaitAsync(TimeSpan.FromSeconds(5));
    }

    // Guarantee 4: a bounded mailbox in RejectWhenFull mode fails fast with MailboxFullException
    // instead of waiting.
    [Fact]
    public async Task Guarantee4_BoundedMailbox_RejectWhenFullMode_ThrowsImmediately()
    {
        await using var fixture = new SchedulingSemanticsFixture(mailboxCapacity: 1, mailboxFullMode: MailboxFullMode.RejectWhenFull);
        ISchedulingGrain grain = fixture.Client.GetGrain<ISchedulingGrain>("bounded-reject");
        await grain.NoOpAsync(); // force activation

        Task t1 = grain.BlockThenRecordAsync(1);
        await WaitUntilAsync(() => Task.FromResult(fixture.EntryLog.Snapshot().Contains(1)));

        // Fills the bounded queue's one remaining slot. The enqueue (TryWrite) itself is
        // synchronous and succeeds immediately; t2 as a whole only completes once t1 releases
        // the mailbox, so it must not be awaited to completion here.
        Task t2 = grain.NoOpAsync();
        await Task.Delay(TimeSpan.FromMilliseconds(50));

        // Mailbox is now full (1 executing + 1 queued against capacity 1) — the next enqueue
        // must fail fast rather than hang.
        await Assert.ThrowsAsync<MailboxFullException>(() => grain.NoOpAsync()).WaitAsync(TimeSpan.FromSeconds(5));

        fixture.Gate.Release();
        await Task.WhenAll(t1, t2).WaitAsync(TimeSpan.FromSeconds(5));
    }

    // Guarantee 5 (a faulted work item does not stop the loop) intentionally has no test here —
    // it overlaps with Quark.Tests.Unit.FailureSemantics.BehaviorThrowFailureSemanticsTests
    // .Guarantee4_MailboxContinuesProcessingAfterThrow, which is the canonical test for it.

    private static async Task WaitUntilAsync(Func<Task<bool>> conditionAsync, int timeoutMs = 2000)
    {
        long deadline = Environment.TickCount64 + timeoutMs;
        while (!await conditionAsync() && Environment.TickCount64 < deadline)
        {
            await Task.Delay(5);
        }
    }
}
