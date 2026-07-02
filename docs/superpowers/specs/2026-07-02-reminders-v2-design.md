# Design: Reminders V2 — scalable, general-purpose reminders
**Issue:** #95
**Date:** 2026-07-02
**Status:** Draft — ready for implementation

---

## 1. Goals / Non-goals

### Goals
- **G1 — Ownership partitioning.** Each reminder is *scanned and fired* by exactly one silo, instead of every silo scanning the whole table and every silo firing every due reminder (today's behaviour). Removes both the O(N) per-silo full scan and the N-fold fan-out.
- **G2 — Rebalance on membership change.** When silos join/leave, reminder ownership redistributes with minimal churn, and no reminder is dropped or permanently double-owned.
- **G3 — Single-firer guarantee across transitions.** Even in the brief hand-off window where two silos disagree about ownership, a due reminder fires once. This leans on the CAS/lease from **#63** (prerequisite) — ownership makes the common case cheap; #63's CAS makes the transition case correct.
- **G4 — Caller-supplied context payload.** A registration may attach an opaque `ReadOnlyMemory<byte>` context, delivered on every tick, **without breaking Orleans drop-in `IRemindable`**.
- **G5 — Richer schedules where cheap.** Add fixed-delay alongside today's fixed-rate.

### Non-goals
- **Cron / calendar schedules.** Out of scope — see decision D5. Requires an expression parser (AOT/trim-heavy or a third-party dep) and a "compute next fire" that cannot be expressed as `StartAt + n·Period`. Deferrable to a later `IReminderSchedule` provider without reopening this design.
- **Re-designing the CAS/no-double-fire mechanism.** Owned by **#63**; this spec consumes its storage-level primitive and defines the ownership layer above it.
- **Sub-poll-interval precision.** Reminders remain coarse (poll-cadence) timers; sub-second precision is the timer subsystem's job.
- **Changing `IRemindable.ReceiveReminder`'s signature.** Stays byte-for-byte drop-in.
- **Adopting the new consistent-hash ring for grain placement.** Placement keeps its current modulo hash; the ring is introduced for reminders only (noted as future convergence work).

---

## 2. Current state (verified against live source)

**Firing model — single-silo scan loop replicated on every silo.**
`DefaultReminderService` (`src/Quark.Reminders.Abstractions/DefaultReminderService.cs`) is a singleton `IHostedService` registered on **every** silo. Its `RunLoopAsync` drives a `PeriodicTimer` (`ReminderOptions.PollInterval`, default 1 s). Each tick, `FireDueRemindersAsync`:
1. `_storage.ReadAllAsync()` — reads **the entire cluster's reminder table** (`DefaultReminderService.cs:130`).
2. For every entry with `NextFireAt <= now`: advances `NextFireAt += Period` and upserts (`:138-139`), then fires via `_invoker.InvokeVoidAsync(grainId, ReceiveReminderInvokable, ct)` (`:141-144`).

Consequences in a multi-silo cluster:
- **Full-table scan per silo per tick** — the Orleans #947 bottleneck this issue exists to avoid.
- **N-fold double-fire** — every silo independently fires every due reminder.
- **No CAS** — even single-silo, two overlapping ticks can double-advance (this is #63's evidence line `:128-146`).

**Firing is already location-transparent.** `LocalGrainCallInvoker.InvokeVoidAsync` routes to the grain's owning silo via `ISiloRouter.TryRouteRemote` (`src/Quark.Runtime/LocalGrainCallInvoker.cs:134-137`). So ownership only needs to decide **which silo initiates the fire**, never where the grain lives — the two are already decoupled.

**Storage SPI** (`IReminderStorage`): `ReadAllAsync`, `ReadByGrainAsync`, `UpsertAsync`, `DeleteAsync`. No range/partition query, no version/CAS. Providers: `InMemoryReminderStorage` (ConcurrentDictionary), `RedisReminderStorage` (one Redis Hash, STJ source-gen DTO).

**Durable record** (`ReminderEntry`): `GrainId`, `ReminderName`, `StartAt`, `Period`, `NextFireAt`. `NextFireAt` is advanced **before** invoke → fixed-rate semantics only. No context, no partition, no version.

**Callback contract** (`Quark.Core.Abstractions/Reminders/`):
- `IRemindable.ReceiveReminder(string reminderName, TickStatus status)` — Orleans drop-in.
- `TickStatus(DateTimeOffset FirstTickTime, TimeSpan Period, DateTimeOffset CurrentTickTime)` — positional `readonly record struct`.
- `ReceiveReminderInvokable` — `IGrainVoidInvokable` with a **no-op `Serialize`** (`:32`); currently carries nothing over the wire beyond name + status, and status is not itself codec-serialized.

**Cluster machinery available for reuse:**
- `IMembershipTable` + `MembershipEntry(SiloAddress, SiloName, Status, IAmAlive)` (`Quark.Core.Abstractions.Clustering`).
- `MembershipOracle` (`src/Quark.Runtime/Clustering/`) — writes IAmAlive every 10 s, evicts silos silent > 30 s, calls `ISiloRouter.Unregister`. This is the authoritative "who is Active" view.
- `PlacementDirector.SelectHashBased` (`src/Quark.Runtime/PlacementDirector.cs`) — orders silos, FNV-1a `ComputeStableHash`, `hash % count`. **Modulo hash, not consistent hash** — a membership change reshuffles ~all keys.
- No consistent-hash ring type exists anywhere in the tree.

**Layering constraint (hard):** `Quark.Reminders.Abstractions` references **only** `Quark.Core.Abstractions` (verified in its `.csproj`) — it must not reference `Quark.Runtime`. Any ownership *interface* the firing loop consumes must live in an Abstractions package; the ring *implementation* lives in `Quark.Runtime`.

---

## 3. Proposed API

### 3.1 Context payload — `TickStatus` gains a non-positional `Context` (Quark-native, drop-in preserved)

```csharp
// Quark.Core.Abstractions/Reminders/TickStatus.cs
public readonly record struct TickStatus(
    DateTimeOffset FirstTickTime,
    TimeSpan Period,
    DateTimeOffset CurrentTickTime)
{
    /// <summary>Opaque caller-supplied context registered with the reminder. Empty when none was set.</summary>
    public ReadOnlyMemory<byte> Context { get; init; } = ReadOnlyMemory<byte>.Empty;
}
```

`Context` is added as an **init-only property, not a fourth positional parameter** — this preserves the existing `new TickStatus(a, b, c)` 3-arg construction that `DefaultReminderService.cs:143` and every existing `IRemindable` test relies on. Existing grains that ignore `Context` are unaffected.

Typed convenience accessor (extension, opt-in, requires an explicitly registered codec — never reflection):

```csharp
// Quark.Reminders.Abstractions
public static class TickStatusContextExtensions
{
    public static T GetContext<T>(this in TickStatus status, IQuarkSerializer serializer);
    public static bool TryGetContext<T>(this in TickStatus status, IQuarkSerializer serializer, out T value);
}
```

### 3.2 `IReminderService` — additive context + schedule-kind overloads (minor-change)

```csharp
public enum ReminderScheduleKind
{
    FixedRate = 0,   // default; NextFireAt advances on the wall-clock grid (today's behaviour)
    FixedDelay = 1,  // next tick is Period after the previous tick *completed*
}

public interface IReminderService
{
    // Existing signature — UNCHANGED (drop-in). Delegates to the overload with empty context, FixedRate.
    Task<IGrainReminder> RegisterOrUpdateReminderAsync(
        GrainId grainId, string name, TimeSpan dueTime, TimeSpan period, CancellationToken ct = default);

    // NEW — raw context bytes + schedule kind.
    Task<IGrainReminder> RegisterOrUpdateReminderAsync(
        GrainId grainId, string name, TimeSpan dueTime, TimeSpan period,
        ReadOnlyMemory<byte> context,
        ReminderScheduleKind schedule = ReminderScheduleKind.FixedRate,
        CancellationToken ct = default);

    Task UnregisterReminderAsync(GrainId grainId, string name, CancellationToken ct = default);
    Task<IReadOnlyList<IGrainReminder>> GetRemindersAsync(GrainId grainId, CancellationToken ct = default);
}
```

Typed convenience (extension, opt-in, explicit codec):

```csharp
public static class ReminderServiceContextExtensions
{
    public static Task<IGrainReminder> RegisterOrUpdateReminderAsync<TContext>(
        this IReminderService svc, IQuarkSerializer serializer,
        GrainId grainId, string name, TimeSpan dueTime, TimeSpan period,
        TContext context, ReminderScheduleKind schedule = ReminderScheduleKind.FixedRate,
        CancellationToken ct = default);
}
```

### 3.3 `ReminderEntry` — gains `PartitionId`, `Context`, `Schedule`, `Version`

```csharp
public sealed record ReminderEntry
{
    public required GrainId GrainId { get; init; }
    public required string ReminderName { get; init; }
    public required DateTimeOffset StartAt { get; init; }
    public required TimeSpan Period { get; init; }
    public required DateTimeOffset NextFireAt { get; init; }

    // NEW
    public int PartitionId { get; init; }                                   // stable: hash(GrainId) % PartitionCount
    public ReadOnlyMemory<byte> Context { get; init; } = ReadOnlyMemory<byte>.Empty;
    public ReminderScheduleKind Schedule { get; init; } = ReminderScheduleKind.FixedRate;
    public long Version { get; init; }                                       // CAS token — semantics owned by #63
}
```

`PartitionId` is derived **at registration** from a stable hash of `GrainId` only (not the reminder name), so **all reminders of one grain share a partition** and land on one owning silo — cheaper scans, and a single grain never fans across owners.

### 3.4 `IReminderStorage` — partition-scoped read + CAS advance (minor-change to the provider SPI)

```csharp
public interface IReminderStorage
{
    // NEW hot path: only entries in the given partitions that are due at or before `dueBefore`.
    Task<IReadOnlyList<ReminderEntry>> ReadDuePartitionedAsync(
        IReadOnlyCollection<int> partitionIds, DateTimeOffset dueBefore, CancellationToken ct = default);

    // NEW: compare-and-swap advance. Returns true iff `expectedVersion` still matched (the caller won the tick).
    // Exact contract (version vs lease/TTL) is defined by #63; this spec assumes an optimistic Version.
    Task<bool> TryAdvanceAsync(
        GrainId grainId, string reminderName, long expectedVersion, ReminderEntry advanced,
        CancellationToken ct = default);

    Task<IReadOnlyList<ReminderEntry>> ReadByGrainAsync(GrainId grainId, CancellationToken ct = default);
    Task UpsertAsync(ReminderEntry entry, CancellationToken ct = default);
    Task DeleteAsync(GrainId grainId, string reminderName, CancellationToken ct = default);

    // Retained for admin/testing only; no longer on the firing hot path.
    Task<IReadOnlyList<ReminderEntry>> ReadAllAsync(CancellationToken ct = default);
}
```

- **InMemory:** add a `partitionId → set<key>` secondary index; `ReadDuePartitionedAsync` filters it; `TryAdvanceAsync` guards with per-key `Version` compare under the existing lock-free dictionary (CompareExchange on an immutable record swap).
- **Redis:** keep the entry Hash; add a **per-partition sorted set** `reminders:p:{partitionId}` scored by `NextFireAt.UtcTicks` so `ReadDuePartitionedAsync` is `ZRANGEBYSCORE 0 now` per owned partition (bounded, indexed). `TryAdvanceAsync` = Lua script doing `HGET version → compare → HSET + ZADD` atomically (this is precisely #63's CAS primitive).

### 3.5 Ownership abstraction (lives in Abstractions; real impl in Runtime)

```csharp
// Quark.Reminders.Abstractions
public interface IReminderPartitionOwnership
{
    int PartitionCount { get; }
    int PartitionOf(GrainId grainId);                         // stable hash → [0, PartitionCount)
    IReadOnlyCollection<int> GetOwnedPartitions();            // partitions this silo currently owns
    event Action? OwnershipChanged;                           // raised when membership reshuffles ownership
}
```

- **Default (single-silo / tests):** `SingleSiloReminderOwnership` — `GetOwnedPartitions()` returns all partitions; `OwnershipChanged` never fires. Registered by `AddInMemoryReminders` / `AddRedisReminders`.
- **Clustered:** `ConsistentHashReminderOwnership` in `Quark.Runtime/Clustering/` — built on a new `ConsistentHashRing<SiloAddress>` seeded from `IMembershipTable` Active entries, recomputed when the membership set changes; `TryAddSingleton`-overrides the default when clustering is configured.

`ReminderOptions.PartitionCount` (new, default e.g. **1024**) fixes the partition space so it is stable independent of silo count.

---

## 4. Ownership & partitioning

### 4.1 Two-level scheme

```
reminder  --stable hash(GrainId) % PartitionCount-->  partitionId   (fixed, stored on the entry)
partitionId  --consistent-hash ring over Active silos-->  owning silo   (rebalances on membership)
```

Two levels because a **fixed** partition space (1024 virtual partitions) is what makes storage indexable (`ReadDuePartitionedAsync` over a bounded id set) and what lets ownership move in **partition-sized granules** rather than per-reminder. Only the partition→silo map churns on membership change; the reminder→partition map is immutable.

### 4.2 Reuse evaluation — existing machinery vs bespoke (issue's central question)

| Existing piece | Reuse verdict |
|---|---|
| `IMembershipTable` + `MembershipOracle` | **Reuse as-is.** Authoritative Active-silo view already exists and is maintained; ownership derives its ring from it. No new membership scheme. |
| `ISiloRouter` / `LocalGrainCallInvoker` routing | **Reuse as-is for firing.** The invoker already routes the fire to the grain's silo; ownership does not touch this. |
| `PlacementDirector.SelectHashBased` (modulo hash) | **Do not reuse for ownership.** Modulo hashing reshuffles ~all keys on any membership change → a cluster-wide reminder-scan hand-off storm and a wide #63-CAS contention window on every join/leave. Grain placement tolerates this (grains reactivate lazily); reminder ownership does not. |
| Consistent-hash ring | **New (bespoke) primitive** — `ConsistentHashRing<SiloAddress>` with virtual nodes, in `Quark.Runtime/Clustering/`. Introduced for reminders; placement may converge onto it later (future work, not this issue). |

**Net:** reuse membership + routing wholesale; add one small consistent-hash ring for the ownership function. This directly answers issue #95's open question ("can sharding reuse Quark's placement/hash machinery?") — **partly**: reuse the cluster-membership and routing halves; replace the modulo-hash half with consistent hashing, because minimal-movement rebalancing is the whole point.

### 4.3 Rebalancing on membership change

1. `ConsistentHashReminderOwnership` observes the Active set (polls `IMembershipTable.ReadAllAsync` on the same cadence the oracle already uses, or subscribes if a membership-changed signal is added later; MVP: recompute each poll tick, cheap for ≤ hundreds of silos).
2. It hashes a *membership fingerprint*; when it changes, it rebuilds the ring, recomputes `GetOwnedPartitions()`, and raises `OwnershipChanged`.
3. The firing loop reads `GetOwnedPartitions()` each tick, so it naturally starts/stops scanning moved partitions on the next tick — no explicit hand-off protocol.

Because a silo join/leave with a consistent-hash ring moves only ~`1/N` of partitions, only that fraction of partitions ever has a transient dual-owner window.

### 4.4 Single-firer guarantee

- **Steady state:** exactly one silo owns a partition → exactly one silo scans it → exactly one fire. Ownership alone suffices.
- **Transition window** (old owner still scanning, new owner already scanning the moved partition): both may see the same due entry. Here **#63's `TryAdvanceAsync` CAS** decides — the loser's advance fails and it skips the fire. Ownership shrinks the contended set to the moved partitions; #63 guarantees correctness within it. **This is why #63 is a hard prerequisite, not a nice-to-have.**
- **Delivery guarantee stays at-least-once** (advance-then-fire; a crash after advance but before fire drops that one tick — unchanged from today, documented).

---

## 5. Runtime integration (anchors)

- **`DefaultReminderService.FireDueRemindersAsync`** (`src/Quark.Reminders.Abstractions/DefaultReminderService.cs:128-146`) — rewrite:
  - Replace `ReadAllAsync()` with `_ownership.GetOwnedPartitions()` → `_storage.ReadDuePartitionedAsync(owned, now, ct)`.
  - Replace the unconditional `UpsertAsync(advanced)` (`:138-139`) with `_storage.TryAdvanceAsync(...expectedVersion...)`; only fire when it returns `true`.
  - Compute the advanced `NextFireAt` per `entry.Schedule`: `FixedRate` → `NextFireAt + Period` (grid, today's line `:138`); `FixedDelay` → await the invoke, then `now + Period`. FixedDelay therefore advances *after* `InvokeVoidAsync` completes; FixedRate advances *before* (preserving current at-least-once-on-crash behaviour).
  - Pass `entry.Context` into `new TickStatus(entry.StartAt, entry.Period, entry.NextFireAt) { Context = entry.Context }` (`:143`).
  - Inject `IReminderPartitionOwnership` via constructor (`:25-35`).
- **`DefaultReminderService.RegisterOrUpdateReminderAsync`** (`:47-63`) — set `PartitionId = _ownership.PartitionOf(grainId)`, `Context`, `Schedule`, initial `Version` on the new `ReminderEntry`.
- **`ReceiveReminderInvokable`** (`Quark.Core.Abstractions/Reminders/ReceiveReminderInvokable.cs`) — carries `TickStatus` including `Context`; for the **TCP path**, its currently-empty `Serialize` (`:32`) must write name + status + context bytes, and a matching deserializer read them, so a reminder that fires on the owning silo but targets a grain on another silo delivers context over the wire. (In-process fires never serialize.)
- **Registration extensions** (`AddInMemoryReminders`, `AddRedisReminders`) — `TryAddSingleton<IReminderPartitionOwnership, SingleSiloReminderOwnership>()`. The clustering wiring in `Quark.Runtime` overrides with `ConsistentHashReminderOwnership` (registered where `MembershipOracle`/`ISiloRouter` are wired).
- **New `ConsistentHashRing<T>`** — `Quark.Runtime/Clustering/ConsistentHashRing.cs`, pure arithmetic, virtual-node ring, `GetOwner(int partitionId)`.
- **Note on `IReminderService.ReminderService` doc** — `IReminderService.cs` XML-doc references `Hosting.IGrainContext.ReminderService`; per house rule #78 `IGrainContext` is dead. Behaviors resolve `IReminderService` from DI (singleton). Fix the doc comment to stop referencing the stale type (no code change).

---

## 6. AOT notes

- **Context is `ReadOnlyMemory<byte>` end-to-end** — no reflection, no boxing. Typed `GetContext<T>` / `RegisterOrUpdateReminderAsync<T>` require an **explicitly registered** `IQuarkSerializer`/`IFieldCodec<T>` (same contract as `AddStreamableCodec<T,TCodec>`); never STJ-reflection, never `Activator`.
- **Redis DTO** — add `ContextBase64` (or `byte[] Context`; STJ source-gen encodes `byte[]` as base64), `PartitionId`, `Schedule` (int), `Version` (long) to `RedisReminderStorage.ReminderEntryDto` and its `[JsonSerializable]` context (`ReminderEntryDtoJsonContext`). Stays fully source-generated — no reflection fallback.
- **`ConsistentHashRing<SiloAddress>`** — arithmetic + sorted array; no reflection, no dynamic code. AOT-clean.
- **`ReceiveReminderInvokable.Serialize`** — uses `CodecWriter`/`CodecReader` (LEB128) directly, consistent with the codebase's no-`ISerializable` rule (avoids QRK0003).
- **All new registrations explicit** — ownership, partition count, storage all wired via the existing `Add*Reminders` extensions; no assembly scanning.
- **AOT smoke build** (`dotnet publish … /p:PublishAot=true`) must remain warning-free.

---

## 7. Test plan

- **Multi-silo single-fire** (2–3 silo `TestCluster`): a reminder fires **exactly once** cluster-wide over K periods (assert the target grain's tick count == K, not K·N).
- **Ownership partitioning:** register many reminders; assert each silo scans only its owned partition set (probe via a test `IReminderStorage` recording `ReadDuePartitionedAsync` partition args) and no partition is scanned by two Active silos in steady state.
- **Rebalance — silo leave:** kill an owner mid-run; its partitions reassign; reminders continue firing once, none dropped.
- **Rebalance — silo join:** add a silo mid-run; ~1/N partitions move; assert no double-fire during the window (this exercises the #63 CAS path — pairs with #63's concurrent-poller test).
- **CAS loser skips** (integration with #63): two pollers over the same partition → one `TryAdvanceAsync` wins, the other returns false and does not fire.
- **Context round-trip:** register with raw bytes → `ReceiveReminder` observes identical bytes; typed overload round-trips a `[GenerateSerializer]` DTO via a registered codec; empty-context path yields `TickStatus.Context.IsEmpty`.
- **Context over TCP:** reminder owned on silo A, grain on silo B → context arrives intact (exercises `ReceiveReminderInvokable.Serialize`).
- **Schedule semantics:** FixedRate keeps the wall-clock grid under a slow callback; FixedDelay spaces ticks by `Period` measured from callback completion.
- **Orleans drop-in regression:** an existing `IRemindable` using `new TickStatus(a,b,c)` and 4-arg `RegisterOrUpdateReminderAsync` compiles and fires unchanged.
- **AOT smoke build** passes.
- **Provider parity:** run the storage suite against both InMemory and Redis (`Testcontainers`, `[Trait("category","integration")]`).

---

## 8. Implementation checklist (ordered — safe top-to-bottom, no circular deps)

1. `Quark.Core.Abstractions/Reminders/TickStatus.cs` — add init-only `Context`.
2. `Quark.Core.Abstractions/Reminders/ReminderScheduleKind.cs` — new enum.
3. `Quark.Core.Abstractions/Reminders/IReminderService.cs` — add context/schedule overload; fix stale `IGrainContext` doc reference.
4. `Quark.Core.Abstractions/Reminders/ReceiveReminderInvokable.cs` — carry `Context`; implement `Serialize`/deserialize for the TCP path.
5. `Quark.Reminders.Abstractions/ReminderOptions.cs` — add `PartitionCount`.
6. `Quark.Reminders.Abstractions/ReminderEntry.cs` — add `PartitionId`, `Context`, `Schedule`, `Version`.
7. `Quark.Reminders.Abstractions/IReminderPartitionOwnership.cs` — new interface.
8. `Quark.Reminders.Abstractions/SingleSiloReminderOwnership.cs` — default all-owning impl.
9. `Quark.Reminders.Abstractions/IReminderStorage.cs` — add `ReadDuePartitionedAsync` + `TryAdvanceAsync` (align CAS shape with #63).
10. `Quark.Reminders.Abstractions/TickStatusContextExtensions.cs` + `ReminderServiceContextExtensions.cs` — typed helpers.
11. `Quark.Reminders.Abstractions/DefaultReminderService.cs` — inject ownership; rewrite `FireDueRemindersAsync` (owned-partition read + CAS + schedule + context); set new fields in `RegisterOrUpdate`.
12. `Quark.Reminders.InMemory/InMemoryReminderStorage.cs` — partition index + CAS.
13. `Quark.Reminders.InMemory/…ServiceCollectionExtensions.cs` — `TryAddSingleton<IReminderPartitionOwnership, SingleSiloReminderOwnership>()`.
14. `Quark.Reminders.Redis/RedisReminderStorage.cs` (+ DTO + JSON context) — per-partition sorted set, Lua CAS, new DTO fields.
15. `Quark.Reminders.Redis/…ServiceCollectionExtensions.cs` — default ownership registration.
16. `Quark.Runtime/Clustering/ConsistentHashRing.cs` — new ring primitive.
17. `Quark.Runtime/Clustering/ConsistentHashReminderOwnership.cs` — ring over `IMembershipTable`; `OwnershipChanged`.
18. `Quark.Runtime` clustering wiring — override `IReminderPartitionOwnership` with the clustered impl when clustering is configured.
19. Tests (`Quark.Tests.Unit`, `Quark.Tests.Integration`, `Quark.Tests.Fault[.Integration]`) per §7.
20. Docs: `wiki/` reminders section, `FEATURES.md` parity entry, `CLAUDE.md` reminders note (context + schedules + ownership).

---

## 9. Resolved design decisions (answers to all issue questions)

- **D1 — Reuse placement/hash machinery for sharding? (issue #95 Q1).** *Partly.* Reuse `IMembershipTable`/`MembershipOracle` (silo view) and `ISiloRouter`/invoker (firing) unchanged. **Do not** reuse `PlacementDirector`'s modulo hash — introduce a purpose-built `ConsistentHashRing` because reminder ownership needs minimal-movement rebalancing that modulo hashing cannot give. Partitioning is a two-level scheme (stable `hash(GrainId) % PartitionCount` → ring → silo).
- **D2 — What does "user-specified context" map to? (issue #95 Q2).** An opaque `ReadOnlyMemory<byte>` attached at registration, stored on `ReminderEntry`, delivered via a new **init-only `TickStatus.Context`**. Typed access through an explicitly registered codec. **`IRemindable.ReceiveReminder` stays byte-for-byte drop-in** — context rides on the existing `TickStatus` parameter, so no new overload and no forced re-implementation.
- **D3 — Deliver context via new `ReceiveReminder` overload or `TickStatus`?** Via `TickStatus`. A new `IRemindable` overload would break every existing implementer (drop-in violation). Adding a non-positional field to the status struct is additive and compiles existing call sites.
- **D4 — Single-firer mechanism.** Ownership (steady state) + #63 CAS (`TryAdvanceAsync`, transition window). Delivery remains at-least-once (advance-then-fire).
- **D5 — Cron?** **Out of scope.** Justification: needs an expression parser (AOT/trim-hostile or a third-party dep) and a non-`StartAt+n·Period` "next fire" computation; neither of the two asks requires it; it can be added later as an `IReminderSchedule` strategy without touching this design. V2 ships `FixedRate` (default, drop-in) + `FixedDelay` (Quark-native) only — both cheap, both TimeSpan-expressible.
- **D6 — Partition granularity.** Per-**grain** (`hash(GrainId)`, not per-reminder), so all of a grain's reminders co-locate on one owner. Fixed `PartitionCount` (default 1024) decouples the partition space from silo count.
- **D7 — Ownership interface placement.** In `Quark.Reminders.Abstractions` (so the firing loop can consume it without referencing `Quark.Runtime`); real consistent-hash impl in `Quark.Runtime.Clustering`; default all-owning impl for single-silo/tests.
- **D8 — Context param type in the delegate/hook.** N/A to the #78 scope-initializer; but for symmetry, nothing here exposes `ICallContext`/`IGrainContext` beyond `GrainId` — the firing path uses `GrainId` + the invoker only, honouring the ground rule.

### Compatibility tiers
| Change | Tier | Justification |
|---|---|---|
| `TickStatus.Context` (init-only) | **Quark-native**, drop-in preserved | Orleans `TickStatus` has no context; additive field keeps the 3-arg ctor. |
| `RegisterOrUpdateReminderAsync` context/schedule overload | **Minor-change** | New overload; existing 4-arg signature untouched. |
| `ReminderScheduleKind.FixedDelay` | **Quark-native** | No Orleans equivalent; FixedRate is the drop-in default. |
| `IReminderStorage` partition/CAS methods | **Minor-change (provider SPI)** | Breaking only for custom out-of-tree providers; both in-tree providers updated. |
| Ownership partitioning + ring | **Quark-native (internal)** | No public surface beyond `PartitionCount` + the ownership interface. |
| `IRemindable.ReceiveReminder` | **Drop-in (unchanged)** | Deliberately not touched. |

---

## 10. Dependencies & related work

- **#63 (prerequisite, hard).** Provides the storage-level CAS/lease (`TryAdvanceAsync` shape). This spec assumes an optimistic `Version`; if #63 lands a lease/TTL model instead, adjust `IReminderStorage.TryAdvanceAsync` and `ReminderEntry.Version` to match — the ownership layer above is agnostic to which #63 chooses.
- **#78 (adjacent).** Confirms `IGrainContext` is dead; this spec keeps the reminder path on `GrainId` + invoker only and fixes the one stale doc reference in `IReminderService.cs`.
- **Reuses:** `IMembershipTable`, `MembershipOracle`, `SiloAddress`, `ISiloRouter`, `LocalGrainCallInvoker` routing, `IQuarkSerializer`/codec infra, STJ source-gen (`ReminderEntryDtoJsonContext`).
- **Future convergence (not this issue):** migrate `PlacementDirector.SelectHashBased` onto the new `ConsistentHashRing` so grain placement and reminder ownership share one ring primitive.
- **Orleans references:** dotnet/orleans#7477 (Reminders V2 epic), #7573, #947 (the 2015 `IReminderTable` scalability bottleneck this design avoids by construction).
