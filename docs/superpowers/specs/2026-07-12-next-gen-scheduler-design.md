# Next-Generation Silo Scheduler — Design

**Status:** Draft · **Date:** 2026-07-12 · **Scope:** Silo runtime only (no client/transport changes) · **Supersedes:** the `ActivationScheduler` / sharded-ready-queue / work-stealing line of specs (2026-07-03 → 2026-07-09). Clean-slate; existing implementation is not a constraint.

## 1. Goals & non-goals

**Goals**

- **High throughput, low tail latency** for the silo's grain-call dispatch loop.
- **Thread-safe by construction** — the per-actor single-threaded turn invariant holds with *zero per-grain locks*, purely through single-ownership of the schedulable unit.
- **NUMA-aware** execution: dedicated pinned worker threads grouped into arenas with node-local memory pools; affinity is **opt-in** and degrades cleanly to a single arena on cloud VMs.
- **Two-axis priority**: *actor priority* (which grain runs next, cross-actor) and *message priority* (which message within a grain runs next), FIFO within a lane, **non-preemptive** turns.
- **Static + dynamic priority**: attribute defaults, runtime overrides.
- **Cooperative cancellation** in two phases: *cancel-in-queue* (drop before dispatch) and *cancel-at-runtime* (cooperative token into the running turn).
- **Work stealing** for intra-node load balancing, with cross-arena stealing as an escape valve.
- **Bounded fairness** via a composite *worker budget* (message count + time quantum) and deficit round-robin across priority bands.

**Non-goals**

- Preemptive interruption of a running turn (rejected: breaks turn atomicity).
- Cross-silo/placement load balancing (that's placement's job — this schedules within one silo).
- Changing the engine model: schedulable unit stays the **activation**, behaviors stay per-call POCOs, mailbox stays MPSC-single-consumer.

## 2. Core model & invariants

The schedulable unit is the **`GrainActivation`**, *not* the message. This is the load-bearing decision:

- An activation is **"ready"** when its mailbox holds undispatched work.
- A ready activation is owned by **exactly one worker at a time** (a CAS'd `_scheduled` claim). While claimed, no other worker can touch it — this *is* the thread-safety guarantee. No lock is needed because ownership, not mutual exclusion, enforces single-threadedness.
- Work stealing moves *activations* between workers. Because a stolen activation carries its exclusive claim, stealing is safe with no extra synchronization on grain state.
- A worker, holding an activation, **drains** its mailbox (up to the worker budget) honoring message-priority lanes, then either yields (re-enqueue) or releases the claim.

Invariants (must hold at all times):

1. **I1 — Single turn:** for a non-reentrant activation, at most one turn executes at any instant, on one thread.
2. **I2 — Single claim:** an activation appears in at most one worker run-queue / injection queue at a time.
3. **I3 — Intra-lane FIFO:** messages in the same priority lane of the same activation execute in arrival order.
4. **I4 — No lost wake:** every empty→non-empty mailbox transition results in the activation being scheduled and a worker eventually servicing it (no message stranded).
5. **I5 — Progress:** the system makes forward progress even under reentrant call chains that exhaust the worker pool (stall watchdog + rescue capacity).

## 3. Topology & arenas (NUMA)

```
SchedulerTopology  (discovered once at silo start)
 ├─ Arena 0  (NUMA node 0)
 │   ├─ Worker 0  (thread, pinned core 0)   local run-queues + steal
 │   ├─ Worker 1  (thread, pinned core 1)
 │   ├─ ArenaPool          node-local object pool (mailbox nodes, work items)
 │   ├─ InjectionQueue     MPMC — external/rebalanced ready activations
 │   └─ IdleStack          parked-worker registry + per-worker semaphores
 ├─ Arena 1  (NUMA node 1)  …
 └─ neighbor map: arena → ordered list of steal targets (nearest node first)
```

- **Discovery.** On Linux, parse `/sys/devices/system/node/nodeN/cpulist`; on Windows, `GetLogicalProcessorInformationEx`. Guarded by `RuntimeInformation.IsOSPlatform`. If discovery fails or affinity is off → **one arena** spanning all logical cores.
- **Affinity (opt-in, default off).** `EnableAffinity` + `AffinityMode { CorePin, NodePin }`. Core-pin sets a single-CPU mask per worker; node-pin sets the node's CPU set. P/Invoke `sched_setaffinity` (Linux) / `SetThreadAffinityMask` (Windows). Off → workers float; arenas remain a logical grouping for pool/steal locality.
- **Arena assignment for a grain.** `arena = stableHash(GrainId) % arenaCount`. A grain's activation lives on one arena for its lifetime, so its `StateHolder<T>` bags, mailbox nodes, and work items all allocate from that arena's node-local pool → state stays on the node the worker runs on. Cross-arena movement happens only under load balancing (§9) and is the exception, not the rule.
- **ArenaPool.** Magazine-style per-arena free lists for `MailboxMessage` nodes and pooled work items. Cross-arena frees return to the freeing thread's local magazine (bounded drift, standard allocator trade-off). Eliminates steady-state allocation on the hot path.

## 4. Worker & run-queues (stealing)

Each worker is a **dedicated `Thread`** (never the ThreadPool) running a tight loop:

```
loop:
  a = PopLocal()                       // own deque, LIFO (hot, cache-warm)
      ?? StealIntraArena()             // siblings' deques, FIFO end
      ?? DrainInjectionQueue()         // arena rebalance/external
      ?? StealNeighborArena()          // nearest neighbor first (escape valve)
  if a == null: Park()                 // register idle, re-check, wait on semaphore
  else: DrainActivation(a)             // §5 + §8
```

- **Run-queue = Chase-Lev work-stealing deque**, one per *actor-priority band* (§6). Owner pushes/pops the bottom (LIFO → freshest activation, warmest cache). Thieves steal the top (FIFO → oldest, fairest to steal). Lock-free.
- **Band scan.** Pop scans bands high→low with **deficit round-robin** weights so Low is not starved (e.g. weights 4:2:1). Steal scans victim bands high→low.
- **Steal policy.** Steal a **small batch** (`StealBatch`, e.g. 1–4) to amortize the steal cost but avoid convoying. Intra-arena victims chosen by randomized start index (avoids all thieves hammering worker 0). Neighbor-arena stealing only after intra-arena comes up empty, and can be capped (`StealNeighborArenas`) or disabled.
- **Parking.** Per-arena idle stack + per-worker `SemaphoreSlim(0,…)`. On empty-after-double-check, worker pushes itself idle and waits. An enqueue that makes an activation ready wakes one idle worker *in that arena* (pop from idle stack, release its semaphore); if the arena has no idle worker, it may wake a neighbor arena's worker (cross-arena escape) so a saturated arena can shed. Double-check-before-park closes the lost-wake race (I4).

## 5. Mailbox & priority lanes

Replaces the single `Channel<IMailboxWorkItem>` with a lane-structured MPSC mailbox owned by the activation:

```
ActorMailbox
 ├─ lanes[L]        L MPSC queues, one per message-priority level (Urgent..Low)
 ├─ depth           total undispatched count (empty↔non-empty detection)
 ├─ scheduled       CAS claim: is this activation in a run-queue right now?
 └─ laneDeficit[L]  DRR counters so Urgent can't starve Low within an actor
```

- **Lane = Vyukov intrusive MPSC queue** of pooled `MailboxMessage` nodes. Many producers (callers) enqueue lock-free; one consumer (the owning worker) dequeues. Nodes come from the arena pool.
- **Enqueue (producer):**
  1. `node = pool.Rent(); node.priority = p; node.token = ct; node.promise = tcs;`
  2. `lanes[p].Enqueue(node); Interlocked.Increment(ref depth);`
  3. If `TryMarkScheduled()` wins the CAS (0→1) → push the activation onto a worker run-queue at its **effective band** (§6) and wake. If it loses, the activation is already scheduled; the message will be seen by the in-flight/next drain (I4).
- **Drain (owning worker):** for each budget step, pick the highest non-empty lane subject to DRR deficit, dequeue, run the turn.
- **`MailboxMessage` (pooled):** `{ workItem, priority, CancellationToken, TaskCompletionSource promise, volatile bool cancelled }`.

## 6. Priority model

Two independent axes, unified by an **effective band** for cross-actor scheduling.

| Axis | Granularity | Mechanism |
|---|---|---|
| **Actor priority** | across activations | which run-queue **band** the activation sits in → which grain a worker picks next |
| **Message priority** | within an activation | which **lane** of the mailbox drains first → which message of that grain runs next |

- **Bands** (actor): `High, Normal, Low` (3, keeps run-queue count small). **Lanes** (message): `Urgent, High, Normal, Low` (4).
- **Effective band = `max(actorBand, highestPendingLane)`** computed at the moment the scheduling CAS is won. So an Urgent message to a Low-priority actor schedules that activation in the High run-queue band — urgency crosses the actor boundary. Intra-actor ordering is still governed by lanes.
  - *Lazy re-prioritization contract:* if a higher-priority message arrives while the activation is **already** queued at a lower band, it is still drained first *within* that turn (lane order, correct semantics); the cross-actor band boost applies on the next scheduling cycle. Cheap, documented, no dequeue-from-middle of a lock-free deque.
- **Static defaults (compile-time):**
  - `[ActorPriority(Priority.High)]` on the behavior class → baked into the generated registration; sets the activation's `actorBand` at activation.
  - `[MessagePriority(Priority.Urgent)]` on an interface method → baked into the generated proxy/transport dispatcher; sets the default lane for that call.
- **Dynamic overrides (runtime):**
  - `ctx.SetActorPriority(Priority.Low)` — mutates `actorBand`; applies from the next scheduling cycle (lazy, as above).
  - `RequestContext.SetPriority(Priority.Urgent)` at the call site — carried in the invocation, overrides the method's default lane for that one message.
- **Anti-starvation:** DRR at both levels (across bands in the worker, across lanes in the mailbox). Optional aging (a Low item's effective lane is promoted after it has been skipped N times) can be layered on if starvation shows up in practice — off by default.

## 7. Cancellation

One `CancellationToken` per call, observed in two phases — this covers both keywords with one mechanism.

- **Cancel-in-queue** (before dispatch): the token's registration flips `node.cancelled = true`. The drain loop skips cancelled nodes cheaply (tombstone — no mid-queue removal) and completes `node.promise` with `OperationCanceledException`. A bounded skip cap prevents a cancel-flood from monopolizing a drain. This is the "yank m4 out of the normal lane" behavior.
- **Cancel-at-runtime** (during the turn): the *same* token is threaded into `ICallContext.CancellationToken`, visible to the running behavior. Cancellation is **cooperative** — the thread is never aborted; the grain observes the token and unwinds, the turn's Task completes (cancelled), the worker moves on. Turn atomicity is preserved; grain authors opt in by checking the token.
- **Source of cancellation:** the caller's token (flowed through the proxy/invoker), a silo-initiated control signal (e.g., shutdown, deactivation), or a per-call deadline (`Message-TTL` integration — expired messages self-cancel in-queue).

## 8. Worker budget & fairness

The **worker budget** is a composite quantum bounding how long one activation holds a worker before yielding:

- **Message budget** — max messages drained per turn (`DrainBudget`, e.g. 16).
- **Time quantum** — max wall-time per turn (`Quantum`, e.g. 200µs). Checked between messages (turns are non-preemptive, so this bounds *multi-message* drains, not a single slow message).
- **Yield rule:** stop when `messages ≥ DrainBudget` **or** `elapsed ≥ Quantum`. If lanes still hold work → re-mark scheduled, re-enqueue to the worker's run-queue (fairness yield). Else release the claim, then double-check lanes for a late enqueue (I4) and re-schedule if needed.

This is what keeps a hot grain from monopolizing a worker while other activations wait, and bounds the latency any one activation adds to the queue behind it.

## 9. Load balancing & stealing

Three tiers, cheapest first:

1. **Intra-arena stealing** (primary). Idle worker steals a batch from a busy sibling. Keeps the activation on its home node → state/pool stay warm.
2. **Cross-arena stealing** (escape valve). Only when intra-arena is dry. Nearest-neighbor node first. Bounded/disable-able. Moving an activation off its home node costs locality, so it's a last resort under genuine imbalance.
3. **Injection-queue rebalance.** New/external ready activations and shed work land on an arena's MPMC injection queue; workers drain it after their local deque but before neighbor stealing. A saturated arena that can't wake a local worker wakes a neighbor (§4 parking), letting pressure bleed across nodes.

Placement (`stableHash(GrainId) % arenaCount`) is the *default* balancer — it spreads grains across arenas up front so stealing is the exception. Under skew (a few hot grains), stealing + effective-band scheduling smooths it out without central coordination.

## 10. Overload & backpressure

- **Per-mailbox bound.** Lanes share a total `MailboxCapacity`. On full: `MailboxFullMode.Wait` (async backpressure to the caller's `PostAsync`) or `RejectWhenFull` (throw `MailboxFullException`). Preserved from today.
- **Arena admission.** Soft cap on total ready activations per arena; `SchedulerOverloadMode { Wait, RejectWhenFull, Shed }`. `Shed` pushes to the injection queue / neighbor arena instead of rejecting.
- **Metrics-driven.** Overload state is observable (§12) so operators can size budgets/capacity.

## 11. Stall / deadlock safety & reentrancy

- **Reentrancy.** `[Reentrant]` activations may interleave turns: the drain can dispatch a new message before the prior turn's Task completes (bounded by budget). Non-reentrant activations keep strict single-turn (I1) via the claim. The reentrancy mode is a per-activation property (as today).
- **Reentrancy deadlock** (the failure the previous line of specs fought): a grain call chain that re-enters the scheduler while every worker is blocked awaiting a downstream call can starve. **As of the async-resume worker (§17 PA, implemented 2026-07-12) this is structurally gone for V2** — a worker never blocks *inside* a turn, so an awaited nested call always frees its worker to make progress. The mitigations below now cover only the residual pathological edge (e.g. an unbounded fan-out of simultaneously-suspended turns) rather than the common nested-call case:
  - Dedicated threads + stealing already reduce the blast radius vs. a fixed ThreadPool.
  - **Per-arena stall watchdog:** if an arena shows no completed drain for `StallThreshold` while its ready queue is non-empty, spin a **transient rescue thread** that drains until the backlog clears, then retires. Same idea as today's overflow workers, scoped per arena. Emits a diagnostic.
  - Await-heavy grain code should prefer reentrancy or `[AlwaysInterleave]` for call-into-self patterns — documented guidance, enforced by the existing reentrancy analyzer.

## 12. Public API & DI seam

Keep `IActivationScheduler` as the seam; widen it:

```csharp
public interface IActivationScheduler : IAsyncDisposable
{
    ValueTask ScheduleAsync(GrainActivation activation, CancellationToken ct = default);
    void SetActorPriority(GrainActivation activation, Priority band);   // lazy, next cycle
    SchedulerSnapshot Snapshot();                                       // arenas, depths, steals
}
```

- `GrainActivation` swaps its `Channel<IMailboxWorkItem>` for `ActorMailbox`; `PostAsync` gains `(Priority, CancellationToken)`.
- Message priority + cancellation flow from the proxy → `LocalGrainCallInvoker` / `TcpGatewayCallInvoker` → `PostAsync`.
- `ICallContext` exposes `CancellationToken` and `SetActorPriority(...)`.
- Registration: `services.AddQuarkRuntime()` wires the new scheduler; `BehaviorRegistrationGenerator` emits `[ActorPriority]`/`[MessagePriority]` metadata into the generated registration (no runtime reflection).

## 13. Config surface (`SiloRuntimeOptions`)

| Option | Default | Meaning |
|---|---|---|
| `ArenaCount` | auto (NUMA nodes) | logical arenas; 1 when affinity off / discovery fails |
| `WorkersPerArena` | cores-in-node | dedicated threads per arena |
| `EnableAffinity` | `false` | opt-in thread pinning |
| `AffinityMode` | `CorePin` | `CorePin` \| `NodePin` |
| `DrainBudget` | 16 | max messages per turn |
| `Quantum` | 200µs | max time per turn |
| `StealBatch` | 2 | activations stolen per steal |
| `StealNeighborArenas` | 1 | 0 disables cross-arena steal |
| `ActorPriorityBands` | 3 | High/Normal/Low run-queues |
| `MessagePriorityLanes` | 4 | Urgent/High/Normal/Low |
| `BandWeights` | 4:2:1 | DRR anti-starvation |
| `MailboxCapacity` | 0 (unbounded) | per-activation lane total |
| `MailboxFullMode` | `Wait` | `Wait` \| `RejectWhenFull` |
| `OverloadMode` | `Wait` | `Wait` \| `RejectWhenFull` \| `Shed` |
| `StallThreshold` | 5s | rescue-thread trigger |

## 14. Diagnostics

Extend `IQuarkDiagnosticListener` (all no-op defaults) with `in`-ref-struct events:

- Arena/worker: `OnArenaConfigured`, `OnWorkerParked/Unparked`, `OnAffinityApplied`.
- Steal/balance: `OnStealAttempt/Succeeded`, `OnCrossArenaSteal`, `OnInjectionRebalance`.
- Mailbox/priority: `OnLaneDepthChanged`, `OnEffectiveBandBoosted`, `OnCancelInQueue`, `OnCancelAtRuntime`.
- Fairness: `OnDrainYielded` (budget/quantum), `OnBandStarvationAging`.
- Safety: `OnStallDetected`, `OnRescueWorkerSpawned/Retired`, `OnShutdownStalled`.

Plus `QuarkInstruments` counters/histograms: per-arena ready depth, steal rate, park rate, drain duration, lane depths, cancel counts, effective-band boosts.

## 15. AOT / trim

- Lock-free structures use only `Interlocked`/`Volatile` → AOT-clean.
- Affinity P/Invoke guarded by `RuntimeInformation.IsOSPlatform`; no dynamic code, no `DynamicMethod`.
- Priority metadata is source-generated (no runtime reflection); `[ActorPriority]`/`[MessagePriority]` read by the generator.
- Every new type stays in a `IsTrimmable=true` package; no `ISerializable`, no assembly scanning.

## 16. Concrete data structures

| Concern | Structure | Why |
|---|---|---|
| Worker run-queue | **Chase-Lev deque** per band | lock-free single-owner push/pop + steal |
| Mailbox lane | **Vyukov intrusive MPSC** | O(1) lock-free multi-producer enqueue, single-consumer |
| Arena injection | **MPMC bounded ring** (e.g. `BoundedMpmcQueue`) | many producers, many draining workers |
| Idle registry | lock-free **Treiber stack** of worker ids + per-worker `SemaphoreSlim` | O(1) wake, no lost wake with double-check |
| Object pooling | per-arena **magazine free lists** | node-local reuse, no hot-path alloc |
| Priority pick | **deficit round-robin** counters | starvation-free weighted band/lane selection |

## 17. Phased implementation plan

1. **P1 — Skeleton & seam. ✅ implemented (2026-07-12).** New `ArenaScheduler` + `WorkStealingDeque` behind `IActivationScheduler`; single arena, no affinity, single lane/band. Selected via `SiloRuntimeOptions.SchedulerKind = ArenaV2` (default stays `Legacy`); `--v2` flag on the PingPong benchmark. Dedicated worker threads, per-worker CLR-style work-stealing deques, per-worker hashed injection shards, Treiber idle-stack parking with the proven push-then-double-check lost-wake ordering, and a **blocking drain** (allocation-free / no-ThreadPool-hop for synchronously-completing turns). Invariants I1 (no turn overlap) and I4 (no lost work) proven by `ArenaSchedulerConcurrencyStressTests` (64 activations/2 workers overlap + no-loss; 300× single-worker lost-wake and idle-park cycles).
   - **Measured (32-core box, PingPong):** V2 reaches ~95% of the legacy scheduler when worker count is not oversubscribed by the in-process CPU-bound driver (pairs=32, workers=16: 365k vs 386k calls/s). At default workers=ProcessorCount it is ~84% (325k vs 386k) because 32 dedicated worker threads contend with the 32 CPU-bound driver threads for 32 cores — an in-process-benchmark artifact (a real silo's load arrives over the network, not from co-located CPU-spinning threads), not a dispatch-loop cost. The ~13% residual at backlog (pairs=256) is the same dedicated-thread-vs-ThreadPool oversubscription. Closing it fully needs the cooperative async-resume `SynchronizationContext` (frees the worker thread across awaits instead of blocking) — deferred as noted below.
2. **P2 — Stealing & arenas. ◑ mechanism validated (2026-07-12); locality benefit blocked on P5.** Work-stealing deques, per-worker hashed injection shards, and from-worker local-deque routing are implemented and proven correct by `ArenaSchedulerStealingTests`: a single source drain fires 2000 fire-and-forget posts onto one worker's local deque; all 2000 complete exactly once, all take the from-worker route (`local==2000`, `external==1`), `steals>0`, and work spreads across multiple worker threads. Multi-arena / affinity still pending (P5).
   - **AstroSim finding (the requested validation target).** AstroSim **cannot** validate stealing/locality: its `ChunkGrain` is `[Reentrant]`, so every call — the driver's `TickAsync` *and* all ≤26 nested `neighbor.GetAggregateAsync()` calls per tick — runs inline via `ReentrantSchedulingMode.Immediate` and never enters `ScheduleAsync`. Instrumentation confirms it directly: over a full run the arena scheduler recorded `external=0 local=0 steals=0` — the workers stay parked the entire time. Measured throughput (V2 ~99k vs legacy ~58–89k msg/s at grid=8) is dominated by JIT/GC/ThreadPool-contention noise (a 50%+ swing between identical legacy runs), **not** scheduling; any V2 edge is incidental (its dedicated workers park and cede the ThreadPool to the driver), not a scheduling win. Net: a reentrant-heavy workload is the wrong shape to exercise or benefit from the scheduler at all.
   - **Design consequence — from-worker locality needs cooperative async resume.** Under a **blocking** drain, a non-reentrant grain that *awaits* a nested call blocks its worker; local-deque placement of the callee then *guarantees* a cross-core steal (the opposite of locality), and nesting deeper than the worker count deadlocks without rescue. From-worker locality therefore pays off only for fire-and-forget fan-out (validated above) or once an awaiting worker can run its own local work instead of blocking. This motivated bringing the async-resume worker forward — see **PA** below.

**PA — Async resume (spill-to-ThreadPool on await). ✅ implemented (2026-07-12), brought forward from P5.** Instead of a per-worker `SynchronizationContext` event loop (which the runtime's pervasive `ConfigureAwait(false)` would defeat), the drain is split: a turn that completes **synchronously** finishes inline on the dedicated worker (no async frame, no ThreadPool hop — the CPU-bound fast path), and the instant a turn **awaits**, the drain's remainder runs as a ThreadPool continuation while the worker returns to its loop, free to run the very nested call the suspended turn is waiting on. The `_running`/`_scheduled` claims span the whole async drain, so single-threaded-per-activation still holds across the await. This structurally removes both the blocking-drain latency and the bounded-worker reentrancy deadlock (issue #167). Validated by `ArenaSchedulerAsyncResumeTests`: a **depth-8 non-reentrant call chain completes on a single worker** (blocking would deadlock at the first nested await) and on 2 workers (blocking would deadlock at depth 2). PingPong sync-path throughput is unregressed (V2 at 16 workers ≈ 395–456k calls/s, ahead of legacy's ≈ 346–394k on the same box).

   - **In-flight bound (backpressure). ✅ implemented (2026-07-12).** Each worker holds at most `SchedulerMaxInFlightDrainsPerWorker` suspended (spilled) drains before it stops taking new work and parks until one of its own completes, bounding memory and open per-call scopes under a flood of slow-awaiting turns. The counter is touched only on the spill path, so the synchronous fast path pays a single `Volatile.Read` (always 0 when nothing spills) — PingPong is unregressed. The gate uses a Dekker-fenced `_capWaiting` flag with a single-winner CAS release, so no wake is lost and permit inflation is bounded. **The cap is a safety ceiling, not a throttle:** it defaults to 256/worker and *must* exceed the deepest legitimate non-reentrant nesting, because async-resume deadlock-freedom relies on a worker being able to take on the nested call it awaits — a cap below the nesting depth would reintroduce the bounded-worker deadlock. Validated by `ArenaSchedulerInFlightBoundTests`: with cap 4 and 2 workers, 40 simultaneously-suspending turns peak at exactly 8 concurrent suspensions (never exceeded) and all 40 still complete once released. Residual: a stall/rescue watchdog for the pathological all-workers-capped-and-stalled edge remains for P6.
3. **P3 — Priority.** Lanes + bands, effective-band boost, DRR anti-starvation, static attributes via generator, dynamic setters. Semantics tests for I3 + priority ordering.
   - **P3a — message-priority lanes. ✅ implemented (2026-07-12).** `MessagePriority { Low, Normal, High, Urgent }` (Quark.Core.Abstractions.Scheduling). `GrainActivation`'s single mailbox `Channel` became a 4-lane array (one `Channel` per priority, reusing the existing bounded/unbounded/Reject machinery — a bounded mailbox now bounds each lane independently); the drain reads the highest-priority non-empty lane first, FIFO within a lane. Because the lanes live in the shared mailbox, **both schedulers get message priority for free**. Exposed via a new `PostAsync(work, MessagePriority)` overload (the arity-1 overload is preserved for the timer's method-group use; existing callers post `Normal`). Validated by `MessagePriorityLaneTests` (drain order `urgent → high → normal-1 → normal-2 → low` with FIFO within the Normal lane). No hot-path regression: full unit suite 546/546 green, PingPong unchanged (the high→low lane scan on all-Normal traffic is lost in the noise — empty `Channel.TryRead` is cheap). *Strict* priority for now; DRR anti-starvation is a documented refinement (Low waits behind sustained High).
   - **P3 remaining slices (not yet done):** actor-priority **bands** in the arena run-queues (per-worker × per-band deques/inject) + **effective band = max(actorBand, highest-pending-lane)**; `[ActorPriority]`/`[MessagePriority]` attributes + generator emission and `RequestContext.SetPriority()` / `ctx.SetActorPriority()` **call-site plumbing** (so far priority is set only via the `PostAsync` overload); and **cancel-in-queue** (needs the P4 cancellation token). `ActorPriority { Low, Normal, High }` is already defined, ready for the bands work.
4. **P4 — Cancellation.** In-queue tombstone + at-runtime cooperative token, wired through invoker + `ICallContext`; integrate Message-TTL deadlines.
5. **P5 — NUMA.** Topology discovery, opt-in affinity P/Invoke, node-local arena pools, cross-arena steal + rebalance. Validate on a real multi-socket box; confirm graceful single-arena fallback on cloud VMs.
6. **P6 — Overload & safety.** Backpressure modes, arena admission/shed, stall watchdog + rescue threads, full diagnostics surface.

Each phase is independently shippable behind the seam and benchmarked before the next.

## 18. Open questions / risks

- **Band re-prioritization latency** — lazy (next-cycle) application is cheap; if a use case needs *immediate* cross-actor re-banding, we'd need dequeue-from-middle (expensive) or a redirect marker. Defer until proven necessary.
- **Cross-arena pool drift** — magazine free lists can drift objects off their home node under sustained cross-arena traffic. Bounded, but worth measuring; cap magazine size.
- **Affinity vs. the .NET ThreadPool** — dedicated pinned threads coexist with the pool used elsewhere in the runtime (I/O, timers). On small boxes over-subscription is possible; `WorkersPerArena` and affinity are opt-in to manage this.
- **Reentrancy deadlock residual** — rescue threads are a safety net, not a cure; the real fix stays "don't block a turn on a synchronous call into the same activation," enforced by the reentrancy analyzer.
- **Quantum granularity** — 200µs default is a guess; must be tuned against real grain-turn cost distributions from the benchmarks.
```
