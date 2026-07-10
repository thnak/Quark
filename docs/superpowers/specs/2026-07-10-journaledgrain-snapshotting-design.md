# JournaledGrain snapshotting — design

**Issue:** #144 — *JournaledGrain replays the entire event log on every activation — no snapshotting*
**Date:** 2026-07-10
**Status:** Approved design — ready for implementation plan
**Scope:** `ISnapshotStore` abstraction + `InMemorySnapshotStore` + `JournaledGrain` wiring + config + exception + tests. Redis snapshot store and a durable Redis `ILogStorage` are an explicit follow-up (see §9).

## 1. Problem

`JournaledGrain<TState,TEvent>.ReloadFromLogAsync` reads and replays **every** event from
version 0 to `int.MaxValue` on each activation
(`src/Quark.Persistence.Abstractions/Journaling/JournaledGrain.cs`):

```csharp
private async Task ReloadFromLogAsync(CancellationToken ct)
{
    JournaledGrainState<TState, TEvent> st = _memory.Value;
    IReadOnlyList<LogEntry> all =
        await _logStorage!.ReadEntriesAsync(GrainId, 0, int.MaxValue, ct).ConfigureAwait(false);
    st.State = new TState();
    foreach (LogEntry entry in all)
    {
        TransitionState(st.State, (TEvent)entry.Event);
        st.ConfirmedVersion = entry.Version + 1;
    }
}
```

Replay cost is unbounded and grows linearly with the grain's entire event history, forever.
A long-lived event-sourced grain (bank account, inventory ledger) pays a growing activation
latency that never amortizes. No snapshot mechanism exists anywhere in the codebase.

## 2. Guiding principle

The **event log remains the sole source of truth.** A snapshot is *only* a replay-shortcut:
given a snapshot `(version V, state S)`, activation seeds `State = S` and replays only log
entries `[V, …)` instead of `[0, …)`. A snapshot must never affect correctness — if it is
missing, broken, or inconsistent with the log, the system either falls back to a full replay
(missing) or fails loudly (broken), but never silently produces wrong state.

## 3. Design decisions (locked)

| Decision | Choice |
|---|---|
| **Storage** | A **new dedicated `ISnapshotStore`** abstraction, separate from both `ILogStorage` and `IGrainStorage`. |
| **Cadence** | **Auto every N confirmed events** (configurable; default 100; `0` disables) **plus** a manual `WriteSnapshotAsync()` hook. |
| **Fallback policy** | **Strict / fail-fast.** A *missing* snapshot is normal → full replay. A *present-but-broken* snapshot (undeserializable, or version ahead of the log) throws `CorruptSnapshotException` and blocks activation. |
| **Recovery** | `ISnapshotStore.ClearSnapshotAsync(grainId)` + typed `CorruptSnapshotException` (carries `GrainId` + snapshot version). |
| **Provider scope** | `InMemorySnapshotStore` now + the abstraction. Redis snapshot store and durable Redis `ILogStorage` are a called-out follow-up. |

## 4. New abstraction — `Quark.Persistence.Abstractions.Journaling`

```csharp
/// <summary>
///     Optional snapshot store for <see cref="JournaledGrain{TState,TEvent}"/>. A snapshot is a
///     replay-shortcut only; the event log remains the source of truth. Missing snapshots are
///     normal (activation full-replays). A present-but-corrupt snapshot must surface as a
///     <see cref="CorruptSnapshotException"/> rather than silently producing wrong state.
/// </summary>
public interface ISnapshotStore
{
    /// <summary>
    ///     Reads the latest snapshot for <paramref name="grainId"/>, or <c>null</c> if none exists.
    ///     Durable providers throw <see cref="CorruptSnapshotException"/> when a stored snapshot
    ///     cannot be deserialized into <typeparamref name="TState"/>.
    /// </summary>
    Task<SnapshotEnvelope<TState>?> ReadSnapshotAsync<TState>(
        GrainId grainId, CancellationToken ct = default) where TState : class;

    /// <summary>Writes (replaces) the snapshot for <paramref name="grainId"/>.</summary>
    Task WriteSnapshotAsync<TState>(
        GrainId grainId, SnapshotEnvelope<TState> snapshot, CancellationToken ct = default)
        where TState : class;

    /// <summary>Deletes any stored snapshot for <paramref name="grainId"/> (recovery path).</summary>
    Task ClearSnapshotAsync(GrainId grainId, CancellationToken ct = default);
}

/// <summary>A point-in-time projection of grain state and the log version it folds up to.</summary>
public sealed class SnapshotEnvelope<TState>
{
    public SnapshotEnvelope(int version, TState state) { Version = version; State = state; }

    /// <summary>Number of events folded into <see cref="State"/> — i.e. the index of the next event.</summary>
    public int Version { get; }

    /// <summary>State after applying events <c>[0, Version)</c>.</summary>
    public TState State { get; }
}

/// <summary>Thrown when a present snapshot is unusable (undeserializable or inconsistent with the log).</summary>
public sealed class CorruptSnapshotException : Exception
{
    public CorruptSnapshotException(GrainId grainId, int snapshotVersion, string message, Exception? inner = null)
        : base(message, inner) { GrainId = grainId; SnapshotVersion = snapshotVersion; }

    public GrainId GrainId { get; }
    public int SnapshotVersion { get; }
}
```

**Rationale for a per-call generic `TState` rather than a typed store:** mirrors `IGrainStorage`'s
existing shape (`ReadStateAsync<TState>`), so a single provider instance serves all grain types.

## 5. Provider — `InMemorySnapshotStore` (`Quark.Persistence.InMemory`)

```csharp
public sealed class InMemorySnapshotStore : ISnapshotStore
{
    private readonly ConcurrentDictionary<GrainId, (int Version, object State)> _snapshots = new();
    private readonly ICopierProvider _copiers;   // already reachable: this package references Quark.Serialization

    public InMemorySnapshotStore(ICopierProvider copiers) => _copiers = copiers;

    public Task WriteSnapshotAsync<TState>(GrainId id, SnapshotEnvelope<TState> snap, CancellationToken ct = default)
        where TState : class
    {
        ct.ThrowIfCancellationRequested();
        // Same deep-copy idiom already used by InMemoryGrainStorage.cs:92.
        TState isolated = _copiers.GetRequiredCopier<TState>().DeepCopy(snap.State, new CopyContext());
        _snapshots[id] = (snap.Version, isolated);
        return Task.CompletedTask;
    }

    public Task<SnapshotEnvelope<TState>?> ReadSnapshotAsync<TState>(GrainId id, CancellationToken ct = default)
        where TState : class
    {
        ct.ThrowIfCancellationRequested();
        if (!_snapshots.TryGetValue(id, out var e))
            return Task.FromResult<SnapshotEnvelope<TState>?>(null);
        TState copy = _copiers.GetRequiredCopier<TState>().DeepCopy((TState)e.State, new CopyContext());
        return Task.FromResult<SnapshotEnvelope<TState>?>(new SnapshotEnvelope<TState>(e.Version, copy));
    }

    public Task ClearSnapshotAsync(GrainId id, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _snapshots.TryRemove(id, out _);
        return Task.CompletedTask;
    }
}
```

**Why deep-copy on both write and read:** the grain hands us its *live* `State`, which keeps being
mutated by later `RaiseEvent` calls. Without an isolating copy, the stored snapshot would drift to
reflect later mutations, and a subsequent activation would double-apply events. Copying on write
isolates the store from the caller; copying on read isolates the caller from the store (the returned
state is mutated during tail replay).

**Soft new constraint:** a snapshotted `TState` needs a generated deep copier — i.e. `[GenerateSerializer]`
on `TState`. This is the same machinery Quark already uses for in-process grain-call isolation, so it is
idiomatic; it is only required for grains that actually enable snapshotting. The Bank sample's `LedgerState`
gains `[GenerateSerializer]` to demonstrate.

**DI registration** (`Quark.Persistence.InMemory`):

```csharp
public static IServiceCollection AddInMemorySnapshotStore(this IServiceCollection services)
{
    services.TryAddSingleton<ISnapshotStore, InMemorySnapshotStore>();
    return services;
}
```

`InMemorySnapshotStore` never throws `CorruptSnapshotException` — with no serialization there is nothing
to corrupt. The undeserializable-snapshot failure mode belongs to durable providers (follow-up); the
*ahead-of-log* failure mode is detected in `JournaledGrain` (§6) and applies to every provider.

## 6. `JournaledGrain` changes

### 6.1 Constructor & state

```csharp
protected JournaledGrain(
    IActivationMemory<JournaledGrainState<TState, TEvent>> memory,
    ICallContext ctx,
    ILogStorage? logStorage = null,
    ISnapshotStore? snapshotStore = null)   // NEW — optional, backward-compatible
```

An unregistered `ISnapshotStore` resolves to `null` (snapshotting off), exactly as `ILogStorage`
does today. Because JournaledGrain subclasses already carry nullable-default ctor params, the
`BehaviorRegistrationGenerator` already routes them through the `ActivatorUtilities` reflection path
(a compile-time factory is only generated when *all* ctor params are required — see
`BehaviorRegistrationGenerator.cs`, the `Parameters.All(p => !p.HasExplicitDefaultValue)` guard). So
`ActivatorUtilities.CreateInstance` fills an unregistered optional param with its default. **No source
generator change is required.**

`JournaledGrainState<TState,TEvent>` gains one field:

```csharp
public int LastSnapshotVersion { get; set; }   // ConfirmedVersion at the last snapshot write
```

### 6.2 Cadence configuration

```csharp
/// <summary>
///     Number of confirmed events between automatic snapshots. Override per grain type.
///     <c>0</c> disables automatic snapshotting for this grain type. Default: 100.
/// </summary>
protected virtual int SnapshotInterval => 100;
```

Per-type override via a virtual property keeps configuration zero-DI and discoverable. Automatic
snapshotting only ever happens when an `ISnapshotStore` is registered *and* `SnapshotInterval > 0`.

### 6.3 Write path (append + auto-snapshot)

At the end of `ConfirmEventsAsync`, after the log append and `ConfirmedVersion` bump:

```csharp
st.ConfirmedVersion += st.StagedEvents.Count;
st.StagedEvents.Clear();

if (_snapshotStore is not null && SnapshotInterval > 0 &&
    st.ConfirmedVersion - st.LastSnapshotVersion >= SnapshotInterval)
{
    await WriteSnapshotCoreAsync(cancellationToken).ConfigureAwait(false);
}
```

```csharp
private async Task WriteSnapshotCoreAsync(CancellationToken ct)
{
    JournaledGrainState<TState, TEvent> st = _memory.Value;
    await _snapshotStore!.WriteSnapshotAsync(
        GrainId, new SnapshotEnvelope<TState>(st.ConfirmedVersion, st.State), ct).ConfigureAwait(false);
    st.LastSnapshotVersion = st.ConfirmedVersion;
}

/// <summary>Manually writes a snapshot of the current confirmed state. No-op if no snapshot store is registered.</summary>
protected Task WriteSnapshotAsync(CancellationToken cancellationToken = default) =>
    _snapshotStore is null ? Task.CompletedTask : WriteSnapshotCoreAsync(cancellationToken);
```

### 6.4 Activation path (snapshot-aware replay)

```csharp
private async Task ReloadFromLogAsync(CancellationToken ct)
{
    JournaledGrainState<TState, TEvent> st = _memory.Value;
    st.State = new TState();
    st.ConfirmedVersion = 0;
    st.LastSnapshotVersion = 0;

    if (_snapshotStore is not null)
    {
        SnapshotEnvelope<TState>? snap =
            await _snapshotStore.ReadSnapshotAsync<TState>(GrainId, ct).ConfigureAwait(false);
        //   ^ durable providers throw CorruptSnapshotException here on an undeserializable snapshot

        if (snap is not null && snap.Version > 0)
        {
            // Boundary probe: read from snap.Version-1 so we can confirm the log actually contains
            // >= snap.Version contiguous entries WITHOUT adding a length API to ILogStorage
            // (AppendEntriesAsync guarantees version == index, so entry[V-1] existing ⇒ 0..V-1 all exist).
            IReadOnlyList<LogEntry> tail =
                await _logStorage!.ReadEntriesAsync(GrainId, snap.Version - 1, int.MaxValue, ct).ConfigureAwait(false);

            if (tail.Count == 0 || tail[0].Version != snap.Version - 1)
                throw new CorruptSnapshotException(GrainId, snap.Version,
                    $"Snapshot version {snap.Version} is ahead of the event log for grain {GrainId}.");

            st.State = snap.State;                       // store returned an isolated copy
            st.ConfirmedVersion = snap.Version;
            st.LastSnapshotVersion = snap.Version;

            for (int i = 1; i < tail.Count; i++)         // skip the boundary entry (already in the snapshot)
            {
                TransitionState(st.State, (TEvent)tail[i].Event);
                st.ConfirmedVersion = tail[i].Version + 1;
            }
            return;
        }
    }

    // No usable snapshot → full replay from 0 (today's behavior).
    IReadOnlyList<LogEntry> all =
        await _logStorage!.ReadEntriesAsync(GrainId, 0, int.MaxValue, ct).ConfigureAwait(false);
    foreach (LogEntry entry in all)
    {
        TransitionState(st.State, (TEvent)entry.Event);
        st.ConfirmedVersion = entry.Version + 1;
    }
}
```

`OnActivateAsync` guard is unchanged: replay only runs when `_logStorage is not null`. When a snapshot
store is registered but no log store is, snapshotting is inert (there is nothing to shorten).

## 7. Error handling & recovery

| Situation | Behavior |
|---|---|
| No snapshot stored | Normal — full replay from 0. Not an error. |
| `SnapshotInterval == 0` or no `ISnapshotStore` | Snapshotting off; today's full-replay behavior. |
| Snapshot present, deserialization fails (durable) | `CorruptSnapshotException(grainId, version)` from `ReadSnapshotAsync`. |
| Snapshot present, version ahead of log | `CorruptSnapshotException(grainId, version)` from the boundary probe. |
| Recovery | Operator / management grain calls `ClearSnapshotAsync(grainId)`; next activation full-replays and writes a fresh snapshot. |

Fail-fast is deliberate: a durability subsystem should surface corruption loudly rather than silently
burning CPU on repeated full replays or, worse, producing wrong state.

## 8. Testing (`tests/Quark.Tests.Unit/Journaling`)

A spying `ILogStorage` decorator records the `(fromVersion, toVersion)` of each `ReadEntriesAsync` so
tests can assert *how many* entries were replayed.

- **Auto-snapshot at interval:** confirm N events with `SnapshotInterval = N` ⇒ exactly one snapshot written at version N.
- **Tail-only replay:** with a snapshot at V and events up to V+k, a fresh activation replays only `[V, V+k)` (assert via the spy: read starts at `V-1`, not 0).
- **Manual `WriteSnapshotAsync`:** writes at the current confirmed version regardless of interval.
- **Disabled:** `SnapshotInterval = 0` ⇒ no snapshot ever written; full replay on activation.
- **Missing snapshot:** store returns null ⇒ full replay from 0; no exception.
- **Ahead-of-log:** snapshot version > log length ⇒ `CorruptSnapshotException`.
- **Recovery:** after a forced corrupt/ahead snapshot, `ClearSnapshotAsync` ⇒ next activation succeeds via full replay and rewrites a snapshot.
- **Deep-copy isolation:** write a snapshot, mutate live `State`, read the snapshot back ⇒ read reflects the value at snapshot time, not the later mutation.
- **Bank sample:** `LedgerState` gains `[GenerateSerializer]`; `Bank.Server` registers `AddInMemorySnapshotStore()`; a deactivate/reactivate cycle after many events replays only the tail.

## 9. Follow-up (out of scope for this spec)

- **`RedisSnapshotStore`** — serialize `TState` via `QuarkSerializer`/`IFieldCodec<TState>` (as Redis grain storage already does); this is where the *undeserializable-snapshot* `CorruptSnapshotException` path is exercised.
- **Durable Redis `ILogStorage`** — a durable snapshot paired with the current InMemory-only log is only half-durable; a durable log is the natural companion. File as a paired issue.
- **`SnapshotOptions` global default** — if per-type `SnapshotInterval` overrides prove insufficient, add a silo-level default interval. Not needed for v1.

## 10. Files touched

**New**
- `src/Quark.Persistence.Abstractions/Journaling/ISnapshotStore.cs` (`ISnapshotStore`, `SnapshotEnvelope<TState>`, `CorruptSnapshotException`)
- `src/Quark.Persistence.InMemory/InMemorySnapshotStore.cs`
- `tests/Quark.Tests.Unit/Journaling/JournaledGrainSnapshotTests.cs`

**Modified**
- `src/Quark.Persistence.Abstractions/Journaling/JournaledGrain.cs` (ctor param, cadence property, write path, activation path, `WriteSnapshotAsync`)
- `src/Quark.Persistence.Abstractions/Journaling/JournaledGrainState.cs` (`LastSnapshotVersion`)
- `src/Quark.Persistence.InMemory/InMemorySnapshotStoreServiceCollectionExtensions.cs` (`AddInMemorySnapshotStore`) — new, sibling to `InMemoryGrainStorageServiceCollectionExtensions`
- `samples/Persistence/Bank.Grains/LedgerBehavior.cs` + `LedgerState` (`[GenerateSerializer]`, forward `ISnapshotStore`)
- `samples/Persistence/Bank.Server/Program.cs` (`AddInMemorySnapshotStore()`)
