# Design: Event Sourcing V2 (JournaledGrain snapshots, live event streams, retention)
**Issues:** #96, #119, #121
**Date:** 2026-07-02
**Status:** Draft — ready for implementation (phased)

---

## Goals / Non-goals

### Goals
- **Phase 1 (#96):** Periodic state snapshots for `JournaledGrain<TState,TEvent>` so activation replay is bounded (snapshot + tail) rather than "replay the entire log on every activation." Configurable cadence, disabled by default (preserve current behaviour).
- **Phase 2 (#119):** Opt-in live publication of confirmed events to an `IAsyncStream<TEvent>` with a well-known `StreamId` derived from grain identity, in append order. In-memory streams suffice for this phase.
- **Phase 3 (#121):** An additive `ILogStoragePurge` interface plus a retention policy so event logs do not grow unbounded. Purge only below the latest durable snapshot (retention **requires** snapshots — hard dependency on Phase 1).
- Address event **schema evolution** (type-level `[Alias]` + an upcast hook shape) at least at the guidance level.
- Everything AOT/trim-first: source-generation-friendly, no reflection, no `ISerializable`.

### Non-goals
- Durable / at-least-once stream delivery. Live publication (Phase 2) is **at-most-once**; the event log remains the single source of truth. Durable replay off a persistent stream is deferred to the parallel persistent-streams work (#41, spec `2026-07-02-persistent-streams-design.md`, not yet written). Phase 2 must **not** take a dependency on it.
- Cross-silo snapshot/log coordination beyond what `ILogStorage` optimistic concurrency already provides.
- A generalized runtime upcasting pipeline. Phase 1 ships the **shape** and the authoring rules; a full `IEventUpcaster<TEvent>` implementation is explicitly deferred (see Resolved decision R7).
- Extending `IGrainContext` in any way. It is stale/unwired (#78). Per-call identity comes from `ICallContext.GrainId` only.
- A Redis `ILogStorage` provider. It does not exist today (only `RedisGrainStorage`, an `IGrainStorage`). Phase 3's `ILogStoragePurge` is defined so that a future Redis log provider implements it; nothing here creates one.

---

## Current state (verified against live source)

`src/Quark.Persistence.Abstractions/Journaling/`:

- **`JournaledGrain<TState,TEvent>`** — abstract `IGrainBehavior` + `IActivationLifecycle`. Constructor:
  `(IActivationMemory<JournaledGrainState<TState,TEvent>> memory, ICallContext ctx, ILogStorage? logStorage = null)`.
  - Cross-call state lives in `_memory.Value` (shell-owned `JournaledGrainState`), surviving across calls, lost on deactivation.
  - `RaiseEvent(e)` → stages into `StagedEvents` **and** applies `TransitionState(State, e)` immediately (optimistic in-memory projection).
  - `ConfirmEventsAsync()` → builds `LogEntry(ConfirmedVersion + i, e)` list, calls `_logStorage.AppendEntriesAsync(GrainId, ConfirmedVersion, entries)`, then advances `ConfirmedVersion` and clears staged.
  - `OnActivateAsync` → `ReloadFromLogAsync`: **reads the entire log `[0, int.MaxValue)`**, resets `State = new()`, replays every entry via `TransitionState`, sets `ConfirmedVersion = lastVersion + 1`. This is the O(log length) cost #96 targets.
  - `RetrieveConfirmedEvents(from, to)` → range read from log.
- **`JournaledGrainState<TState,TEvent>`** — `{ TState State; List<TEvent> StagedEvents; int ConfirmedVersion }`. `int` version throughout.
- **`LogEntry`** — `{ int Version; object Event }`. Event boxed as `object`; serialization handled by the codec provider on the wire path.
- **`ILogStorage`** — exactly two methods: `ReadEntriesAsync(grainId, fromVersion, toVersion, ct)` and `AppendEntriesAsync(grainId, expectedVersion, entries, ct)`. No delete/purge, no snapshot slot, no enumeration.
- **`InMemoryLogStorage`** (`Quark.Persistence.InMemory`) — `ConcurrentDictionary<GrainId, List<LogEntry>>`. Registered manually: `AddSingleton<ILogStorage, InMemoryLogStorage>()` (not keyed/named). **Optimistic-concurrency check is `log.Count != expectedVersion`** — i.e. physical entry count is assumed equal to logical next-version. **This invariant breaks under head purge (Phase 3) — see Risk R-CONC.**
- No Redis `ILogStorage`. `Quark.Persistence.Redis` ships `RedisGrainStorage` (`IGrainStorage`) only.

`IGrainStorage` (`Read/Write/ClearStateAsync<TState>(stateName, grainId, GrainState<TState>, ct)`, `GrainState<T> { State; ETag; RecordExists }`) is the mature, multi-provider (InMemory, Redis, AdoNet incoming) durable state abstraction. **This is where snapshots should live** (see R1).

Streaming: a behavior obtains a provider by injecting `[FromKeyedServices("providerName")] IStreamProvider` (see `samples/Streaming/.../ProducerBehavior.cs`), then `provider.GetStream<T>(StreamId.Create(ns, key))` → `stream.OnNextAsync(item)`. `InMemoryStreamProvider` registered via `AddMemoryStreams(name)` as `AddKeyedSingleton<IStreamProvider>(name, ...)`. `StreamId` is `{ string Namespace; string Key }`.

Bank sample `LedgerBehavior : JournaledGrain<LedgerState, LedgerEvent>` is the canonical usage: `RaiseEvent(new Credited(...))` then `await ConfirmEventsAsync()`. It is the natural place to demo all three phases.

Background-sweep precedent: `GrainIdleCollector : BackgroundService` gated on `SiloRuntimeOptions.GrainCollectionAge == TimeSpan.Zero` (disabled by default) with a `PeriodicTimer(GrainCollectionInterval)`. Phase 3's optional sweep mirrors this shape.

---

## Phase 1 — Snapshots (#96)

**Compatibility tier:** Quark-native. `JournaledGrain` has no drop-in Orleans signature to preserve; Orleans stores log-consistency snapshots separately too, so the model is familiar but the API is ours.

### Where snapshots are stored — decision: `IGrainStorage` named slot (not an `ILogStorage` extension)

Rationale:
1. `IGrainStorage` already has `Read/Write/Clear` + `ETag` + multiple providers (InMemory, Redis, AdoNet incoming). Snapshots inherit durability across all of them for free. `ILogStorage` has exactly one provider (in-memory) and would have to grow `TState` serialization in every future implementation.
2. Clean separation: the **log** is append-only events; the **snapshot** is a mutable, overwrite-in-place materialized projection. Overwriting a record is exactly `WriteStateAsync`. Folding snapshots into `ILogStorage` conflates two different lifecycles.
3. A future Redis/AdoNet log provider stays a pure event store; snapshots ride the existing grain-storage provider the app already configured.

### Proposed API (Quark.Persistence.Abstractions)

```csharp
// New serializable snapshot envelope. [GenerateSerializer] so Redis/AdoNet providers can persist it;
// InMemory grain storage never serializes.
[GenerateSerializer]
public sealed class JournaledSnapshot<TState> where TState : new()
{
    [Id(0)] public TState State { get; set; } = new();
    [Id(1)] public int Version { get; set; }   // ConfirmedVersion at the moment the snapshot was taken
}

// Configuration for a JournaledGrain's snapshot / stream / retention behavior.
// Passed by the subclass (see wiring below); all-zero/all-null = today's behavior.
public sealed class JournalOptions
{
    /// <summary>Write a snapshot every N confirmed events. 0 (default) = snapshotting disabled.</summary>
    public int SnapshotInterval { get; set; }

    /// <summary>Grain-storage state name used for the snapshot slot. Default derived per-grain-type.</summary>
    public string? SnapshotStateName { get; set; }

    // Phase 3 fields live here too (see Phase 3): RetentionEnabled, RetainVersionsBeforeSnapshot.
}
```

`JournaledGrain` constructor gains **two optional, defaulted** parameters (fully backward compatible — existing `LedgerBehavior` compiles and behaves unchanged):

```csharp
protected JournaledGrain(
    IActivationMemory<JournaledGrainState<TState, TEvent>> memory,
    ICallContext ctx,
    ILogStorage? logStorage = null,
    IGrainStorage? snapshotStore = null,   // NEW — null disables snapshotting
    JournalOptions? options = null);       // NEW — null => new JournalOptions() (all disabled)
```

### Behaviour changes

- **Activation replay (`ReloadFromLogAsync`):**
  1. If `snapshotStore != null`: `ReadStateAsync<JournaledSnapshot<TState>>(SnapshotStateName, GrainId, ...)`. If `RecordExists`, seed `State = snapshot.State`, `ConfirmedVersion = snapshot.Version`.
  2. Replay the **tail only**: `ReadEntriesAsync(GrainId, ConfirmedVersion, int.MaxValue)`, apply each via `TransitionState`, advance `ConfirmedVersion`.
  3. No snapshot / no store → identical to today (`from = 0`).
- **Snapshot write (in `ConfirmEventsAsync`, after a successful append):** if `SnapshotInterval > 0` and the newly advanced `ConfirmedVersion` crosses a multiple of `SnapshotInterval` (i.e. `prev/N != new/N`), write `JournaledSnapshot { State = current projection, Version = ConfirmedVersion }` via `WriteStateAsync`. Snapshot write is best-effort relative to the log: **the append is the durability point**; a failed snapshot write is logged/diagnostic and retried at the next interval — it must never fail the confirmed call (the events are already durable and will simply be replayed from the log until a snapshot succeeds).
- **`SnapshotStateName` default:** `$"{GrainId.Type.Value}-journal"` (or `options.SnapshotStateName` when set). Stable and grain-type-scoped.

### Cadence — decision: event-count interval, default disabled

Event-count (`SnapshotInterval = N events`) is deterministic and unit-testable, and directly bounds replay cost to `< N` events. Time-based cadence is deferred (a `SnapshotAge` field can be added later without breaking the shape). Default `0` preserves current full-replay behaviour, so this phase is opt-in and non-breaking.

### Impact
- **Quark.Persistence.Abstractions:** add `JournaledSnapshot<TState>`, `JournalOptions`; modify `JournaledGrain` (ctor + reload + confirm). Interfaces/value types only — allowed in an `*.Abstractions` package (JournaledGrain already lives here).
- **samples/Persistence (Bank):** `LedgerBehavior` gains optional snapshot wiring in its constructor to demo Phase 1.
- No runtime, client, transport, or code-generator changes required for Phase 1.

---

## Phase 2 — Live event streams (#119)

**Compatibility tier:** Quark-native (no Orleans equivalent — Orleans has no built-in JournaledGrain→stream bridge).

### Opt-in mechanism — decision: subclass passes a keyed `IStreamProvider`; base publishes in-turn

Reject reflection/attribute-magic base resolution (base class cannot know the keyed provider name at compile time without reflection). Instead follow the existing streaming DI convention: the **subclass** injects `[FromKeyedServices("name")] IStreamProvider` and forwards it to the base. `null` = publication disabled. This keeps registration explicit, AOT-safe, and consistent with `ProducerBehavior`.

```csharp
protected JournaledGrain(
    ...,
    IStreamProvider? eventStream = null);   // NEW (Phase 2) — null disables live publication
```

### Well-known StreamId — derived from grain identity

```csharp
/// <summary>StreamId that confirmed events are published to. Override to customize.</summary>
protected virtual StreamId EventStreamId =>
    StreamId.Create($"quark.journal:{GrainId.Type.Value}", GrainId.Key.ToString());
```

Consumers (read-model projectors, audit sinks) subscribe to this well-known id without a direct reference to the source grain — exactly the decoupling #119 asks for.

### Ordering — append order, guaranteed by the mailbox (verified)

Behavior methods run serialized through the activation's `Channel<Func<Task>>` mailbox; a single `ConfirmEventsAsync` call runs to completion within one turn, and concurrent calls to the same grain never interleave. Therefore, if publication happens **inside `ConfirmEventsAsync`, after `AppendEntriesAsync` succeeds, awaited in the loop order of the confirmed entries**, stream consumers observe events in exactly append (version) order. No extra sequencing machinery needed. We pass the log `Version` as the `StreamSequenceToken` so consumers can dedupe/resume.

```csharp
// inside ConfirmEventsAsync, after append succeeds and before returning:
if (_eventStream is not null)
{
    IAsyncStream<TEvent> s = _eventStream.GetStream<TEvent>(EventStreamId);
    foreach (var (version, evt) in confirmedThisCall)   // ascending version
        await s.OnNextAsync(evt, new SequentialToken(version));
}
```

### Delivery semantics & backpressure — decision: at-most-once, in-turn await

- Publication is awaited **inside the grain turn**. A slow subscriber therefore applies backpressure to the grain (head-of-line). This is the correct, simple default for ordering; a decoupled/buffered path is the persistent-streams work (#41), intentionally out of scope.
- **The append is the commit point.** If publication throws *after* a successful append, the events are already durable — we must **not** roll back `ConfirmedVersion`. Publish failure is surfaced via diagnostics and swallowed (best-effort). Consequence: live delivery is **at-most-once**; the log is the source of truth. A late/new subscriber catches up by replaying the log (query side), not by expecting the live stream to have buffered history.
- Item codec: for the TCP/persistent path an event type crossing a boundary needs `AddStreamableCodec<TEvent,...>()`. For in-memory streams (this phase) no codec is required. Document that enabling remote/persistent delivery later requires the codec.

### Impact
- **Quark.Persistence.Abstractions:** `JournaledGrain` gains the `IStreamProvider? eventStream` ctor param, `EventStreamId` virtual, and publish loop. Adds a *type-only* dependency on `Quark.Streaming.Abstractions` (`IAsyncStream`, `IStreamProvider`, `StreamId`, `SequentialToken`). **Verify this project reference does not create a cycle** — Streaming.Abstractions must not reference Persistence.Abstractions (it does not today). If a reference is undesirable, alternative: extract publication into a thin `Quark.Persistence.Journaling.Streaming` bridge behavior mix-in. **Open for confirmation (see R-DEP).**
- **samples/Persistence (Bank):** `LedgerBehavior` opts in; add a small projector/observer demonstrating a subscription to the ledger event stream.
- No runtime/transport changes for the in-memory phase.

---

## Phase 3 — Retention / purge (#121)

**Compatibility tier:** drop-in-preserving + Quark-native. `ILogStorage` is untouched (existing providers keep working); purge is a **separate additive interface** providers may optionally implement.

### Additive interface (Quark.Persistence.Abstractions)

```csharp
/// <summary>Optional capability: providers that can delete confirmed log entries below a version.</summary>
public interface ILogStoragePurge
{
    /// <summary>
    /// Permanently deletes all entries with Version &lt; <paramref name="beforeVersion"/> for the grain.
    /// Callers guarantee beforeVersion &lt;= latest durable snapshot version (retention invariant).
    /// Idempotent; deleting already-absent entries is a no-op.
    /// </summary>
    Task PurgeEntriesBeforeAsync(GrainId grainId, int beforeVersion, CancellationToken ct = default);
}
```

### Policy config (extends `JournalOptions` from Phase 1)

```csharp
// added to JournalOptions:
public bool RetentionEnabled { get; set; }            // default false
public int  RetainVersionsBeforeSnapshot { get; set; } // keep this many events below the snapshot; default e.g. 0
```

### Retention **requires** snapshots — explicit dependency

You may only delete events that have been folded into a **durable** snapshot, because activation reload seeds from the snapshot and replays the tail. Deleting below snapshot version `V` is safe iff a snapshot at `>= V` is durably written. Therefore `RetentionEnabled` is a no-op unless `SnapshotInterval > 0` and a `snapshotStore` is present. Validate at wiring time (or throw on first confirm if misconfigured).

### Mechanism — decision: inline purge-on-snapshot (v1); background sweep deferred

Reject a stand-alone sweep service for v1: `ILogStorage` exposes **no grain enumeration**, so a `GrainIdleCollector`-style sweep cannot discover which grains have logs without a new enumeration surface. Instead, purge **inline, immediately after a successful snapshot write**, from inside `ConfirmEventsAsync`:

1. Append events (durability point).
2. If interval crossed → write snapshot at version `V` (must succeed and be durable before step 3).
3. If `RetentionEnabled` and `snapshotStore` provider or `logStorage` implements `ILogStoragePurge`: `await purge.PurgeEntriesBeforeAsync(GrainId, V - RetainVersionsBeforeSnapshot)`.

This ties purge to the exact safety condition (a fresh durable snapshot) with **zero new enumeration surface**, and naturally satisfies "purge only below the latest snapshot." A background sweep for **deactivated / orphaned** grains (whose logs never get re-snapshotted because they're idle) is a documented future extension — it would need either a log-enumeration API on `ILogStorage` or a snapshot-index, and mirrors the `GrainIdleCollector` shape (`JournalRetentionCollector : BackgroundService`, gated on options, `PeriodicTimer`). Deferred (R6).

### Interaction with the optimistic-concurrency invariant — must fix (Risk R-CONC)

`InMemoryLogStorage.AppendEntriesAsync` validates `log.Count != expectedVersion`, i.e. it assumes **physical entry count == logical next version**. Head purge breaks this. The provider must be reworked to track a logical base offset (e.g. `PurgedBelowVersion`) and validate `PurgedBelowVersion + log.Count != expectedVersion`, and range-read must offset accordingly. Any real (Redis/AdoNet) log provider implementing `ILogStoragePurge` must persist this base version alongside the entries. **This is mandatory for Phase 3 correctness** and must be covered by a regression test (append-after-purge).

### Interaction with live subscribers reading the tail (#121 concern)

- **Live push subscribers (Phase 2)** consume events as appended — they read the **tail**, never re-read the head. Purging the head (old, already-snapshotted, already-delivered events) does not affect them.
- **Catch-up / late readers** calling `RetrieveConfirmedEvents(from: 0, ...)` after a purge would silently get a **gap** (reads below the purge horizon just return fewer rows). Mitigation: catch-up must **floor reads at the snapshot version**, not 0. Expose the floor:

```csharp
/// <summary>Lowest version still available in the log (entries below this were purged; use the snapshot).</summary>
protected int OldestRetainedVersion { get; }   // = snapshot.Version - RetainVersionsBeforeSnapshot, else 0
```

Document: to reconstruct full history, start from the snapshot state at `Version`, then apply events `>= OldestRetainedVersion`. The retention invariant guarantees the snapshot covers everything below the purge horizon, so there is no true information loss — only that raw pre-snapshot events are gone by design.

### Impact
- **Quark.Persistence.Abstractions:** add `ILogStoragePurge`; extend `JournalOptions`; `JournaledGrain` purge call + `OldestRetainedVersion`.
- **Quark.Persistence.InMemory:** `InMemoryLogStorage` implements `ILogStoragePurge` **and** is reworked for the base-offset invariant (R-CONC).
- **samples/Persistence (Bank):** enable retention on `LedgerBehavior` to demo bounded logs.

---

## Event schema versioning / upcasting (brief, spans phases)

- **Wire-level** unknown-field skip already exists (closed #104) — adding `[Id(n)]` members to an existing event type is forward/backward tolerant on the wire.
- **Type-level evolution — required guidance now:** tag every event type with `[Alias("stable-name")]` so type identity survives class renames/namespace moves. Never renumber or reuse `[Id]`. Never mutate the meaning of an existing event; **add** new event types and handle them in `TransitionState`'s switch (old + new arms coexist). This is the immediate, zero-runtime-cost answer to #96's upcasting bullet.
- **Runtime upcast hook — decision R7: reserve the shape, defer implementation.** A future hook, invoked during replay/read before `TransitionState`:

  ```csharp
  // Deferred; not implemented in this spec.
  protected virtual TEvent UpcastEvent(TEvent stored) => stored;
  ```

  Or, provider-agnostic: an optional `IEventUpcaster<TEvent>` resolved from DI. Deferred because (a) `[Alias]` + additive `TransitionState` arms cover the common cases, (b) a general polymorphic upcast pipeline risks reflection/AOT complexity that needs its own design pass. Reserving the virtual keeps the door open without committing surface now.

---

## AOT / trim notes

- No reflection, no `Assembly.Load`, no `ISerializable` (would trip QRK0003). All new types are POCOs.
- `JournaledSnapshot<TState>` carries `[GenerateSerializer]`/`[Id]` so the `SerializerGenerator` emits its codec/copier for Redis/AdoNet snapshot providers; InMemory grain storage never serializes it. The app must ensure `TState` is itself `[GenerateSerializer]` when using a serializing snapshot provider (same rule as `[PersistentState]`/`JournaledGrain` events today).
- New public types land in `*.Abstractions` (`ILogStoragePurge`, `JournaledSnapshot<T>`, `JournalOptions`) — interfaces/value types only, honouring the package boundary rule.
- Phase 2's cross-package type reference (Persistence.Abstractions → Streaming.Abstractions) is compile-time only; confirm no project-reference cycle (R-DEP). No dynamic-code path is introduced; `IStreamProvider.GetStream<TEvent>` is a generic virtual call, AOT-safe.
- All new ctor params are optional/defaulted → no source-generator changes needed for `BehaviorRegistrationGenerator` (it already wires `IActivationMemory`, `ICallContext`, and DI-resolved services by constructor); `IGrainStorage`, `IStreamProvider` (keyed), `JournalOptions` resolve through normal DI/keyed DI.

---

## Test plan

**Phase 1 (Snapshots)** — `tests/Quark.Tests.Unit/Journaling/`:
- Snapshot written exactly when `ConfirmedVersion` crosses `SnapshotInterval`; not written when interval `0`.
- Activation with an existing snapshot replays only the tail (spy `ILogStorage` asserts `ReadEntriesAsync(from = snapshot.Version, ...)`, not `from = 0`).
- Snapshot-write failure does not fail `ConfirmEventsAsync`; events remain durable; next interval re-attempts.
- Round-trip: snapshot + tail reconstruct the exact same `State` as full replay (property-style over random event sequences).

**Phase 2 (Live streams)** — unit + `Quark.Tests.Integration`:
- Confirmed events appear on `EventStreamId` in ascending version order, with `SequentialToken(version)`.
- Concurrent calls to the same grain never interleave stream items (mailbox ordering).
- No publication when `eventStream` is null (opt-out).
- Publish-throws-after-append: `ConfirmedVersion` still advances, events durable, diagnostic emitted (at-most-once semantics).
- End-to-end Bank demo: a projector subscribed to the ledger stream builds a running balance matching `GetBalanceAsync`.

**Phase 3 (Retention)** — unit + fault tests:
- `ILogStoragePurge.PurgeEntriesBeforeAsync` removes only entries `< beforeVersion`.
- **R-CONC regression:** append succeeds after a head purge (logical version continuity; `expectedVersion` validated against base-offset, not physical count).
- Retention is a no-op when snapshotting disabled (dependency enforced).
- Late reader after purge: reads floored at `OldestRetainedVersion`; snapshot + retained tail reconstruct full state; no silent gap in the projection.
- Live subscriber unaffected by concurrent head purge.

**AOT:** extend the Native AOT smoke publish to exercise a snapshotting + streaming `JournaledGrain` (no trim/AOT warnings).

---

## Implementation checklist

### Phase 1
1. `src/Quark.Persistence.Abstractions/Journaling/JournaledSnapshot.cs` — new `[GenerateSerializer]` envelope.
2. `src/Quark.Persistence.Abstractions/Journaling/JournalOptions.cs` — new (SnapshotInterval, SnapshotStateName).
3. `src/Quark.Persistence.Abstractions/Journaling/JournaledGrain.cs` — add `snapshotStore`/`options` ctor params; snapshot-aware `ReloadFromLogAsync`; snapshot write in `ConfirmEventsAsync`; `SnapshotStateName` default.
4. `tests/Quark.Tests.Unit/Journaling/` — snapshot cadence + tail-replay + failure tests.
5. `samples/Persistence/Bank.Grains/LedgerBehavior.cs` + `Bank.Server/Program.cs` — wire an `IGrainStorage` snapshot store; wiki `Persistence.md` update.

### Phase 2
6. Confirm/establish `Quark.Persistence.Abstractions` → `Quark.Streaming.Abstractions` project reference (no cycle) **or** decide on a bridge mix-in (R-DEP).
7. `JournaledGrain.cs` — add `eventStream` ctor param, `EventStreamId` virtual, publish loop in `ConfirmEventsAsync`.
8. Tests: ordering, opt-out, publish-failure semantics.
9. Bank sample: opt-in publication + a projector observer; wiki `Streaming.md`/`Persistence.md` cross-link.

### Phase 3
10. `src/Quark.Persistence.Abstractions/Journaling/ILogStoragePurge.cs` — new.
11. `JournalOptions.cs` — add `RetentionEnabled`, `RetainVersionsBeforeSnapshot`.
12. `JournaledGrain.cs` — inline purge-on-snapshot; `OldestRetainedVersion`; misconfiguration guard.
13. `src/Quark.Persistence.InMemory/InMemoryLogStorage.cs` — implement `ILogStoragePurge`; **rework the `expectedVersion` invariant to a logical base offset (R-CONC)**.
14. Tests: purge correctness, append-after-purge regression, retention-requires-snapshot, late-reader floor.
15. Bank sample: enable retention; wiki update.

### Deferred (documented, not built here)
- Time-based snapshot cadence (`SnapshotAge`).
- `JournalRetentionCollector` background sweep for idle/orphaned grains (+ log enumeration surface).
- `IEventUpcaster<TEvent>` / `UpcastEvent` runtime pipeline.
- Redis/AdoNet `ILogStorage` + `ILogStoragePurge` providers; durable/persistent event stream (#41).

---

## Resolved design decisions

- **R1 — Snapshot storage:** `IGrainStorage` named slot, not an `ILogStorage` extension. (Multi-provider durability, clean event/state separation.)
- **R2 — Snapshot cadence:** event-count `SnapshotInterval`, default `0` (disabled → today's behaviour, non-breaking). Time-based deferred.
- **R3 — Snapshot vs durability:** the append is the commit point; snapshot-write failure is non-fatal, retried next interval.
- **R4 — Live publication opt-in:** subclass forwards a keyed `IStreamProvider` (null = off) + overridable `EventStreamId`; no reflection/attribute base-resolution. Well-known id `quark.journal:{grainType}` + grain key.
- **R5 — Ordering & delivery:** append-order guaranteed by the mailbox with in-turn awaited publish; `SequentialToken(version)`; **at-most-once** live delivery, log is source of truth; publish failure never rolls back a confirmed append.
- **R6 — Retention mechanism:** inline purge-on-snapshot from `ConfirmEventsAsync` (no enumeration surface); additive `ILogStoragePurge`; background sweep for idle grains deferred.
- **R7 — Retention requires snapshots:** hard dependency; `RetentionEnabled` is a no-op / config error without `SnapshotInterval > 0` + a snapshot store. Purge only below the durable snapshot version; readers floor at `OldestRetainedVersion`.
- **R8 — Schema evolution:** `[Alias]` + additive `TransitionState` arms as the immediate rule; runtime upcast hook shape reserved but deferred (R7-schema).
- **R9 — `IGrainContext`:** untouched. Identity via `ICallContext.GrainId` only (#78).
- **R10 — Backward compatibility:** all new `JournaledGrain` ctor params optional/defaulted; existing behaviors compile and run unchanged with every feature disabled.

---

## Dependencies & related work

- **Phase 3 → Phase 1** (hard): retention requires durable snapshots.
- **Phase 2 → Phase 1** (soft): independent, but the Bank demo composes both.
- **#41 persistent streams** (`2026-07-02-persistent-streams-design.md`, **not yet written**): the durable/at-least-once upgrade path for Phase 2. Phase 2 must not depend on it; in-memory streams suffice. Cross-link once that spec lands.
- **#104** (closed): wire-level unknown-field skip — the wire half of schema evolution; this spec supplies the type-level half.
- **AdoNet grain storage** (`2026-07-02-adonet-grain-storage-design.md`): a snapshot provider Phase 1 benefits from automatically (snapshots ride any `IGrainStorage`).
- **`GrainIdleCollector` / `SiloRuntimeOptions`:** the shape template for a future `JournalRetentionCollector` sweep.
- **Affected packages:** `Quark.Persistence.Abstractions` (all phases), `Quark.Persistence.InMemory` (Phase 3), `samples/Persistence` (demo, all phases), tests (`Quark.Tests.Unit/Journaling`, `Quark.Tests.Integration`, `Quark.Tests.Fault`), `wiki/Persistence.md` + `wiki/Streaming.md`. No client/transport/runtime changes for the in-memory scope; no code-generator changes (optional ctor params flow through existing DI wiring).

### Open items for user confirmation
- **R-DEP:** allow `Quark.Persistence.Abstractions` → `Quark.Streaming.Abstractions` project reference (clean today, no cycle), or prefer a separate bridge package/mix-in to keep persistence stream-agnostic?
- **R-CONC:** confirm the `InMemoryLogStorage` optimistic-concurrency rework (base-offset) is acceptable for Phase 3, and that any future durable log provider must persist the purge base version.
