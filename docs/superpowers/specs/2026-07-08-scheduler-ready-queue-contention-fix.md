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

## 6. Expected outcome

Not a guessable exact multiplier — the point of step 5.3 above is to measure it — but since ~85% of
sampled thread-time was going into contended-lock waiting rather than useful execution, and the
majority of that traces to a structure this fix removes the single-point-of-contention nature of,
a multiple-x improvement in PingPong's sustained throughput is the reasonable expectation, not a
marginal percentage change.
