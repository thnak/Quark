# JournaledGrain Snapshotting Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give `JournaledGrain<TState,TEvent>` an optional snapshot mechanism so activation replays only the events after the latest snapshot instead of the entire log from version 0.

**Architecture:** A new dedicated `ISnapshotStore` abstraction (separate from `ILogStorage`/`IGrainStorage`) stores `(version, state)` snapshots. `JournaledGrain` writes a snapshot automatically every N confirmed events (plus a manual hook), and on activation seeds state from the snapshot then replays only the tail. The event log stays the sole source of truth: a missing snapshot triggers a full replay; a present-but-broken snapshot (undeserializable, or version ahead of the log) throws `CorruptSnapshotException` (fail-fast), recoverable via `ClearSnapshotAsync`.

**Tech Stack:** C# / .NET 10, xUnit, Quark serialization deep-copiers (`ICopierProvider`/`IDeepCopier<T>`), `Microsoft.Extensions.DependencyInjection`.

**Spec:** `docs/superpowers/specs/2026-07-10-journaledgrain-snapshotting-design.md`

## Global Constraints

- Target framework: `net10.0`. SDK pinned to `10.0.201` (`global.json`).
- No `Version=` on `<PackageReference>` — versions are centralized in `Directory.Packages.props`.
- AOT/trim safe: prefer source generation over reflection; every production package has `IsTrimmable=true` / `EnableAotAnalyzer=true` and `TreatWarningsAsErrors`. Do not introduce new reflection.
- `Quark.Persistence.Abstractions` holds abstractions only; concrete providers live in `Quark.Persistence.InMemory`.
- The event log is the sole source of truth; a snapshot must never change the replayed result. Missing snapshot → full replay (not an error). Present-but-broken → `CorruptSnapshotException`.
- In test projects the code generators do NOT run — hand-write any `IDeepCopier<T>` a test needs and register it in DI.
- Commit message style: `Component: imperative summary` (match recent history, e.g. `CodecProvider: fix ...`). End every commit body with:
  `Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>`
- Build the whole solution with `dotnet build Quark.slnx`; run the touched unit tests with the filters shown per task.

---

### Task 1: `ISnapshotStore` abstraction + envelope + exception

**Files:**
- Create: `src/Quark.Persistence.Abstractions/Journaling/ISnapshotStore.cs`
- Test: `tests/Quark.Tests.Unit/Journaling/SnapshotEnvelopeTests.cs`

**Interfaces:**
- Produces:
  - `interface ISnapshotStore` with `Task<SnapshotEnvelope<TState>?> ReadSnapshotAsync<TState>(GrainId, CancellationToken) where TState : class`, `Task WriteSnapshotAsync<TState>(GrainId, SnapshotEnvelope<TState>, CancellationToken) where TState : class`, `Task ClearSnapshotAsync(GrainId, CancellationToken)`.
  - `sealed class SnapshotEnvelope<TState>` with ctor `(int version, TState state)`, `int Version { get; }`, `TState State { get; }`.
  - `sealed class CorruptSnapshotException : Exception` with ctor `(GrainId grainId, int snapshotVersion, string message, Exception? inner = null)`, `GrainId GrainId { get; }`, `int SnapshotVersion { get; }`.

- [ ] **Step 1: Write the failing test**

Create `tests/Quark.Tests.Unit/Journaling/SnapshotEnvelopeTests.cs`:

```csharp
using Quark.Core.Abstractions.Identity;
using Quark.Persistence.Abstractions.Journaling;
using Xunit;

namespace Quark.Tests.Unit.Journaling;

public sealed class SnapshotEnvelopeTests
{
    private sealed class State { public int N { get; set; } }

    [Fact]
    public void SnapshotEnvelope_ExposesVersionAndState()
    {
        var s = new State { N = 7 };
        var env = new SnapshotEnvelope<State>(3, s);
        Assert.Equal(3, env.Version);
        Assert.Same(s, env.State);
    }

    [Fact]
    public void CorruptSnapshotException_CarriesGrainIdAndVersion()
    {
        var id = new GrainId(new GrainType("G"), "k");
        var ex = new CorruptSnapshotException(id, 42, "boom");
        Assert.Equal(id, ex.GrainId);
        Assert.Equal(42, ex.SnapshotVersion);
        Assert.Contains("boom", ex.Message);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet build tests/Quark.Tests.Unit/Quark.Tests.Unit.csproj`
Expected: FAIL — `ISnapshotStore` / `SnapshotEnvelope` / `CorruptSnapshotException` do not exist (CS0246).

- [ ] **Step 3: Write minimal implementation**

Create `src/Quark.Persistence.Abstractions/Journaling/ISnapshotStore.cs`:

```csharp
using Quark.Core.Abstractions.Identity;

namespace Quark.Persistence.Abstractions.Journaling;

/// <summary>
///     Optional snapshot store for <see cref="JournaledGrain{TState,TEvent}" />. A snapshot is a
///     replay-shortcut only; the event log remains the source of truth. A missing snapshot is
///     normal (activation full-replays). A present-but-corrupt snapshot must surface as a
///     <see cref="CorruptSnapshotException" /> rather than silently producing wrong state.
/// </summary>
public interface ISnapshotStore
{
    /// <summary>
    ///     Reads the latest snapshot for <paramref name="grainId" />, or <c>null</c> if none exists.
    ///     Durable providers throw <see cref="CorruptSnapshotException" /> when a stored snapshot
    ///     cannot be deserialized into <typeparamref name="TState" />.
    /// </summary>
    Task<SnapshotEnvelope<TState>?> ReadSnapshotAsync<TState>(
        GrainId grainId, CancellationToken ct = default) where TState : class;

    /// <summary>Writes (replaces) the snapshot for <paramref name="grainId" />.</summary>
    Task WriteSnapshotAsync<TState>(
        GrainId grainId, SnapshotEnvelope<TState> snapshot, CancellationToken ct = default)
        where TState : class;

    /// <summary>Deletes any stored snapshot for <paramref name="grainId" /> (recovery path).</summary>
    Task ClearSnapshotAsync(GrainId grainId, CancellationToken ct = default);
}

/// <summary>A point-in-time projection of grain state and the log version it folds up to.</summary>
public sealed class SnapshotEnvelope<TState>
{
    public SnapshotEnvelope(int version, TState state)
    {
        Version = version;
        State = state;
    }

    /// <summary>Number of events folded into <see cref="State" /> — i.e. the index of the next event.</summary>
    public int Version { get; }

    /// <summary>State after applying events <c>[0, Version)</c>.</summary>
    public TState State { get; }
}

/// <summary>Thrown when a present snapshot is unusable (undeserializable or inconsistent with the log).</summary>
public sealed class CorruptSnapshotException : Exception
{
    public CorruptSnapshotException(GrainId grainId, int snapshotVersion, string message, Exception? inner = null)
        : base(message, inner)
    {
        GrainId = grainId;
        SnapshotVersion = snapshotVersion;
    }

    /// <summary>The grain whose snapshot is corrupt.</summary>
    public GrainId GrainId { get; }

    /// <summary>The version stamped on the offending snapshot.</summary>
    public int SnapshotVersion { get; }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Quark.Tests.Unit/Quark.Tests.Unit.csproj --filter "FullyQualifiedName~SnapshotEnvelopeTests"`
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add src/Quark.Persistence.Abstractions/Journaling/ISnapshotStore.cs \
        tests/Quark.Tests.Unit/Journaling/SnapshotEnvelopeTests.cs
git commit -m "$(cat <<'EOF'
ISnapshotStore: add snapshot-store abstraction for JournaledGrain

Introduces ISnapshotStore, SnapshotEnvelope<TState>, and
CorruptSnapshotException in Quark.Persistence.Abstractions.Journaling as
the foundation for JournaledGrain log snapshotting (#144).

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 2: `InMemorySnapshotStore` + DI registration

**Files:**
- Create: `src/Quark.Persistence.InMemory/InMemorySnapshotStore.cs`
- Create: `src/Quark.Persistence.InMemory/InMemorySnapshotStoreServiceCollectionExtensions.cs`
- Test: `tests/Quark.Tests.Unit/Journaling/InMemorySnapshotStoreTests.cs`

**Interfaces:**
- Consumes: `ISnapshotStore`, `SnapshotEnvelope<TState>` (Task 1); `ICopierProvider`/`IDeepCopier<T>`/`CopyContext` (`Quark.Serialization.Abstractions.Abstractions`).
- Produces:
  - `sealed class InMemorySnapshotStore : ISnapshotStore` with ctor `(ICopierProvider copiers)`.
  - `static class InMemorySnapshotStoreServiceCollectionExtensions` with `IServiceCollection AddInMemorySnapshotStore(this IServiceCollection services)`.

- [ ] **Step 1: Write the failing test**

Create `tests/Quark.Tests.Unit/Journaling/InMemorySnapshotStoreTests.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Quark.Core.Abstractions.Identity;
using Quark.Persistence.Abstractions.Journaling;
using Quark.Persistence.InMemory;
using Quark.Serialization;
using Quark.Serialization.Abstractions.Abstractions;
using Xunit;

namespace Quark.Tests.Unit.Journaling;

public sealed class InMemorySnapshotStoreTests
{
    // Snapshotted state needs a deep copier. Generators don't run in test projects, so hand-write one.
    public sealed class Bag
    {
        public int N { get; set; }
        public List<string> Items { get; set; } = [];
    }

    private sealed class BagCopier : IDeepCopier<Bag>
    {
        public Bag DeepCopy(Bag original, CopyContext context) =>
            new() { N = original.N, Items = [.. original.Items] };
    }

    private static (InMemorySnapshotStore Store, GrainId Id) NewStore()
    {
        var services = new ServiceCollection();
        services.AddQuarkSerialization();
        services.AddSingleton<IDeepCopier<Bag>>(new BagCopier());
        var sp = services.BuildServiceProvider();
        var store = new InMemorySnapshotStore(sp.GetRequiredService<ICopierProvider>());
        return (store, new GrainId(new GrainType("G"), "k"));
    }

    [Fact]
    public async Task ReadSnapshotAsync_ReturnsNull_WhenMissing()
    {
        (InMemorySnapshotStore store, GrainId id) = NewStore();
        Assert.Null(await store.ReadSnapshotAsync<Bag>(id));
    }

    [Fact]
    public async Task WriteThenRead_RoundTripsVersionAndState()
    {
        (InMemorySnapshotStore store, GrainId id) = NewStore();
        await store.WriteSnapshotAsync(id, new SnapshotEnvelope<Bag>(5, new Bag { N = 9, Items = ["a"] }));

        SnapshotEnvelope<Bag>? read = await store.ReadSnapshotAsync<Bag>(id);
        Assert.NotNull(read);
        Assert.Equal(5, read!.Version);
        Assert.Equal(9, read.State.N);
        Assert.Equal(new[] { "a" }, read.State.Items);
    }

    [Fact]
    public async Task Write_IsolatesFromLaterMutationOfOriginal()
    {
        (InMemorySnapshotStore store, GrainId id) = NewStore();
        var live = new Bag { N = 1, Items = ["x"] };
        await store.WriteSnapshotAsync(id, new SnapshotEnvelope<Bag>(1, live));

        live.N = 99;            // mutate the live state after the snapshot was taken
        live.Items.Add("y");

        SnapshotEnvelope<Bag>? read = await store.ReadSnapshotAsync<Bag>(id);
        Assert.Equal(1, read!.State.N);
        Assert.Equal(new[] { "x" }, read.State.Items);
    }

    [Fact]
    public async Task Read_IsolatesStoredCopyFromCallerMutation()
    {
        (InMemorySnapshotStore store, GrainId id) = NewStore();
        await store.WriteSnapshotAsync(id, new SnapshotEnvelope<Bag>(1, new Bag { N = 1, Items = ["x"] }));

        SnapshotEnvelope<Bag>? first = await store.ReadSnapshotAsync<Bag>(id);
        first!.State.N = 42;               // caller mutates the returned copy
        first.State.Items.Add("z");

        SnapshotEnvelope<Bag>? second = await store.ReadSnapshotAsync<Bag>(id);
        Assert.Equal(1, second!.State.N);
        Assert.Equal(new[] { "x" }, second.State.Items);
    }

    [Fact]
    public async Task ClearSnapshotAsync_RemovesSnapshot()
    {
        (InMemorySnapshotStore store, GrainId id) = NewStore();
        await store.WriteSnapshotAsync(id, new SnapshotEnvelope<Bag>(1, new Bag { N = 1 }));
        await store.ClearSnapshotAsync(id);
        Assert.Null(await store.ReadSnapshotAsync<Bag>(id));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet build tests/Quark.Tests.Unit/Quark.Tests.Unit.csproj`
Expected: FAIL — `InMemorySnapshotStore` does not exist (CS0246).

- [ ] **Step 3: Write minimal implementation**

Create `src/Quark.Persistence.InMemory/InMemorySnapshotStore.cs`:

```csharp
using System.Collections.Concurrent;
using Quark.Core.Abstractions.Identity;
using Quark.Persistence.Abstractions.Journaling;
using Quark.Serialization.Abstractions.Abstractions;

namespace Quark.Persistence.InMemory;

/// <summary>
///     In-memory <see cref="ISnapshotStore" /> for development and tests. State is deep-copied on
///     both write and read to isolate the stored snapshot from the grain's live, still-mutating
///     state (the same isolation <see cref="InMemoryGrainStorage" /> applies). Not durable across
///     process restarts, so it never produces the undeserializable-snapshot failure mode.
/// </summary>
public sealed class InMemorySnapshotStore : ISnapshotStore
{
    private readonly ConcurrentDictionary<GrainId, (int Version, object State)> _snapshots = new();
    private readonly ICopierProvider _copiers;

    /// <summary>Initializes the in-memory snapshot store.</summary>
    public InMemorySnapshotStore(ICopierProvider copiers) => _copiers = copiers;

    /// <inheritdoc />
    public Task WriteSnapshotAsync<TState>(
        GrainId grainId, SnapshotEnvelope<TState> snapshot, CancellationToken ct = default)
        where TState : class
    {
        ct.ThrowIfCancellationRequested();
        TState isolated = _copiers.GetRequiredCopier<TState>().DeepCopy(snapshot.State, new CopyContext());
        _snapshots[grainId] = (snapshot.Version, isolated);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<SnapshotEnvelope<TState>?> ReadSnapshotAsync<TState>(
        GrainId grainId, CancellationToken ct = default)
        where TState : class
    {
        ct.ThrowIfCancellationRequested();
        if (!_snapshots.TryGetValue(grainId, out (int Version, object State) entry))
            return Task.FromResult<SnapshotEnvelope<TState>?>(null);

        TState copy = _copiers.GetRequiredCopier<TState>().DeepCopy((TState)entry.State, new CopyContext());
        return Task.FromResult<SnapshotEnvelope<TState>?>(new SnapshotEnvelope<TState>(entry.Version, copy));
    }

    /// <inheritdoc />
    public Task ClearSnapshotAsync(GrainId grainId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _snapshots.TryRemove(grainId, out _);
        return Task.CompletedTask;
    }
}
```

Create `src/Quark.Persistence.InMemory/InMemorySnapshotStoreServiceCollectionExtensions.cs`:

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Quark.Persistence.Abstractions.Journaling;

namespace Quark.Persistence.InMemory;

/// <summary>Service registration helpers for the in-memory snapshot store.</summary>
public static class InMemorySnapshotStoreServiceCollectionExtensions
{
    /// <summary>
    ///     Registers the in-memory <see cref="ISnapshotStore" />. Once registered, every
    ///     <see cref="JournaledGrain{TState,TEvent}" /> with a positive <c>SnapshotInterval</c>
    ///     writes snapshots and replays only post-snapshot events on activation.
    /// </summary>
    public static IServiceCollection AddInMemorySnapshotStore(this IServiceCollection services)
    {
        services.TryAddSingleton<ISnapshotStore, InMemorySnapshotStore>();
        return services;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Quark.Tests.Unit/Quark.Tests.Unit.csproj --filter "FullyQualifiedName~InMemorySnapshotStoreTests"`
Expected: PASS (5 tests).

- [ ] **Step 5: Commit**

```bash
git add src/Quark.Persistence.InMemory/InMemorySnapshotStore.cs \
        src/Quark.Persistence.InMemory/InMemorySnapshotStoreServiceCollectionExtensions.cs \
        tests/Quark.Tests.Unit/Journaling/InMemorySnapshotStoreTests.cs
git commit -m "$(cat <<'EOF'
InMemorySnapshotStore: add in-memory ISnapshotStore provider

Deep-copies state on write and read (via ICopierProvider) to isolate the
stored snapshot from the grain's live state, matching InMemoryGrainStorage
isolation. Adds AddInMemorySnapshotStore() DI helper. (#144)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 3: `JournaledGrain` write path — auto snapshot every N events + manual hook

**Files:**
- Modify: `src/Quark.Persistence.Abstractions/Journaling/JournaledGrainState.cs`
- Modify: `src/Quark.Persistence.Abstractions/Journaling/JournaledGrain.cs`
- Test: `tests/Quark.Tests.Unit/Journaling/JournaledGrainSnapshotTests.cs`

**Interfaces:**
- Consumes: `ISnapshotStore`, `SnapshotEnvelope<TState>` (Task 1).
- Produces (for Task 4 and tests):
  - `JournaledGrain` ctor gains 4th optional param `ISnapshotStore? snapshotStore = null`.
  - `protected virtual int SnapshotInterval => 100;` (0 disables).
  - `protected Task WriteSnapshotAsync(CancellationToken cancellationToken = default)` — manual snapshot; no-op when no store.
  - `JournaledGrainState<TState,TEvent>` gains `int LastSnapshotVersion { get; set; }`.
  - Test helpers in the new test file: `sealed class FakeSnapshotStore : ISnapshotStore` (records writes in `List<(GrainId Id, int Version)> Writes`, `Seed<TState>(GrainId, SnapshotEnvelope<TState>)`, optional `Func<GrainId, Exception?>? ReadThrows`); `sealed class CounterState { public int Count { get; set; } }`; `abstract record CounterEvent` + `sealed record Bumped : CounterEvent`; `sealed class CounterGrain : JournaledGrain<CounterState, CounterEvent>`; `FixedCallContext`; `ActivateAsync(...)` helper.

- [ ] **Step 1: Write the failing test**

Create `tests/Quark.Tests.Unit/Journaling/JournaledGrainSnapshotTests.cs`:

```csharp
using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;
using Quark.Persistence.Abstractions;
using Quark.Persistence.Abstractions.Journaling;
using Quark.Persistence.InMemory;
using Xunit;

namespace Quark.Tests.Unit.Journaling;

public sealed class JournaledGrainSnapshotTests
{
    // ---- Write-path tests (Task 3) ----

    [Fact]
    public async Task ConfirmEvents_WritesSnapshot_WhenIntervalReached()
    {
        var snap = new FakeSnapshotStore();
        CounterGrain g = await ActivateAsync(new InMemoryLogStorage(), snap, interval: 3, NewId());

        g.Bump(); g.Bump(); g.Bump();
        await g.SaveAsync();                       // ConfirmedVersion 0 -> 3

        Assert.Single(snap.Writes);
        Assert.Equal(3, snap.Writes[0].Version);
    }

    [Fact]
    public async Task ConfirmEvents_DoesNotSnapshot_BelowInterval()
    {
        var snap = new FakeSnapshotStore();
        CounterGrain g = await ActivateAsync(new InMemoryLogStorage(), snap, interval: 3, NewId());

        g.Bump(); g.Bump();
        await g.SaveAsync();                       // ConfirmedVersion 0 -> 2

        Assert.Empty(snap.Writes);
    }

    [Fact]
    public async Task SnapshotInterval_Zero_DisablesAutoSnapshot()
    {
        var snap = new FakeSnapshotStore();
        CounterGrain g = await ActivateAsync(new InMemoryLogStorage(), snap, interval: 0, NewId());

        for (int i = 0; i < 5; i++) g.Bump();
        await g.SaveAsync();

        Assert.Empty(snap.Writes);
    }

    [Fact]
    public async Task WriteSnapshotAsync_Manual_WritesAtCurrentVersion()
    {
        var snap = new FakeSnapshotStore();
        CounterGrain g = await ActivateAsync(new InMemoryLogStorage(), snap, interval: 0, NewId());

        g.Bump(); g.Bump();
        await g.SaveAsync();                       // version 2, no auto snapshot (interval 0)
        await g.SnapshotNowAsync();

        Assert.Single(snap.Writes);
        Assert.Equal(2, snap.Writes[0].Version);
    }

    [Fact]
    public async Task WriteSnapshotAsync_NoStore_IsNoOp()
    {
        CounterGrain g = await ActivateAsync(new InMemoryLogStorage(), snapshotStore: null, interval: 3, NewId());
        g.Bump();
        await g.SaveAsync();
        await g.SnapshotNowAsync();                // must not throw
        Assert.Equal(1, g.Version);
    }

    // ---- Shared helpers ----

    private static GrainId NewId() => new(new GrainType("CounterGrain"), Guid.NewGuid().ToString("N"));

    private static async Task<CounterGrain> ActivateAsync(
        ILogStorage? log, ISnapshotStore? snapshotStore, int interval, GrainId id)
    {
        var holder = new StateHolder<JournaledGrainState<CounterState, CounterEvent>>();
        var memory = new ActivationMemoryAccessor<JournaledGrainState<CounterState, CounterEvent>>(holder);
        var grain = new CounterGrain(memory, new FixedCallContext(id), log, snapshotStore, interval);
        await grain.OnActivateAsync(CancellationToken.None);
        return grain;
    }

    public sealed class CounterState { public int Count { get; set; } }

    public abstract record CounterEvent;
    public sealed record Bumped : CounterEvent;

    public sealed class CounterGrain : JournaledGrain<CounterState, CounterEvent>
    {
        private readonly int _interval;

        public CounterGrain(
            IActivationMemory<JournaledGrainState<CounterState, CounterEvent>> memory,
            ICallContext ctx,
            ILogStorage? log,
            ISnapshotStore? snapshotStore,
            int interval)
            : base(memory, ctx, log, snapshotStore)
            => _interval = interval;

        protected override int SnapshotInterval => _interval;

        public new CounterState State => base.State;
        public new int Version => base.Version;

        public void Bump() => RaiseEvent(new Bumped());
        public Task SaveAsync() => ConfirmEventsAsync();
        public Task SnapshotNowAsync() => WriteSnapshotAsync();

        protected override void TransitionState(CounterState state, CounterEvent @event) => state.Count++;
    }

    private sealed class FixedCallContext(GrainId grainId) : ICallContext
    {
        public GrainId GrainId => grainId;
    }

    private sealed class FakeSnapshotStore : ISnapshotStore
    {
        private readonly Dictionary<GrainId, object> _snaps = [];
        public List<(GrainId Id, int Version)> Writes { get; } = [];
        public Func<GrainId, Exception?>? ReadThrows { get; set; }

        public void Seed<TState>(GrainId id, SnapshotEnvelope<TState> snap) where TState : class
            => _snaps[id] = snap;

        public Task<SnapshotEnvelope<TState>?> ReadSnapshotAsync<TState>(
            GrainId grainId, CancellationToken ct = default) where TState : class
        {
            if (ReadThrows?.Invoke(grainId) is { } ex) throw ex;
            return Task.FromResult(_snaps.TryGetValue(grainId, out object? s)
                ? (SnapshotEnvelope<TState>?)s
                : null);
        }

        public Task WriteSnapshotAsync<TState>(
            GrainId grainId, SnapshotEnvelope<TState> snapshot, CancellationToken ct = default)
            where TState : class
        {
            Writes.Add((grainId, snapshot.Version));
            _snaps[grainId] = snapshot;
            return Task.CompletedTask;
        }

        public Task ClearSnapshotAsync(GrainId grainId, CancellationToken ct = default)
        {
            _snaps.Remove(grainId);
            return Task.CompletedTask;
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet build tests/Quark.Tests.Unit/Quark.Tests.Unit.csproj`
Expected: FAIL — `JournaledGrain` has no 4-arg ctor / no `SnapshotInterval` / no `WriteSnapshotAsync`; `JournaledGrainState` has no `LastSnapshotVersion` (CS1729 / CS1061).

- [ ] **Step 3: Write minimal implementation**

In `src/Quark.Persistence.Abstractions/Journaling/JournaledGrainState.cs`, add one property to the class body:

```csharp
    /// <summary>The <see cref="ConfirmedVersion" /> captured by the most recent snapshot write.</summary>
    public int LastSnapshotVersion { get; set; }
```

In `src/Quark.Persistence.Abstractions/Journaling/JournaledGrain.cs`:

Add the field next to `_logStorage`:

```csharp
    private ISnapshotStore? _snapshotStore;
```

Replace the constructor with:

```csharp
    protected JournaledGrain(
        IActivationMemory<JournaledGrainState<TState, TEvent>> memory,
        ICallContext ctx,
        ILogStorage? logStorage = null,
        ISnapshotStore? snapshotStore = null)
    {
        _memory = memory;
        _ctx = ctx;
        _logStorage = logStorage;
        _snapshotStore = snapshotStore;
    }
```

Add the cadence property (place it near the other `protected` members, e.g. after `State`):

```csharp
    /// <summary>
    ///     Number of confirmed events between automatic snapshots. Override per grain type.
    ///     <c>0</c> disables automatic snapshotting for this grain type. Default: 100.
    ///     Automatic snapshots require a registered <see cref="ISnapshotStore" />.
    /// </summary>
    protected virtual int SnapshotInterval => 100;
```

In `ConfirmEventsAsync`, after `st.StagedEvents.Clear();`, append:

```csharp
        if (_snapshotStore is not null && SnapshotInterval > 0 &&
            st.ConfirmedVersion - st.LastSnapshotVersion >= SnapshotInterval)
        {
            await WriteSnapshotCoreAsync(cancellationToken).ConfigureAwait(false);
        }
```

Add the two snapshot-write methods (e.g. right after `ConfirmEventsAsync`):

```csharp
    /// <summary>
    ///     Writes a snapshot of the current confirmed state to the registered
    ///     <see cref="ISnapshotStore" />. No-op when no snapshot store is registered.
    /// </summary>
    protected Task WriteSnapshotAsync(CancellationToken cancellationToken = default) =>
        _snapshotStore is null ? Task.CompletedTask : WriteSnapshotCoreAsync(cancellationToken);

    private async Task WriteSnapshotCoreAsync(CancellationToken ct)
    {
        JournaledGrainState<TState, TEvent> st = _memory.Value;
        await _snapshotStore!
            .WriteSnapshotAsync(GrainId, new SnapshotEnvelope<TState>(st.ConfirmedVersion, st.State), ct)
            .ConfigureAwait(false);
        st.LastSnapshotVersion = st.ConfirmedVersion;
    }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Quark.Tests.Unit/Quark.Tests.Unit.csproj --filter "FullyQualifiedName~JournaledGrainSnapshotTests"`
Expected: PASS (5 tests).

- [ ] **Step 5: Run the existing JournaledGrain tests to confirm no regression**

Run: `dotnet test tests/Quark.Tests.Unit/Quark.Tests.Unit.csproj --filter "FullyQualifiedName~JournaledGrainTests"`
Expected: PASS (existing 6 tests — the new optional ctor param is backward-compatible).

- [ ] **Step 6: Commit**

```bash
git add src/Quark.Persistence.Abstractions/Journaling/JournaledGrain.cs \
        src/Quark.Persistence.Abstractions/Journaling/JournaledGrainState.cs \
        tests/Quark.Tests.Unit/Journaling/JournaledGrainSnapshotTests.cs
git commit -m "$(cat <<'EOF'
JournaledGrain: write snapshots every N confirmed events + manual hook

Adds an optional ISnapshotStore ctor dependency, a per-type SnapshotInterval
(default 100, 0 disables), and a protected WriteSnapshotAsync() hook.
ConfirmEventsAsync writes a snapshot once ConfirmedVersion advances a full
interval past the last snapshot. (#144)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 4: `JournaledGrain` activation path — snapshot-aware replay + fail-fast + recovery

**Files:**
- Modify: `src/Quark.Persistence.Abstractions/Journaling/JournaledGrain.cs` (`ReloadFromLogAsync`)
- Test: `tests/Quark.Tests.Unit/Journaling/JournaledGrainSnapshotTests.cs` (add activation tests + `SpyLogStorage`)

**Interfaces:**
- Consumes: everything from Task 3, plus `LogEntry` / `ILogStorage` (`Quark.Persistence.Abstractions.Journaling`).
- Produces: snapshot-aware `ReloadFromLogAsync`; a `SpyLogStorage` test decorator recording `(int From, int To)` reads.

- [ ] **Step 1: Write the failing test**

Append these members to the existing `JournaledGrainSnapshotTests` class (before the `// ---- Shared helpers ----` marker):

```csharp
    // ---- Activation-path tests (Task 4) ----

    [Fact]
    public async Task Activation_WithSnapshot_ReplaysOnlyTail()
    {
        var log = new InMemoryLogStorage();
        var snap = new FakeSnapshotStore();
        GrainId id = NewId();

        // Seed 5 confirmed events (interval 0 → no auto snapshot to keep the log clean).
        CounterGrain seed = await ActivateAsync(log, snap, interval: 0, id);
        for (int i = 0; i < 5; i++) seed.Bump();
        await seed.SaveAsync();

        // Hand-seed a snapshot at version 3 (Count == 3).
        snap.Seed(id, new SnapshotEnvelope<CounterState>(3, new CounterState { Count = 3 }));

        var spy = new SpyLogStorage(log);
        CounterGrain reactivated = await ActivateAsync(spy, snap, interval: 0, id);

        Assert.Equal(5, reactivated.Version);
        Assert.Equal(5, reactivated.State.Count);
        // Boundary probe reads from snapshot.Version - 1 (== 2), NOT from 0.
        Assert.Equal(2, spy.Reads[0].From);
    }

    [Fact]
    public async Task Activation_NoSnapshot_FullReplayFromZero()
    {
        var log = new InMemoryLogStorage();
        var snap = new FakeSnapshotStore();
        GrainId id = NewId();

        CounterGrain seed = await ActivateAsync(log, snap, interval: 0, id);
        for (int i = 0; i < 4; i++) seed.Bump();
        await seed.SaveAsync();

        var spy = new SpyLogStorage(log);
        CounterGrain reactivated = await ActivateAsync(spy, snap, interval: 0, id); // no snapshot seeded

        Assert.Equal(4, reactivated.State.Count);
        Assert.Equal(0, spy.Reads[0].From);   // full replay from 0
    }

    [Fact]
    public async Task Activation_SnapshotAheadOfLog_Throws()
    {
        var log = new InMemoryLogStorage();
        var snap = new FakeSnapshotStore();
        GrainId id = NewId();

        CounterGrain seed = await ActivateAsync(log, snap, interval: 0, id);
        seed.Bump(); seed.Bump(); seed.Bump();
        await seed.SaveAsync();                                   // log has 3 entries

        snap.Seed(id, new SnapshotEnvelope<CounterState>(5, new CounterState { Count = 5 })); // ahead of log

        CorruptSnapshotException ex = await Assert.ThrowsAsync<CorruptSnapshotException>(
            () => ActivateAsync(log, snap, interval: 0, id));
        Assert.Equal(id, ex.GrainId);
        Assert.Equal(5, ex.SnapshotVersion);
    }

    [Fact]
    public async Task Activation_StoreThrowsCorrupt_Propagates()
    {
        var log = new InMemoryLogStorage();
        var snap = new FakeSnapshotStore();
        GrainId id = NewId();
        snap.ReadThrows = gid => new CorruptSnapshotException(gid, 1, "undeserializable");

        await Assert.ThrowsAsync<CorruptSnapshotException>(
            () => ActivateAsync(log, snap, interval: 0, id));
    }

    [Fact]
    public async Task Activation_AfterClear_RecoversViaFullReplay()
    {
        var log = new InMemoryLogStorage();
        var snap = new FakeSnapshotStore();
        GrainId id = NewId();

        CounterGrain seed = await ActivateAsync(log, snap, interval: 0, id);
        seed.Bump(); seed.Bump(); seed.Bump();
        await seed.SaveAsync();                                   // log has 3 entries
        snap.Seed(id, new SnapshotEnvelope<CounterState>(5, new CounterState { Count = 5 }));

        await Assert.ThrowsAsync<CorruptSnapshotException>(
            () => ActivateAsync(log, snap, interval: 0, id));     // bricked

        await snap.ClearSnapshotAsync(id);                       // recovery

        CounterGrain recovered = await ActivateAsync(log, snap, interval: 0, id);
        Assert.Equal(3, recovered.Version);
        Assert.Equal(3, recovered.State.Count);
    }
```

Also add the `SpyLogStorage` decorator inside the test class (e.g. after `FakeSnapshotStore`):

```csharp
    private sealed class SpyLogStorage(ILogStorage inner) : ILogStorage
    {
        public List<(int From, int To)> Reads { get; } = [];

        public Task<IReadOnlyList<LogEntry>> ReadEntriesAsync(
            GrainId grainId, int fromVersion, int toVersion, CancellationToken ct = default)
        {
            Reads.Add((fromVersion, toVersion));
            return inner.ReadEntriesAsync(grainId, fromVersion, toVersion, ct);
        }

        public Task AppendEntriesAsync(
            GrainId grainId, int expectedVersion, IReadOnlyList<LogEntry> entries, CancellationToken ct = default)
            => inner.AppendEntriesAsync(grainId, expectedVersion, entries, ct);
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Quark.Tests.Unit/Quark.Tests.Unit.csproj --filter "FullyQualifiedName~JournaledGrainSnapshotTests"`
Expected: FAIL — `Activation_WithSnapshot_ReplaysOnlyTail` and the other new tests fail (current `ReloadFromLogAsync` ignores snapshots: it always reads from 0 and never throws).

- [ ] **Step 3: Write minimal implementation**

Replace `ReloadFromLogAsync` in `src/Quark.Persistence.Abstractions/Journaling/JournaledGrain.cs` with:

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

            if (snap is not null && snap.Version > 0)
            {
                // Boundary probe: read from snap.Version - 1 so we can confirm the log actually
                // contains >= snap.Version contiguous entries WITHOUT a length API on ILogStorage
                // (AppendEntriesAsync guarantees version == index, so entry[V-1] existing => 0..V-1 exist).
                IReadOnlyList<LogEntry> tail = await _logStorage!
                    .ReadEntriesAsync(GrainId, snap.Version - 1, int.MaxValue, ct).ConfigureAwait(false);

                if (tail.Count == 0 || tail[0].Version != snap.Version - 1)
                    throw new CorruptSnapshotException(GrainId, snap.Version,
                        $"Snapshot version {snap.Version} is ahead of the event log for grain {GrainId}.");

                st.State = snap.State;                 // store returned an isolated copy
                st.ConfirmedVersion = snap.Version;
                st.LastSnapshotVersion = snap.Version;

                for (int i = 1; i < tail.Count; i++)   // skip the boundary entry (already in the snapshot)
                {
                    TransitionState(st.State, (TEvent)tail[i].Event);
                    st.ConfirmedVersion = tail[i].Version + 1;
                }

                return;
            }
        }

        // No usable snapshot: full replay from 0 (original behavior).
        IReadOnlyList<LogEntry> all =
            await _logStorage!.ReadEntriesAsync(GrainId, 0, int.MaxValue, ct).ConfigureAwait(false);
        foreach (LogEntry entry in all)
        {
            TransitionState(st.State, (TEvent)entry.Event);
            st.ConfirmedVersion = entry.Version + 1;
        }
    }
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test tests/Quark.Tests.Unit/Quark.Tests.Unit.csproj --filter "FullyQualifiedName~JournaledGrainSnapshotTests"`
Expected: PASS (10 tests total — 5 write-path + 5 activation-path).

- [ ] **Step 5: Run full unit suite for the persistence area**

Run: `dotnet test tests/Quark.Tests.Unit/Quark.Tests.Unit.csproj --filter "FullyQualifiedName~Journaling"`
Expected: PASS (all Journaling tests: `SnapshotEnvelopeTests`, `InMemorySnapshotStoreTests`, `JournaledGrainTests`, `JournaledGrainSnapshotTests`).

- [ ] **Step 6: Commit**

```bash
git add src/Quark.Persistence.Abstractions/Journaling/JournaledGrain.cs \
        tests/Quark.Tests.Unit/Journaling/JournaledGrainSnapshotTests.cs
git commit -m "$(cat <<'EOF'
JournaledGrain: seed activation from snapshot, replay only the tail

On activation, reads the latest snapshot and replays only events after its
version. A missing snapshot full-replays from 0 (unchanged). A snapshot whose
version is ahead of the log throws CorruptSnapshotException (fail-fast);
recovery is via ISnapshotStore.ClearSnapshotAsync. (#144)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 5: Bank sample — showcase snapshotting on the ledger

**Files:**
- Modify: `samples/Persistence/Bank.Grains/LedgerState.cs`
- Modify: `samples/Persistence/Bank.Grains/BankStateCopiers.cs`
- Modify: `samples/Persistence/Bank.Grains/LedgerBehavior.cs`
- Modify: `samples/Persistence/Bank.Server/Program.cs`

**Interfaces:**
- Consumes: `ISnapshotStore` (Task 1), `AddInMemorySnapshotStore` (Task 2), `JournaledGrain` snapshot API (Tasks 3-4).
- Produces: a runnable sample where the ledger snapshots every 5 events. (No automated test — samples are verified by building; the behavior is covered by Task 4's unit tests.)

- [ ] **Step 1: Make `LedgerState` serializable so it can be snapshotted**

Replace `samples/Persistence/Bank.Grains/LedgerState.cs` with:

```csharp
using Quark.Serialization.Abstractions.Attributes;

namespace Bank.Grains;

/// <summary>
///     Projection for <see cref="LedgerBehavior" />, rebuilt by replaying <see cref="LedgerEvent" />s.
///     <c>[GenerateSerializer]</c> lets the in-memory <c>ISnapshotStore</c> deep-copy it so activation
///     can replay only post-snapshot events instead of the whole log.
/// </summary>
[GenerateSerializer]
public sealed class LedgerState
{
    [Id(0)] public decimal Balance { get; set; }
    [Id(1)] public List<string> History { get; set; } = [];
}

/// <summary>Base type for ledger events. Events are the source of truth, persisted to the log.</summary>
public abstract record LedgerEvent;

/// <summary>Money paid into the ledger.</summary>
public sealed record Credited(decimal Amount, string Note) : LedgerEvent;

/// <summary>Money paid out of the ledger.</summary>
public sealed record Debited(decimal Amount, string Note) : LedgerEvent;
```

- [ ] **Step 2: Register the generated `IDeepCopier<LedgerState>`**

In `samples/Persistence/Bank.Grains/BankStateCopiers.cs`, add a registration line inside `AddBankStateCopiers`, after the `ProfileState` line:

```csharp
        services.AddSingleton<IDeepCopier<LedgerState>>(
            sp => new LedgerStateCopier(sp.GetRequiredService<ICopierProvider>()));
```

(`LedgerStateCopier` is emitted by the code generator for the `[GenerateSerializer]` type, exactly like `AccountStateCopier`/`ProfileStateCopier`.)

- [ ] **Step 3: Forward `ISnapshotStore` and set a small interval in `LedgerBehavior`**

In `samples/Persistence/Bank.Grains/LedgerBehavior.cs`, update the constructor and add an interval override.

Change the constructor to accept and forward a snapshot store:

```csharp
    public LedgerBehavior(
        IActivationMemory<JournaledGrainState<LedgerState, LedgerEvent>> memory,
        ICallContext ctx,
        ILogStorage? log = null,
        ISnapshotStore? snapshot = null)
        : base(memory, ctx, log, snapshot) { }

    // Snapshot every 5 confirmed events so long-lived ledgers don't replay their whole history.
    protected override int SnapshotInterval => 5;
```

- [ ] **Step 4: Register the snapshot store in the silo**

In `samples/Persistence/Bank.Server/Program.cs`, immediately after the existing
`silo.Services.AddSingleton<ILogStorage, InMemoryLogStorage>();` line, add:

```csharp
        // Snapshot store — lets the JournaledGrain ledger replay only post-snapshot events.
        silo.Services.AddInMemorySnapshotStore();
```

- [ ] **Step 5: Build the sample to verify it compiles**

Run: `dotnet build samples/Persistence/Bank.Server/Bank.Server.csproj`
Expected: BUILD SUCCEEDED (the generator emits `LedgerStateCopier`; the behavior forwards the snapshot store).

- [ ] **Step 6: Commit**

```bash
git add samples/Persistence/Bank.Grains/LedgerState.cs \
        samples/Persistence/Bank.Grains/BankStateCopiers.cs \
        samples/Persistence/Bank.Grains/LedgerBehavior.cs \
        samples/Persistence/Bank.Server/Program.cs
git commit -m "$(cat <<'EOF'
Bank sample: snapshot the JournaledGrain ledger every 5 events

Makes LedgerState [GenerateSerializer], registers its deep copier, forwards
ISnapshotStore into LedgerBehavior with SnapshotInterval=5, and wires
AddInMemorySnapshotStore() in the silo. (#144)

Co-Authored-By: Claude Opus 4.8 (1M context) <noreply@anthropic.com>
EOF
)"
```

---

### Task 6: Full-solution verification

**Files:** none (verification only).

- [ ] **Step 1: Build the whole solution**

Run: `dotnet build Quark.slnx`
Expected: BUILD SUCCEEDED, no new warnings.

- [ ] **Step 2: Run the full unit-test project**

Run: `dotnet test tests/Quark.Tests.Unit/Quark.Tests.Unit.csproj`
Expected: PASS (pre-existing timing-flaky tests may need an isolated re-run; the Journaling tests must be green).

- [ ] **Step 3: AOT publish smoke test (trim/AOT safety)**

Run: `dotnet publish src/Quark.Runtime/Quark.Runtime.csproj -f net10.0 -c Release -r linux-x64 /p:PublishAot=true`
Expected: publish succeeds with no new trim/AOT warnings.

- [ ] **Step 4: Final confirmation**

Confirm all six task commits are present (`git log --oneline -6`) and the working tree is clean (`git status`). The last snapshotting commit references `#144`; do not push unless the user asks.

---

## Notes / follow-ups (out of scope — see spec §9)

- `RedisSnapshotStore` (serializing `TState` via `QuarkSerializer`) — this is where the undeserializable-snapshot `CorruptSnapshotException` path gets exercised.
- Durable Redis `ILogStorage` — a durable snapshot paired with the InMemory-only log is only half-durable.
- Optional silo-level `SnapshotOptions` default interval if per-type overrides prove insufficient.

File a paired follow-up issue for the two durable-provider items after this plan lands.
