using System.Collections.Concurrent;
using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;
using Quark.Runtime;
using Xunit;

namespace Quark.Tests.Unit.SchedulingSemantics;

/// <summary>
///     New scheduler tests per spec §15 items 1–4 and 9–11 (Phases 1–3 scope).
///     Items 5–8 are Phase 4 QoS scope — not yet implemented.
///     Item 10 (fault isolation) is also pinned by
///     <c>BehaviorThrowFailureSemanticsTests.Guarantee4_MailboxContinuesProcessingAfterThrow</c>;
///     this test exercises the same invariant at the scheduler-drain level.
/// </summary>
public sealed class ActivationSchedulerTests : IAsyncDisposable
{
    private readonly SchedulingSemanticsFixture _fixture = new();

    public ValueTask DisposeAsync() => _fixture.DisposeAsync();

    // §15 item 1: same-activation single-turn exclusivity — the second call must not enter
    // until the first call's work item has released the drain lock.
    [Fact]
    public async Task Spec1_SameActivation_SecondCallDoesNotEnterUntilFirstReleases()
    {
        _fixture.Gate.Reset();
        ISchedulingGrain grain = _fixture.Client.GetGrain<ISchedulingGrain>("sched-item1");
        await grain.NoOpAsync(); // force activation

        // t1 enters the grain and blocks on the gate; EntryLog records the entry.
        Task t1 = grain.BlockThenRecordAsync(1);
        await WaitUntilAsync(() => _fixture.EntryLog.Snapshot().Contains(1));

        // t2 is enqueued behind t1 in the mailbox but must not execute yet.
        Task t2 = grain.RecordAsync(2);
        await Task.Delay(50); // scheduler time to attempt a concurrent drain
        Assert.False(t2.IsCompleted, "Second call must not execute while first call is blocking.");

        _fixture.Gate.Release();
        await Task.WhenAll(t1, t2).WaitAsync(TimeSpan.FromSeconds(5));

        var grainId = new GrainId(new GrainType("SchedulingGrain"), "sched-item1");
        Assert.True(_fixture.ActivationTable.TryGetActivation(grainId, out GrainActivation? activation));
        int[] order = activation!.GetOrCreateHolder<SchedulingState>().Value.Order.ToArray();
        Assert.Equal([1, 2], order);
    }

    // §15 item 2: different activations can run concurrently — blocking one does not prevent
    // the other from making progress.
    [Fact]
    public async Task Spec2_DifferentActivations_ExecuteConcurrently()
    {
        _fixture.Gate.Reset();
        ISchedulingGrain grainA = _fixture.Client.GetGrain<ISchedulingGrain>("sched-item2a");
        ISchedulingGrain grainB = _fixture.Client.GetGrain<ISchedulingGrain>("sched-item2b");
        await Task.WhenAll(grainA.NoOpAsync(), grainB.NoOpAsync()); // force both activations

        // Block grain A. EntryLog index 20 is unique to this test.
        Task tA = grainA.BlockThenRecordAsync(20);
        await WaitUntilAsync(() => _fixture.EntryLog.Snapshot().Contains(20));

        // Grain B must complete while A is still blocked.
        await grainB.NoOpAsync().WaitAsync(TimeSpan.FromSeconds(5));

        _fixture.Gate.Release();
        await tA.WaitAsync(TimeSpan.FromSeconds(5));
    }

    // §15 item 3: activation barrier — many concurrent first-calls to the same un-activated
    // grain all succeed, and exactly one activation enters the table.
    [Fact]
    public async Task Spec3_ActivationBarrier_ConcurrentFirstCallsSucceed()
    {
        ISchedulingGrain grain = _fixture.Client.GetGrain<ISchedulingGrain>("sched-item3");

        var calls = new List<Task>();
        for (int i = 0; i < 20; i++)
            calls.Add(grain.NoOpAsync());

        await Task.WhenAll(calls).WaitAsync(TimeSpan.FromSeconds(5));

        var grainId = new GrainId(new GrainType("SchedulingGrain"), "sched-item3");
        Assert.True(_fixture.ActivationTable.TryGetActivation(grainId, out _),
            "One activation must exist after concurrent first-calls.");
    }

    // §15 item 4: deactivation reliability on bounded mailbox (Phase 3) — GrainActivation.Deactivate()
    // sets Deactivating status and schedules via the scheduler rather than writing to the mailbox.
    // A full bounded mailbox must not prevent deactivation from being scheduled and completing.
    [Fact]
    public async Task Spec4_BoundedMailbox_DeactivationCompletesEvenWhenFull()
    {
        await using var fixture = new SchedulingSemanticsFixture(mailboxCapacity: 1, mailboxFullMode: MailboxFullMode.Wait);
        ISchedulingGrain grain = fixture.Client.GetGrain<ISchedulingGrain>("sched-item4");
        await grain.NoOpAsync(); // force activation

        var grainId = new GrainId(new GrainType("SchedulingGrain"), "sched-item4");
        Assert.True(fixture.ActivationTable.TryGetActivation(grainId, out GrainActivation? activation));

        // t1 is executing (blocked on gate); t2 fills the single queue slot — mailbox at capacity.
        Task t1 = grain.BlockThenRecordAsync(41);
        await WaitUntilAsync(() => fixture.EntryLog.Snapshot().Contains(41));
        Task t2 = grain.NoOpAsync();
        await Task.Delay(50); // let t2 reach the queue

        // Phase 3: Deactivate() must not write to the mailbox and must not block.
        activation!.Deactivate(DeactivationReason.ApplicationRequested);

        // Pre-queued work drains first; DrainAsync then detects Deactivating and runs teardown.
        fixture.Gate.Release();
        await Task.WhenAll(t1, t2).WaitAsync(TimeSpan.FromSeconds(5));
        await WaitUntilAsync(() => activation!.ActivationStatus == GrainActivationStatus.Inactive);
        Assert.Equal(GrainActivationStatus.Inactive, activation!.ActivationStatus);
    }

    // §15 item 9: non-interleaving timer callback waits behind an active grain turn.
    // The timer fires while a grain call is blocking the drain lock; the callback is posted
    // to the mailbox and executes only after the blocking call completes.
    [Fact]
    public async Task Spec9_NonInterleavingTimer_WaitsBehindActiveGrainCall()
    {
        _fixture.Gate.Reset();
        ISchedulingGrain grain = _fixture.Client.GetGrain<ISchedulingGrain>("sched-item9");
        await grain.StartTimerAsync(interleave: false); // DueTime = 10 ms via FakeTimeProvider

        var grainId = new GrainId(new GrainType("SchedulingGrain"), "sched-item9");
        Assert.True(_fixture.ActivationTable.TryGetActivation(grainId, out GrainActivation? activation));
        SchedulingState state = activation!.GetOrCreateHolder<SchedulingState>().Value;

        // Block the mailbox. EntryLog index 90 is unique to this test.
        Task tBlock = grain.BlockThenRecordAsync(90);
        await WaitUntilAsync(() => _fixture.EntryLog.Snapshot().Contains(90));

        // Fire the timer while the drain is locked on tBlock.
        // The timer callback is written to the mailbox queue but cannot execute yet.
        _fixture.Clock.Advance(TimeSpan.FromMilliseconds(10));
        await Task.Delay(20); // allow the timer callback to reach the queue
        Assert.Equal(0, state.TimerFireCount);

        // Release the blocking call; the drain loop picks up the timer callback next.
        _fixture.Gate.Release();
        await tBlock.WaitAsync(TimeSpan.FromSeconds(5));
        await WaitUntilAsync(() => state.TimerFireCount >= 1);
        Assert.Equal(1, state.TimerFireCount);
    }

    // §15 item 10: fault isolation — a work item that throws must not stop drain processing.
    // The exception propagates to the caller via the mailbox work-item signal, and subsequent
    // items still execute (invariant 9 from spec §6).
    [Fact]
    public async Task Spec10_FaultIsolation_SubsequentWorkRunsAfterThrow()
    {
        ISchedulingGrain grain = _fixture.Client.GetGrain<ISchedulingGrain>("sched-item10");
        await grain.NoOpAsync(); // force activation

        var grainId = new GrainId(new GrainType("SchedulingGrain"), "sched-item10");
        Assert.True(_fixture.ActivationTable.TryGetActivation(grainId, out GrainActivation? activation));

        bool laterWorkRan = false;

        // Post a throwing work item followed by a subsequent item.
        Task throwTask = activation!.PostAsync(
            () => throw new InvalidOperationException("deliberate")).AsTask();
        Task laterTask = activation.PostAsync(() =>
        {
            laterWorkRan = true;
            return ValueTask.CompletedTask;
        }).AsTask();

        // The throwing item signals the caller with the exception but drain continues.
        await Assert.ThrowsAsync<InvalidOperationException>(() => throwTask);
        await laterTask.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(laterWorkRan, "Work queued after a throwing item must still execute.");
        Assert.Equal(GrainActivationStatus.Active, activation.ActivationStatus);
    }

    // §15 item 11: DisposeAsync awaits the full deactivation sequence before returning
    // (Phase 3: _completion TaskCompletionSource signal set by RunDeactivationAsync).
    [Fact]
    public async Task Spec11_DisposeAsync_WaitsForDeactivationSequenceToComplete()
    {
        await using var fixture = new SchedulingSemanticsFixture();
        ISchedulingGrain grain = fixture.Client.GetGrain<ISchedulingGrain>("sched-item11");
        await grain.NoOpAsync(); // force activation

        var grainId = new GrainId(new GrainType("SchedulingGrain"), "sched-item11");
        Assert.True(fixture.ActivationTable.TryGetActivation(grainId, out GrainActivation? activation));

        // Block the mailbox so DisposeAsync cannot flush immediately.
        Task tBlock = grain.BlockThenRecordAsync(1);
        await WaitUntilAsync(() => fixture.EntryLog.Snapshot().Contains(1));

        // DisposeAsync enqueues RunDeactivationAsync behind the blocking call.
        // It must not return until after RunDeactivationAsync completes.
        Task disposeTask = activation!.DisposeAsync().AsTask();
        await Task.Delay(100);
        Assert.False(disposeTask.IsCompleted,
            "DisposeAsync must not return while blocking work is still executing.");

        fixture.Gate.Release();
        await disposeTask.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(GrainActivationStatus.Inactive, activation.ActivationStatus);
    }

    // §15 item 5: drain budget fairness — a hot activation must yield to a colder one when the
    // budget is reached.  With drainBudget=2 and a single worker, activation A processes 2 items,
    // then yields so B (1 item) runs, then A continues.
    [Fact]
    public async Task Spec5_DrainBudget_HotActivationYieldsToOther()
    {
        await using var fixture = new SchedulingSemanticsFixture(configureScheduler: o =>
        {
            o.SchedulerDrainBudget = 2;
            o.SchedulerMaxConcurrentActivations = 1;
        });
        fixture.Gate.Reset();

        ISchedulingGrain grainA = fixture.Client.GetGrain<ISchedulingGrain>("budget5-a");
        ISchedulingGrain grainB = fixture.Client.GetGrain<ISchedulingGrain>("budget5-b");
        await Task.WhenAll(grainA.NoOpAsync(), grainB.NoOpAsync());

        var aId = new GrainId(new GrainType("SchedulingGrain"), "budget5-a");
        var bId = new GrainId(new GrainType("SchedulingGrain"), "budget5-b");
        Assert.True(fixture.ActivationTable.TryGetActivation(aId, out GrainActivation? aActivation));
        Assert.True(fixture.ActivationTable.TryGetActivation(bId, out GrainActivation? bActivation));

        var executionOrder = new ConcurrentQueue<string>();

        // A0: blocking item — occupies slot 1 of the budget-2 drain pass.
        Task a0 = aActivation!.PostAsync(async () =>
        {
            executionOrder.Enqueue("A0");
            await fixture.Gate.WaitAsync();
        }).AsTask();

        // Wait until A0 is executing (blocking on the gate).
        await WaitUntilAsync(() => executionOrder.Contains("A0"));

        // Enqueue A1-A9 while A0 blocks the single worker.
        var aTasks = Enumerable.Range(1, 9).Select(i =>
            aActivation.PostAsync(() => { executionOrder.Enqueue($"A{i}"); return ValueTask.CompletedTask; }).AsTask()
        ).ToList();
        await Task.Delay(30);

        // Enqueue B while A is holding the single worker. B enters the ready queue behind A.
        Task bTask = bActivation!.PostAsync(
            () => { executionOrder.Enqueue("B"); return ValueTask.CompletedTask; }).AsTask();
        await Task.Delay(30);

        // Release: drain finishes A0 (slot 1) + A1 (slot 2, budget reached). A yields. B runs next.
        fixture.Gate.Release();

        await Task.WhenAll(new[] { a0, bTask }.Concat(aTasks)).WaitAsync(TimeSpan.FromSeconds(10));

        string[] order = [.. executionOrder];
        int bIndex = Array.IndexOf(order, "B");

        Assert.True(bIndex > 0,
            $"B must not be first; A should have items before it. Order: [{string.Join(", ", order)}]");
        Assert.True(bIndex < order.Length - 1,
            $"B must not be last; some A items must follow. Order: [{string.Join(", ", order)}]");
    }

    // §15 item 6: scheduler concurrency cap of 1 — two activations must not drain simultaneously
    // when the cap is 1; the second must wait until the first's blocking turn completes.
    [Fact]
    public async Task Spec6_SchedulerConcurrencyCap_MaxOne_BlocksSecondActivation()
    {
        await using var fixture = new SchedulingSemanticsFixture(configureScheduler: o =>
        {
            o.SchedulerMaxConcurrentActivations = 1;
        });
        fixture.Gate.Reset();

        ISchedulingGrain grainA = fixture.Client.GetGrain<ISchedulingGrain>("cap1-a");
        ISchedulingGrain grainB = fixture.Client.GetGrain<ISchedulingGrain>("cap1-b");
        await Task.WhenAll(grainA.NoOpAsync(), grainB.NoOpAsync());

        // Block the single worker on grain A.
        Task tA = grainA.BlockThenRecordAsync(60);
        await WaitUntilAsync(() => fixture.EntryLog.Snapshot().Contains(60));

        // With only 1 worker, grain B cannot run while A is blocking it.
        Task tB = grainB.NoOpAsync();
        await Task.Delay(100);
        Assert.False(tB.IsCompleted,
            "With SchedulerMaxConcurrentActivations=1, B must not execute while A blocks the single worker.");

        fixture.Gate.Release();
        await Task.WhenAll(tA, tB).WaitAsync(TimeSpan.FromSeconds(5));
    }

    // §15 item 7: scheduler concurrency cap of 2 — two activations may drain simultaneously
    // when the cap is at least 2.
    [Fact]
    public async Task Spec7_SchedulerConcurrencyParallelism_MaxTwo_AllowsConcurrentActivations()
    {
        await using var fixture = new SchedulingSemanticsFixture(configureScheduler: o =>
        {
            o.SchedulerMaxConcurrentActivations = 2;
        });
        fixture.Gate.Reset();

        ISchedulingGrain grainA = fixture.Client.GetGrain<ISchedulingGrain>("par2-a");
        ISchedulingGrain grainB = fixture.Client.GetGrain<ISchedulingGrain>("par2-b");
        await Task.WhenAll(grainA.NoOpAsync(), grainB.NoOpAsync());

        // Block one worker on grain A.
        Task tA = grainA.BlockThenRecordAsync(70);
        await WaitUntilAsync(() => fixture.EntryLog.Snapshot().Contains(70));

        // With 2 workers, the second worker can drain grain B even while A is blocking.
        await grainB.NoOpAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.False(tA.IsCompleted, "A should still be blocked on the gate while B completed.");

        fixture.Gate.Release();
        await tA.WaitAsync(TimeSpan.FromSeconds(5));
    }

    // §15 item 8: ready queue rejection — a bounded scheduler ready queue with RejectWhenFull
    // throws SchedulerOverloadException when the queue is at capacity.
    [Fact]
    public async Task Spec8_ReadyQueueRejection_ThrowsSchedulerOverloadException()
    {
        await using var fixture = new SchedulingSemanticsFixture(configureScheduler: o =>
        {
            o.SchedulerMaxConcurrentActivations = 1;
            o.SchedulerReadyQueueCapacity = 1;
            o.SchedulerOverloadMode = SchedulerOverloadMode.RejectWhenFull;
        });
        fixture.Gate.Reset();

        ISchedulingGrain grainA = fixture.Client.GetGrain<ISchedulingGrain>("reject-a");
        ISchedulingGrain grainB = fixture.Client.GetGrain<ISchedulingGrain>("reject-b");
        ISchedulingGrain grainC = fixture.Client.GetGrain<ISchedulingGrain>("reject-c");
        // Pre-activate each grain with a settling delay between calls.
        // MailboxWorkItem uses ManualResetValueTaskSourceCore with RunContinuationsAsynchronously=false,
        // so SetResult runs test continuations synchronously on the worker thread. This causes
        // PostCoreAsync to TryWrite the grain back to the bounded ready queue (spurious entry) while
        // the worker is still mid-drain. The delay yields the test to the thread pool, letting the
        // worker complete its iteration and dequeue the spurious entry before the next activation.
        await grainA.NoOpAsync();
        await Task.Delay(10);
        await grainB.NoOpAsync();
        await Task.Delay(10);
        await grainC.NoOpAsync();
        await Task.Delay(10);

        // Block the single worker on A. The worker is now draining A; ready queue is empty.
        Task tA = grainA.BlockThenRecordAsync(80);
        await WaitUntilAsync(() => fixture.EntryLog.Snapshot().Contains(80));

        // B enters the ready queue (capacity=1, currently 0 → succeeds).
        Task tB = grainB.NoOpAsync();
        await Task.Delay(50); // let B's schedule land in the ready queue

        // C tries to enter the ready queue — it's full (capacity=1, B occupies it).
        SchedulerOverloadException ex = await Assert.ThrowsAsync<SchedulerOverloadException>(
            () => grainC.NoOpAsync());
        Assert.Equal(1, ex.Capacity);

        fixture.Gate.Release();
        await Task.WhenAll(tA, tB).WaitAsync(TimeSpan.FromSeconds(5));
    }

    private static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 3000)
    {
        long deadline = Environment.TickCount64 + timeoutMs;
        while (!condition() && Environment.TickCount64 < deadline)
            await Task.Delay(10);
    }
}
