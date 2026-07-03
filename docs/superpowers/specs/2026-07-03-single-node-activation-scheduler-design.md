# Design: Single-node activation scheduler
**Issue:** none yet
**Date:** 2026-07-03
**Status:** Draft - approved direction, ready for issue breakdown

---

## 1. Problem statement

`GrainActivation` currently starts one processing task per activation:

```csharp
_processingLoop = RunLoopAsync(_cts.Token);
```

That loop reads a per-activation `Channel<MailboxWorkItem>` and executes one work item at a
time. The model is simple and correct for non-reentrant grains, but it makes each activation
own its own dispatch loop. That limits Quark's ability to provide engine-level control over
throughput, fairness, overload behavior, stateless-worker concurrency, and scheduling
diagnostics on a single silo.

Quark should move from "each activation runs itself" to "the engine schedules activations."
The activation should still own identity, state, lifecycle, and its mailbox; the scheduler
should own when ready activations get CPU time.

## 2. Goal

Introduce a single-node, engine-owned activation scheduler that replaces permanent
per-activation processing loops with centralized activation dispatch.

The scheduler must improve throughput, fairness, and quality-of-service controls while
preserving Quark's actor execution contract:

> A non-reentrant activation executes at most one mailbox turn at a time.

This is not OS-thread affinity. A turn may resume on different .NET ThreadPool threads after
`await`, but no second non-reentrant turn may enter the same activation while the first turn
is still running.

## 3. Non-goals

- Do not implement a custom `System.Threading.Tasks.TaskScheduler`.
- Do not replace the .NET ThreadPool or take ownership of arbitrary async continuations.
- Do not design cluster-wide scheduling, placement, or load balancing in this spec.
- Do not change grain API surface for normal users.
- Do not make stateless workers stateful logical grains. Their activation memory remains
  per worker activation.
- Do not solve every reentrancy deadlock automatically. The scheduler may expose better
  detection and policy hooks, but analyzers and diagnostics remain part of the story.

## 4. Current model, verified against source

`src/Quark.Runtime/GrainActivation.cs`:

- A `Channel<MailboxWorkItem>` is created per activation.
- A `_processingLoop` task is started in the constructor.
- `RunLoopAsync` drains the channel with one reader.
- `PostAsync` queues work for non-reentrant activations.
- `[Reentrant]` activations bypass the queue and run `workItem()` immediately.
- `Deactivate` writes directly to `_queue.Writer.TryWrite(...)` and attaches
  `_processingLoop.ContinueWith(...)` to run the activation-table removal callback.
- `DisposeAsync` posts deactivation work, cancels the activation, completes the channel, and
  awaits `_processingLoop`.

`src/Quark.Runtime/LocalGrainCallInvoker.cs`:

- Activation is created, then `RunActivationAsync` is posted through `activation.PostAsync`.
- `MarkActive` runs after `OnActivateAsync`.
- Normal calls are posted through `activation.PostAsync`.

`src/Quark.Runtime/GrainTimer.cs`:

- Timer callbacks post through the activation's `PostAsync`, so non-interleaving timers share
  the mailbox path.

Existing tests under `tests/Quark.Tests.Unit/SchedulingSemantics` pin FIFO ordering, bounded
mailbox behavior, deactivation scheduling, timer interleaving, and cancellation limitations.
The scheduler must preserve or deliberately update those tests.

## 5. Design approach

Choose an engine-owned `ActivationScheduler`, not a custom .NET `TaskScheduler`.

```text
LocalGrainCallInvoker
  -> GrainActivation.PostAsync(...)
  -> activation mailbox enqueue
  -> ActivationScheduler.Schedule(activation)
  -> scheduler worker drains activation
  -> MailboxWorkItem.ExecuteAsync()
```

`GrainActivation` owns:

- `GrainId`
- `GrainType`
- activation memory holders
- managed activation memory holders
- lifecycle status
- mailbox storage
- pending work count
- scheduled/running/closed flags
- lifecycle work item creation

`ActivationScheduler` owns:

- global ready queue
- max active activation drains
- drain budget / fairness policy
- scheduler overload policy
- stateless-worker execution limits
- scheduler-level diagnostics and metrics

The first implementation should preserve the existing FIFO mailbox semantics. More advanced
priority and reentrancy behavior can be layered after the single-drain scheduler is proven.

## 6. Hard invariants

These are non-negotiable.

1. **Single-turn execution:** a non-reentrant activation runs at most one mailbox item at a
   time.
2. **Per-activation FIFO:** normal work executes in enqueue order for a single activation.
3. **Activation barrier:** no normal call, timer, reminder, stream item, or deactivation hook
   runs before `RunActivationAsync` and `MarkActive` complete.
4. **Lifecycle path consistency:** activation, normal calls, timers, reminders, streams, and
   deactivation all enter through scheduler-owned activation work.
5. **Reliable deactivation:** accepted deactivation work cannot be silently dropped because a
   bounded mailbox is full.
6. **Terminal deactivation:** once deactivation is accepted, no new normal work may enter the
   old activation.
7. **Drain-before-deactivate:** work already queued ahead of deactivation drains before
   teardown, matching current documented semantics.
8. **Mailbox backpressure compatibility:** `MailboxCapacity` and `MailboxFullMode` preserve
   current wait/reject behavior for normal work.
9. **Failure isolation:** an exception in one mailbox item does not stop later queued work.
10. **ThreadPool execution:** the scheduler dispatches activation drains to normal .NET
    execution; it does not own all async continuations.

## 7. Scheduler state model

Each activation needs explicit scheduler state. Suggested internal fields:

```csharp
private int _scheduled;        // 0 = not in scheduler ready queue, 1 = scheduled
private int _running;          // 0 = not draining, 1 = currently draining
private int _acceptingWork;    // 1 until terminal deactivation starts
private TaskCompletionSource? _completion;
```

The exact representation can change, but the behavior must be:

- the first enqueue after an idle mailbox schedules the activation;
- additional enqueues while scheduled/running do not schedule duplicates;
- only one non-reentrant drain may run at a time;
- after a drain yields or empties the mailbox, the activation either reschedules itself if
  work remains or clears `_scheduled`;
- deactivation completion signals an activation completion awaitable used by `DisposeAsync`
  and activation-table removal.

## 8. API shape

Internal scheduler interface:

```csharp
internal interface IActivationScheduler : IAsyncDisposable
{
    ValueTask ScheduleAsync(GrainActivation activation, CancellationToken cancellationToken = default);
}
```

`GrainActivation` exposes a narrow drain surface to the scheduler:

```csharp
internal bool TryBeginDrain();
internal ValueTask<ActivationDrainResult> DrainAsync(int maxItems, CancellationToken cancellationToken);
internal void CompleteDrain(ActivationDrainResult result);
```

The implementation should avoid exposing raw queues to the scheduler. The scheduler should
coordinate activations; `GrainActivation` should retain mailbox ownership.

Result shape:

```csharp
internal readonly record struct ActivationDrainResult(
    bool HasMoreWork,
    bool IsCompleted,
    int ItemsProcessed);
```

This is intentionally small. Priority lanes, request TTL, and richer scheduling policy can be
added later without exposing mailbox internals now.

## 9. Options

Add scheduler options to `SiloRuntimeOptions`:

```csharp
public int SchedulerMaxConcurrentActivations { get; set; } = Environment.ProcessorCount;
public int SchedulerDrainBudget { get; set; } = 64;
public int SchedulerReadyQueueCapacity { get; set; }
public SchedulerOverloadMode SchedulerOverloadMode { get; set; } = SchedulerOverloadMode.Wait;
```

Add:

```csharp
public enum SchedulerOverloadMode
{
    Wait = 0,
    RejectWhenFull = 1,
}
```

Defaults should preserve today's behavior as much as possible:

- unbounded ready queue by default;
- bounded per-activation mailbox behavior remains unchanged;
- max concurrent activation drains defaults to `Environment.ProcessorCount`, with a lower
  bound of `1`;
- drain budget defaults to `64`, with a lower bound of `1`.

Per-grain-type policy can override these later, preferably through the existing
grain-type-policy design track rather than a scheduler-only one-off API.

## 10. Quality-of-service requirements

The scheduler must provide local QoS primitives:

- **global concurrency cap:** limit the number of activation drains running at once;
- **fairness budget:** a hot activation yields after processing `SchedulerDrainBudget` items
  if other activations are waiting;
- **ready queue overload policy:** wait or reject when a bounded scheduler ready queue is
  full;
- **mailbox overload policy:** preserve existing per-activation bounded mailbox behavior;
- **diagnostics:** expose queue depth, drain duration, processed item count, yield count,
  overload rejections, and activation wait time.

The scheduler should favor fairness over maximum single-grain throughput. A hot activation
may process many turns, but it must not monopolize all scheduler workers while colder
activations wait.

## 11. Reentrancy policy

Current `[Reentrant]` behavior bypasses the mailbox:

```csharp
if (_isReentrant)
{
    return workItem();
}
```

That bypass is not scheduler-compatible as an engine policy because it skips mailbox
diagnostics, fairness, QoS, and lifecycle coordination.

Scheduler v1 should preserve public compatibility but make reentrancy explicit:

- non-reentrant activations use single-drain execution;
- reentrant activations should still enter scheduler-owned dispatch;
- reentrant execution may allow multiple concurrent drains only under a specific policy;
- reentrant lifecycle work remains serialized with activation/deactivation barriers;
- tests must document the exact compatibility behavior.

If preserving today's fully immediate reentrant behavior is necessary for compatibility, it
should be isolated behind a named scheduler policy such as `ReentrantSchedulingMode.Immediate`
and marked as a compatibility mode, not the model for new scheduler features.

## 12. Stateless worker policy

`[StatelessWorker]` should become engine-managed parallelism, not uncontrolled activation
multiplication.

The scheduler should support stateless-worker limits:

```text
MaxLocalActivations
MaxConcurrentExecutions
QueueCapacity
OverloadMode
```

Semantics:

- `MaxLocalActivations` caps how many worker activations of a stateless grain type may exist
  on one silo;
- `MaxConcurrentExecutions` caps how many of those worker activations may be actively
  draining at the same time;
- excess work queues according to the pool's queue policy;
- stateless-worker activation memory is per worker activation, not logical grain state.

The first scheduler implementation can defer the full stateless-worker pool, but the
activation scheduler design must not block it. In particular, scheduler APIs should allow a
future policy layer to choose which worker activation receives work.

## 13. Lifecycle requirements

Activation:

- activation work is the first mailbox item;
- activation must complete before `GetOrActivateAsync` returns;
- `OnGrainActivated` diagnostics and `GrainActivationsCreated` metrics fire only after
  activation succeeds;
- if activation fails, the activation table removes the faulted entry as today.

Normal call:

- enqueued through `PostAsync`;
- executes in a fresh DI scope;
- reports invocation diagnostics as today;
- propagates user exceptions to the caller.

Timer/reminder/stream:

- posts through the same scheduler-owned activation work path;
- non-interleaving timer semantics are preserved;
- interleaving timer behavior must be documented against the new reentrancy policy.

Deactivation:

- accepted deactivation transitions the activation into a non-accepting state for new normal
  work;
- deactivation work is scheduled reliably even when a bounded mailbox is full;
- already-queued work ahead of deactivation drains before teardown;
- `OnDeactivateAsync` runs through the lifecycle hook path;
- timers are disposed before lifecycle teardown as today;
- managed activation memory is disposed after `OnDeactivateAsync`;
- activation completion callback removes the activation from `GrainActivationTable`;
- posts to the old inactive activation fail predictably.

Dispose:

- `DisposeAsync` awaits activation completion through an explicit completion signal, not a
  per-activation loop task;
- shutdown deactivation is scheduled through the same lifecycle path;
- cancellation should stop future scheduling but should not corrupt work item completion.

## 14. Diagnostics and metrics

Add scheduler-level diagnostics to `IQuarkDiagnosticListener` or a sibling internal surface:

- scheduler ready queue depth changed;
- activation scheduled;
- activation drain started;
- activation drain completed;
- activation drain yielded because budget was reached;
- scheduler overload rejected work;
- activation waited in ready queue for duration;
- activation skipped because already scheduled/running.

Add metrics:

- `quark.scheduler.ready_queue.depth`
- `quark.scheduler.activation_wait.duration`
- `quark.scheduler.drain.duration`
- `quark.scheduler.drain.items`
- `quark.scheduler.drain.yields`
- `quark.scheduler.overload.rejections`
- `quark.scheduler.active_drains`

Metric names can be adjusted to match existing `QuarkInstruments` naming style during
implementation, but the information must be available.

## 15. Test plan

Existing tests to keep passing or intentionally update:

- `MailboxOrderingAndCapacityTests`
- `DeactivationSchedulingTests`
- `ReentrancyAndTimerInterleaveTests`
- `BehaviorThrowFailureSemanticsTests`
- `BoundedMailboxTests`
- `GrainActivationLifecycleTests`
- `GrainTimerTests`

New scheduler tests:

1. **Same activation single-turn:** enqueue two blocking calls to one non-reentrant
   activation; prove the second does not enter until the first releases.
2. **Different activations concurrent:** block one activation and prove another activation
   can execute while the first is blocked.
3. **Activation barrier:** enqueue normal work during activation and prove it executes after
   `OnActivateAsync`.
4. **Deactivation reliability:** with a bounded mailbox at capacity, request deactivation and
   prove it is scheduled and completes.
5. **Drain budget fairness:** enqueue many items to activation A and one item to activation B;
   prove B executes before A drains all items when a small drain budget is configured.
6. **Scheduler concurrency cap:** configure max active drains to `1`; prove two activations do
   not execute concurrently.
7. **Scheduler concurrency parallelism:** configure max active drains to `2`; prove two
   activations may execute concurrently.
8. **Ready queue rejection:** configure bounded scheduler ready queue with
   `RejectWhenFull`; prove overload produces a scheduler-specific exception.
9. **Timer scheduler path:** start a non-interleaving timer while a grain call is blocked;
   prove timer callback waits behind the active turn.
10. **Fault isolation:** throw from one scheduled work item and prove later work still runs.
11. **Dispose completion:** dispose an activation and prove `DisposeAsync` waits until
   deactivation cleanup completes.
12. **Stateless worker cap, future task:** once stateless-worker pool exists, prove only
   `MaxConcurrentExecutions` workers run even when many calls arrive.

## 16. Implementation phases

### Phase 1: Scheduler abstraction behind current semantics

- Add `IActivationScheduler`.
- Register a default scheduler in `RuntimeServiceCollectionExtensions`.
- Pass scheduler into `GrainActivation`.
- Route `PostAsync` enqueue notifications through scheduler.
- Keep the current loop if needed as a transitional internal implementation.
- Add tests around scheduler scheduling hooks without changing semantics.

### Phase 2: Remove permanent per-activation loop

- Remove constructor-started `_processingLoop`.
- Add activation scheduled/running/completion state.
- Add `DrainAsync` owned by `GrainActivation`.
- Add a central scheduler worker/ready queue.
- Preserve per-activation FIFO and mailbox capacity behavior.

### Phase 3: Lifecycle hardening

- Replace raw deactivation `_queue.Writer.TryWrite(...)` with lifecycle-aware enqueue.
- Replace `_processingLoop.ContinueWith(...)` with explicit activation completion.
- Update `DisposeAsync` to await activation completion.
- Ensure activation barrier remains posted and awaited.

### Phase 4: QoS

- Add scheduler options to `SiloRuntimeOptions`.
- Enforce global concurrent drain cap.
- Enforce drain budget and rescheduling.
- Add scheduler diagnostics and metrics.
- Add overload exception for scheduler ready queue rejection.

### Phase 5: Reentrancy policy

- Replace direct reentrant bypass with explicit scheduler policy.
- Decide whether v1 preserves immediate compatibility mode or moves to scheduler-managed
  concurrent drains.
- Update docs and tests for the selected behavior.

### Phase 6: Stateless worker policy

- Add stateless-worker pool policy layer.
- Enforce max local activations and max concurrent executions.
- Document per-worker activation memory semantics.
- Add stress and fairness tests for stateless-worker bursts.

## 17. Open decisions

1. **Reentrant compatibility:** preserve immediate bypass as compatibility mode, or move
   `[Reentrant]` immediately into scheduler-managed concurrent drains?
   Recommendation: preserve behavior first, isolate it behind a named policy, then evolve.

2. **Deactivation priority:** should deactivation be a priority lane, or strictly FIFO after
   already-queued work?
   Recommendation: keep today's drain-before-deactivate FIFO semantics. Make deactivation
   non-droppable but not priority over already-queued normal work.

3. **Ready queue capacity default:** unbounded or bounded by default?
   Recommendation: unbounded by default to preserve current behavior; operators can bound it.

4. **Drain budget default:** what is the first default?
   Recommendation: `64`, validated later with benchmarks.

5. **Scheduler diagnostics surface:** extend `IQuarkDiagnosticListener` directly or add a
   scheduler-specific listener?
   Recommendation: extend `IQuarkDiagnosticListener` unless it becomes noisy.

## 18. Acceptance criteria

- No permanent processing task is created per activation.
- Non-reentrant activations preserve single-turn execution.
- Per-activation FIFO remains true for normal work.
- Activation and deactivation lifecycle semantics remain documented and tested.
- Bounded mailbox wait/reject behavior remains compatible.
- Scheduler can cap concurrent activation drains on one silo.
- Scheduler can yield hot activations after a drain budget.
- Scheduler diagnostics expose enough data to explain wait time and throughput.
- The design does not require a custom .NET `TaskScheduler`.
- Existing scheduling/failure/lifecycle tests pass, with intentional updates documented.

