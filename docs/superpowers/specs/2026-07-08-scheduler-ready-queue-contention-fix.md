# Scheduler ready-queue lock contention fix

## 1. Problem

Profiling the PingPong benchmark (`tests/Quark.Performance/PingPong`, 32 pairs / 64 grains, trivial
no-op messages, ~310K raw grain calls/s on a 32-core box) with `dotnet-trace --profile
dotnet-sampled-thread-time` shows the runtime is not CPU-bound on useful work — it's lock-bound:

- 85% of all sampled thread-time across 58 threads is `UNMANAGED_CODE_TIME` (native runtime code),
  not `CPU_TIME` (managed execution).
- 99.5% of that `UNMANAGED_CODE_TIME` is `Monitor.Enter_Slowpath` — i.e. a thread blocked trying to
  acquire an **actually contended** lock, not idle/parked thread-pool wait time.
- Tracing the immediate parent frame of every `Monitor.Enter_Slowpath` sample:

  | Site | Share of contended-lock samples |
  |---|---|
  | `Channel<T>` reader/writer internals (`UnboundedChannelReader.WaitToReadAsync`, `TryWrite`) | ~55% |
  | `ActivationScheduler.ScheduleAsync` (ready-queue channel ops) | ~38% |
  | DI container `ResolveService` | ~6% |
  | DI scope disposal (`ServiceProviderEngineScope.BeginDispose`) | ~0.5% |

  (The first two rows overlap in the raw symbol name because .NET's JIT shares one canonical
  implementation, `UnboundedChannelReader[System.__Canon].WaitToReadAsync`, across all
  `Channel<T>` instantiations over reference types — both the per-activation mailbox channel and
  the scheduler's shared ready-queue channel compile to the same method. The architectural read
  below explains why the ready-queue channel is the one that scales badly, and the mailbox channel
  is not the problem.)

Per-call DI scope creation — the thing I originally guessed would dominate — is real but small
(~6-7% of contention). It is not the fix target here.

## 2. Root cause

`ActivationScheduler` (`src/Quark.Runtime/ActivationScheduler.cs`) uses **one shared
`Channel<GrainActivation>`** as the silo-wide ready queue:

```csharp
_readyQueue = Channel.CreateUnbounded<GrainActivation>(
    new UnboundedChannelOptions { SingleWriter = false, AllowSynchronousContinuations = false });
    // SingleReader defaults to false

_workers = new Task[concurrency];   // concurrency = Environment.ProcessorCount by default
for (int i = 0; i < concurrency; i++)
    _workers[i] = Task.Run(() => RunWorkerAsync(_cts.Token));
```

Every grain activation on the silo, regardless of type or key, funnels its "I have work, schedule
me" signal through this **one** channel:

- **Writers**: every `GrainActivation.PostCoreAsync` calls `_scheduler.ScheduleAsync(this, ...)`,
  which writes to `_readyQueue`. With many concurrently-calling grains, this is inherently
  multi-writer (`SingleWriter = false` is correct and can't be relaxed without breaking
  multi-caller grains).
- **Readers**: `Environment.ProcessorCount` worker tasks (32 on this box) all call
  `_readyQueue.Reader.ReadAllAsync()` concurrently on the *same* channel. `SingleReader` is left at
  its default (`false`) because there genuinely are N concurrent readers.

`Channel<T>` with `SingleWriter = false, SingleReader = false` takes the library's full
lock-protected path for every read/write pair (registering/dequeuing waiting continuations under
an internal monitor) — there's no lock-free fast path available for the many-writer/many-reader
case. That's architecturally the most expensive `Channel<T>` configuration there is, and it's used
for the **one structure every single grain call in the process passes through**. It doesn't shard by
grain, activation, or anything else — every one of the 64 ping-pong grains, and every worker
thread, contends on the same lock.

By contrast, the **per-activation mailbox channel** (`GrainActivation.CreateQueue`,
`src/Quark.Runtime/GrainActivation.cs:93-103`) is already `SingleReader = true` — correct, because
`TryBeginDrain`/`CompleteDrain` guarantee only one drain is ever in flight per activation at a time.
That channel is not the architectural problem; the shared ready queue is.

This is invisible at AstroSim's scale/shape (real per-tick physics work between calls means the
scheduling rate per grain is much lower, so the shared channel isn't hot) and only shows up when
the payload is trivial and the call rate is very high — exactly PingPong's job to surface.

## 3. Proposed fix: shard the ready queue by worker

Replace the single shared `Channel<GrainActivation>` + N-reader-workers with **N independent
channels, one per worker**, each configured `SingleReader = true`. An activation is assigned to a
shard once, deterministically, and always re-enqueues to that same shard.

```csharp
private readonly Channel<GrainActivation>[] _readyQueues;   // one per worker, SingleReader = true
private readonly Task[] _workers;

// shard assignment: activation.GrainId.GetHashCode() (stable for the activation's lifetime)
private int ShardFor(GrainActivation activation) => (activation.GrainId.GetHashCode() & 0x7FFFFFFF) % _readyQueues.Length;

public async ValueTask ScheduleAsync(GrainActivation activation, CancellationToken ct = default)
{
    if (!activation.TryMarkScheduled()) return;
    int shard = ShardFor(activation);
    await _readyQueues[shard].Writer.WriteAsync(activation, ct).ConfigureAwait(false);
    // ... existing diagnostics/metrics, now optionally tagged with shard id
}

private async Task RunWorkerAsync(int shard, CancellationToken ct)
{
    await foreach (GrainActivation activation in _readyQueues[shard].Reader.ReadAllAsync(ct))
    {
        // ... existing drain logic unchanged; reschedule-with-more-work re-writes to _readyQueues[shard]
    }
}
```

Effects:

- **Read side**: each shard channel has exactly one reader (its dedicated worker task), so
  `SingleReader = true` is legal and takes `Channel<T>`'s cheaper, largely lock-free reader path.
  This removes the multi-reader contention entirely (the ~38% `ActivationScheduler.ScheduleAsync`
  bucket and a meaningful chunk of the ~55% `WaitToReadAsync` bucket).
- **Write side**: writes are spread across N independent channels/locks instead of 1, so
  writer-side contention drops roughly N-fold (N = worker count, default `Environment.ProcessorCount`).
- **No behavior change** to per-grain ordering: the mailbox channel (unchanged) still guarantees a
  single activation's own messages execute in order. The ready queue never provided cross-grain
  ordering guarantees, so partitioning which grains land on which shard changes nothing observable.
- **Bounded-queue semantics** (`SchedulerReadyQueueCapacity` / `SchedulerOverloadMode`) become
  per-shard rather than global. Document this as a behavior change: a capacity of `C` now means "up
  to `C` per shard," not "C silo-wide." Existing configs with a global capacity may need to divide by
  worker count to preserve the same aggregate bound — call this out in the changelog/migration note.

## 4. Non-goals for this fix

- **Work-stealing between shards.** Static hash-based sharding can starve one worker while others
  idle if traffic is skewed toward a few hot grains. Ping-pong's load is uniform across 64 grains,
  so this isn't visible here. Note it as a known limitation; a work-stealing follow-up is future
  work, not required for this fix.
- **Reducing DI-resolve lock contention (~6%).** Real, but small next to the ready-queue win; a
  separate, smaller investigation if it still matters after this fix lands.
- **Changing `SingleReader`/`SingleWriter` on the per-activation mailbox channel.** Already correct;
  not touched by this fix.
- **Reducing per-call diagnostics/metrics overhead** (Activity, two Meter calls, three
  `IQuarkDiagnosticListener` struct-event dispatches per message). Out of scope — separate,
  independently measurable question from the lock-contention finding here.

## 5. Validation plan

1. Implement the sharded `ActivationScheduler` behind the existing `IActivationScheduler` interface
   (no public API change — `SiloRuntimeOptions.SchedulerMaxConcurrentActivations` becomes the shard
   count as well as the worker count, which is already its meaning today).
2. Run the existing scheduler test suite (`tests/Quark.Tests.Unit/SchedulingSemantics/`) unchanged —
   it should pass without modification since ordering/fairness invariants are per-activation, not
   global.
3. Re-run the PingPong benchmark at the same scale (32 pairs / 10-25s) and compare:
   - Raw call rate (currently ~310K calls/s / ~621K-650K msg/s x2).
   - Re-profile with `dotnet-trace --profile dotnet-sampled-thread-time` and confirm the
     `Monitor.Enter_Slowpath` share of `UNMANAGED_CODE_TIME` drops materially, and that the
     `ActivationScheduler.ScheduleAsync`/`WaitToReadAsync` parent-frame contention counts fall.
4. Re-run AstroSim (10M bodies / 32³ grid) to confirm no regression at the "real work between calls"
   shape this fix wasn't targeting.
5. Confirm `pidstat` shows the same or better total CPU utilization (the fix should convert wasted
   lock-wait time into useful work, not just move it around).

## 6. Measured outcome (implemented and profiled 2026-07-09)

The multiple-x expectation in the original version of this section did **not** hold. Actual result,
across three implementation iterations, each verified against the full `SchedulingSemantics` suite
and re-profiled with `dotnet-trace`:

| Iteration | Design | PingPong (32 pairs, x2) | `Monitor.Enter_Slowpath` samples |
|---|---|---|---|
| Baseline | Single shared ready-queue channel | 621,290 msg/s | ~75,300 |
| v1 | Sharded writes, each worker sweeps all shards, re-registers `WaitToReadAsync` on all shards when idle | 270,970 msg/s (**regression**) | similar order |
| v2 | Sharded writes/sweep, idle wait replaced with one shared `SemaphoreSlim`, released unconditionally on every write | 632,448 msg/s | ~68,200 |
| v3 (final) | Same as v2, plus approximate per-shard `Interlocked` counters so the sweep skips empty shards without touching their `Channel<T>` lock, and the semaphore is released only on a shard's 0→1 transition | **665,962 msg/s** | ~67,900 |

Final result: **+7% over baseline** (621K → 666K msg/s), not a multiple. `Monitor.Enter_Slowpath`
sample counts barely moved across v2/v3 relative to baseline, despite the redesigns — confirming
the finding below is structural, not an implementation gap this session ran out of time to close.

### Why the multiple-x hypothesis was wrong

Section 3's original design (each worker owns one shard exclusively, no cross-shard reads) was
never implemented as originally written: `tests/Quark.Tests.Unit/SchedulingSemantics/ActivationSchedulerTests.cs`
`Spec7_SchedulerConcurrencyParallelism_MaxTwo_AllowsConcurrentActivations` requires that with
`SchedulerMaxConcurrentActivations = 2`, any 2 distinct busy activations can run truly concurrently
— a guarantee static hash-sharding cannot provide (two activations can collide onto the same shard,
serializing them behind one worker regardless of the configured concurrency). Preserving that
guarantee requires every worker to be able to service every shard, which reintroduces exactly the
kind of cross-shard synchronization this fix set out to remove — just spread across more objects
(shard locks visited during the sweep, plus a new semaphore) rather than concentrated in one.

Sharding only pays off when a shard holds enough queued depth that most operations hit an
already-nonempty (cheap `TryRead`) shard instead of triggering the expensive empty→waiting→wake
cycle. PingPong's workload — one message in flight per grain, strict turn-taking, immediate
turnaround — keeps every shard oscillating between exactly 0 and 1 items almost constantly. That's
close to the worst case for `Channel<T>`'s synchronization regardless of how many shards it's split
across; it was also the worst case for AstroSim-style workloads too, just less visible there because
AstroSim's call rate is far lower (real per-tick work between calls).

A design that would plausibly deliver the originally-hoped-for multiple would need genuine
work-stealing (idle workers steal from a specific other shard only when evidence suggests real
backlog there, not scan all shards unconditionally) or a fundamentally different notification
primitive than `Channel<T>`/`SemaphoreSlim`'s lock-based wake path. Both are substantially larger
and riskier changes than this fix, in code that has previously had real, hard-to-find concurrency
bugs ([[project_mailbox_workitem_pool_race]]). Not attempted in this pass.

### Disposition

Shipped as a real, fully-tested, zero-regression **+7%** improvement (verified: full
`SchedulingSemantics` suite passing across 6+ repeated runs including the concurrency-cap liveness
tests, full `Quark.Tests.Unit`/`Quark.Tests.Integration` suites showing only pre-existing documented
flaky-test signatures, AstroSim re-run at 1M-body scale showing no regression). The bigger win
identified during this investigation — a genuine work-stealing scheduler — is out of scope for this
fix and would need its own design spec if pursued.
