# Design: Persistent (recoverable) streams with durable PubSubStore
**Issue:** #41
**Date:** 2026-07-02
**Status:** Draft — ready for implementation

---

## Goals / Non-goals

### Goals
- Durable subscription registry (`PubSubStore`) so grain subscriptions survive silo restart — the Orleans parity gap called out in #41.
- Rewind: a recovered or late subscriber can resume delivery from a `StreamSequenceToken`.
- Define an explicit **durable event-log / replay provider contract** (append, read-from-token, subscription registry) that broker-backed providers (#92 Redis Streams, #106 Kafka — specced in `2026-07-02-broker-stream-providers-design.md`) can implement.
- Cold rehydration: after restart, re-activate implicitly-subscribed grains even when no new item has been published yet.
- Stay AOT/trim-safe: explicit registration, codegen/registered codecs, no reflection scanning.
- Keep the in-memory provider source-compatible (it degrades to start-from-now, no persistence).

### Non-goals
- Stream **delivery mode / backpressure / per-subscriber failure isolation** — owned by roadmap issue **#63** (`StreamSubscriptionRegistry.PublishAsync` is sequential-await + `AggregateException` today, `src/Quark.Streaming.InMemory/StreamSubscriptionRegistry.cs:96-114`). This spec must not redefine fan-out semantics; it consumes whatever #63 lands.
- Concrete broker wire protocols (#92/#106). This spec only fixes the contract they implement.
- Exactly-once delivery. The durable log gives at-least-once replay from a token; dedup is the subscriber's concern (tokens are monotonic, so subscribers can discard `token <= lastSeen`).
- Extending `IGrainContext` (stale/unwired, #78) or `ICallContext` (exposes only `GrainId`, verified `ICallContext.cs`). Rehydration uses `IImplicitStreamActivator`, not context.

---

## Proposed API

### 1. Durable contracts (land in `Quark.Streaming.Abstractions` — interfaces + DTOs only)

```csharp
namespace Quark.Streaming.Abstractions;

/// <summary>Durable subscription registry. Survives silo restart. Backed by IGrainStorage
/// (in-memory / Redis / AdoNet) in the default provider; brokers may back it natively.</summary>
public interface IStreamPubSubStore
{
    ValueTask<Guid> RegisterSubscriptionAsync(
        StreamId streamId, GrainId subscriber, StreamSequenceToken? from, CancellationToken ct = default);

    ValueTask UnregisterSubscriptionAsync(
        StreamId streamId, Guid subscriptionId, CancellationToken ct = default);

    ValueTask<IReadOnlyList<StreamSubscription>> GetSubscriptionsAsync(
        StreamId streamId, CancellationToken ct = default);

    /// <summary>Enumerate every stream that currently has ≥1 subscription. Drives cold rehydration.
    /// IGrainStorage has no enumeration primitive, so implementations MUST maintain a directory index
    /// (see Resolved decision: subscription index).</summary>
    ValueTask<IReadOnlyList<StreamId>> GetActiveStreamsAsync(CancellationToken ct = default);
}

/// <summary>Public projection of a durable subscription. Plain record (not persisted as-is).</summary>
public sealed record StreamSubscription(
    Guid Id, StreamId StreamId, GrainId Subscriber, StreamSequenceToken? Token);

/// <summary>Durable, append-only, replayable event log. This is the contract a broker-backed
/// provider MUST implement to support recoverable streams + rewind.</summary>
public interface IStreamEventLog
{
    /// <summary>Append one item, returning its assigned monotonic position token.</summary>
    ValueTask<StreamSequenceToken> AppendAsync<T>(StreamId streamId, T item, CancellationToken ct = default);

    /// <summary>Replay items with position strictly greater than <paramref name="after"/>
    /// (null = from the earliest retained item). Ordered by position.</summary>
    IAsyncEnumerable<StreamEvent<T>> ReadFromAsync<T>(
        StreamId streamId, StreamSequenceToken? after, CancellationToken ct = default);
}

public readonly record struct StreamEvent<T>(StreamSequenceToken Token, T Item);
```

### 2. Persisted state (in the provider package, `[GenerateSerializer]`)

```csharp
// Only the persisted shapes carry codecs. GrainId already has a registered codec (crosses TCP);
// StreamId is NOT serialized into the payload — it is the storage key.
[GenerateSerializer]
public sealed class PubSubStreamState
{
    [Id(0)] public List<SubscriptionEntry> Subscriptions { get; set; } = [];
}

[GenerateSerializer]
public sealed class SubscriptionEntry
{
    [Id(0)] public Guid Id { get; set; }
    [Id(1)] public GrainId Subscriber { get; set; }
    // Scalar position instead of the polymorphic StreamSequenceToken — see Resolved decision.
    [Id(2)] public long? StartSequence { get; set; }
}

[GenerateSerializer]
public sealed class PubSubDirectoryState   // per-namespace index for GetActiveStreamsAsync
{
    [Id(0)] public HashSet<string> StreamKeys { get; set; } = [];
}
```

### 3. DI surface (drop-in shaped, Quark-native provider)

```csharp
// Silo:
silo.Services.AddPersistentStreams("providerName");   // parallels AddMemoryStreams("providerName")
silo.Services.AddPubSubStore();                        // binds IStreamPubSubStore to keyed IGrainStorage "PubSubStore"
silo.Services.AddInMemoryGrainStorage("PubSubStore");  // or AddRedisGrainStorage("PubSubStore", ...)

// Item codecs still required (no reflection):
silo.Services.AddStreamableCodec<MyMsg, MyMsgCodec>();

// Implicit subscribers (unchanged registration, now rehydrated on cold start):
silo.Services.AddImplicitStreamSubscription("namespace", "MyGrainTypeKey");
```

### 4. Rewind entry point — correction to the issue draft

The issue draft proposes adding `Task<StreamSubscriptionHandle<T>> SubscribeAsync(IAsyncObserver<T>, StreamSequenceToken?)`. **Validated against live source, this needs adjustment:**

- `IAsyncStream<T>` (`IAsyncStream.cs:14-17`) already has a token-carrying overload, but it is the **delegate form** `SubscribeAsync(Func<T, StreamSequenceToken?, Task> onNext, …)` where the token is delivered *per item to the callback* — it is **not** a rewind-from-token entry point.
- The actual rewind primitive already exists: `StreamSubscriptionHandle<T>.ResumeAsync(IAsyncObserver<T> observer, StreamSequenceToken? token = null)` (`StreamSubscriptionHandle.cs:12`). The in-memory handle throws `NotSupportedException` (`InMemorySubscriptionHandle.cs:28-29`); the persistent handle will implement it as replay-from-token.

**Recommendation:** add one convenience overload for the *initial* rewind-subscribe (you have no handle yet after a cold start), and implement `ResumeAsync` on the persistent handle for re-attach. Both are minor-change tier:

```csharp
// Added to IAsyncStream<T>; InMemoryStream implements it as start-from-now (ignores token).
Task<StreamSubscriptionHandle<T>> SubscribeAsync(IAsyncObserver<T> observer, StreamSequenceToken? token);
```

---

## Runtime integration (real file/method anchors)

### New package `Quark.Streaming.Persistent`
References `Quark.Streaming.Abstractions`, `Quark.Persistence.Abstractions` (for `IGrainStorage`/`GrainState<T>`), `Quark.Core.Abstractions` (for `ILifecycleSubject`, `IImplicitStreamActivator`). Does **not** reference `Quark.Runtime` (keeps the provider runtime-agnostic; the rehydration hook uses only abstractions).

- `PersistentStreamProvider : IStreamProvider` — parallels `InMemoryStreamProvider` (`src/Quark.Streaming.InMemory/InMemoryStreamProvider.cs`). `GetStream<T>` returns a `PersistentStream<T>` bound to the shared registry + `IStreamPubSubStore` + `IStreamEventLog`.
- `PersistentStream<T> : IAsyncStream<T>` — parallels `InMemoryStream<T>` (`InMemoryStream.cs`).
  - `OnNextAsync`: `await _eventLog.AppendAsync(StreamId, item)` → token, then fan out through the shared `StreamSubscriptionRegistry.PublishAsync` (reuse existing fan-out; do not fork it — #63 owns fan-out semantics).
  - `SubscribeAsync(observer)`: `RegisterSubscriptionAsync(StreamId, subscriber, from: null)` then live-attach.
  - `SubscribeAsync(observer, token)`: register with `from: token`, replay `_eventLog.ReadFromAsync(StreamId, token)` into the observer, then attach live at the tail (guard the seam so items appended during replay are not dropped or double-sent — replay up to captured tail token, then live from that token).
- `PersistentSubscriptionHandle<T> : StreamSubscriptionHandle<T>` — implements `ResumeAsync(observer, token)` as replay-from-token (the InMemory handle throws; this one works); `UnsubscribeAsync` calls `IStreamPubSubStore.UnregisterSubscriptionAsync`.
- `GrainStoragePubSubStore : IStreamPubSubStore` — maps `StreamId → GrainId.Create(new GrainType("PubSub"), $"{ns}{key}")`, persists `PubSubStreamState` via the keyed `IGrainStorage "PubSubStore"` using `ReadStateAsync/WriteStateAsync/ClearStateAsync` (`IGrainStorage.cs:12-33`). Maintains `PubSubDirectoryState` per namespace for `GetActiveStreamsAsync`. Uses `GrainState<T>.ETag` for optimistic concurrency on concurrent register/unregister.
- `PubSubRehydrationLifecycleObserver : ILifecycleObserver` — on start, `GetActiveStreamsAsync()` → for each stream with an implicit-subscriber namespace, call `IImplicitStreamActivator.EnsureActivatedAsync(grainTypeKey, streamId.Key)`. Subscribed to `ILifecycleSubject` at a late stage so transports/routers are up first.

### Where rehydration attaches (verified)
`SiloHostedService.StartAsync` (`src/Quark.Runtime/SiloHostedService.cs:36-68`) awaits `_lifecycle.StartAsync` at **line 57**, after `SiloMessagePump.StartAsync` (line 54) and before router registration (line 63). The rehydration observer registers via `LifecycleSubject.Subscribe(name, stage, observer)` (`src/Quark.Runtime/LifecycleSubject.cs:17`) at a stage that runs during that `_lifecycle.StartAsync`. No change to `SiloHostedService` itself is required — registration is pure DI.

### Implicit subscription — verified WIRED (correcting the older spec)
`[ImplicitStreamSubscription]` **is** wired today, contrary to the stale note:
- `StreamSubscriptionRegistry.PublishAsync` (`StreamSubscriptionRegistry.cs:89-94`) consults `ImplicitStreamSubscriptionRegistry.TryGetGrainTypes` and calls `IImplicitStreamActivator.EnsureActivatedAsync` before fan-out.
- `LocalImplicitStreamActivator` (`src/Quark.Runtime/LocalImplicitStreamActivator.cs:11-12`) → `LocalGrainCallInvoker.EnsureActivatedAsync` (`LocalGrainCallInvoker.cs:228`).
- Registered by `RuntimeServiceCollectionExtensions.cs:98` (`TryAddSingleton<IImplicitStreamActivator, LocalImplicitStreamActivator>`).

**Caveat that motivates cold rehydration:** the registry is populated by explicit `AddImplicitStreamSubscription(ns, grainTypeKey)` calls (`InMemoryStreamingServiceCollectionExtensions.cs:34-50`), **not** by scanning the attribute, and grains are auto-activated only **on publish** (warm path). After a restart with durable subscriptions but no new publish, subscribers stay cold — exactly what `PubSubRehydrationLifecycleObserver` fixes by driving `EnsureActivatedAsync` from the durable registry.

### Broker provider contract (explicit, for #92 / #106)
A broker-backed persistent-stream provider must supply exactly three capabilities; everything else (fan-out, handles) is reused:
1. **Append** — `IStreamEventLog.AppendAsync<T>` returning a monotonic `StreamSequenceToken` (Kafka offset / Redis Stream ID mapped to the token; scalar form is a `long`, see Resolved decision).
2. **Read-from-token** — `IStreamEventLog.ReadFromAsync<T>(streamId, after)` streaming ordered replay; `after == null` means earliest retained.
3. **Subscription registry** — `IStreamPubSubStore` (may be backed by the broker's own consumer-group / offset store instead of `IGrainStorage`).
Providers reuse `StreamSubscriptionRegistry` for live fan-out and `PersistentSubscriptionHandle<T>` for rewind/resume. They do not touch `IAsyncStream<T>`/handle abstractions.

---

## AOT & trim notes
- All persisted shapes (`PubSubStreamState`, `SubscriptionEntry`, `PubSubDirectoryState`) use `[GenerateSerializer]` + `[Id]` — `SerializerGenerator` emits their codecs; no reflection.
- Event-log payloads serialize via the registered `IFieldCodec<T>` from `AddStreamableCodec<T, TCodec>()` — no runtime serialization discovery, matching the in-memory path.
- `GrainId` already has a registered codec (crosses TCP). `StreamId` is **not** embedded in any payload — it is the storage key string — so no new struct codec is needed.
- **Token polymorphism avoided:** `StreamSequenceToken` is abstract; persisting it would need a polymorphic codec (AOT-hostile). Persist the scalar `long` position (`SequentialToken.SequenceNumber`, `SequentialToken.cs:7`) and reconstruct a concrete token on rehydration. Brokers map their offset to `long`.
- No assembly scanning; every provider, store, codec, and implicit subscription is explicitly registered.
- `IStreamPubSubStore` / `IStreamEventLog` are `ValueTask`-returning, consistent with the `IGrainCallInvoker` ValueTask convention.

---

## Test plan
- **Unit (`Quark.Tests.Unit`)**
  - `GrainStoragePubSubStore`: register → get → unregister round-trips against in-memory `IGrainStorage`; ETag concurrency on interleaved register/unregister; directory index reflects active streams; `ClearStateAsync` when last subscription removed.
  - `PersistentStream<T>` rewind: append N items, `SubscribeAsync(observer, token@k)` replays items `> k` in order, then live items continue without gap or duplication across the replay→live seam.
  - Scalar token round-trip: `SequentialToken` ↔ `long?` `StartSequence`.
- **Integration (`Quark.Tests.Integration`, `TestCluster`)**
  - Subscription survives silo restart: subscribe, dispose+recreate silo sharing the same store, publish, recovered subscriber receives it (proves rehydration + PubSubStore).
  - Cold rehydration with no publish before restart: implicit subscriber re-activated on start via `PubSubRehydrationLifecycleObserver`.
  - Multi-subscriber fan-out with mixed rewind tokens.
  - Redis-backed `PubSubStore` via Testcontainers (`AddRedisGrainStorage("PubSubStore")`), `[Trait("category","integration")]`.
- **Codegen (`Quark.Tests.CodeGenerator`)**
  - `[GenerateSerializer]` on `PubSubStreamState`/`SubscriptionEntry`/`PubSubDirectoryState` emits codecs; assert no reflection fallbacks.
- **AOT smoke:** `dotnet publish … /p:PublishAot=true` including `Quark.Streaming.Persistent`; expect zero trim/AOT warnings.

---

## Implementation checklist
- [ ] Add `IStreamPubSubStore`, `IStreamEventLog`, `StreamSubscription`, `StreamEvent<T>` to `Quark.Streaming.Abstractions`.
- [ ] Add `SubscribeAsync(IAsyncObserver<T>, StreamSequenceToken?)` overload to `IAsyncStream<T>`; implement start-from-now in `InMemoryStream<T>`.
- [ ] Create `Quark.Streaming.Persistent` project (refs: Streaming.Abstractions, Persistence.Abstractions, Core.Abstractions); add to `Quark.slnx` + `Directory.Packages.props` wiring.
- [ ] Implement `GrainStoragePubSubStore` (+ `PubSubStreamState`, `SubscriptionEntry`, `PubSubDirectoryState`, `[GenerateSerializer]`).
- [ ] Implement `PersistentStreamProvider`, `PersistentStream<T>`, `PersistentSubscriptionHandle<T>` (reuse `StreamSubscriptionRegistry` for fan-out).
- [ ] Implement in-memory `IStreamEventLog` (bounded ring keyed by `long` sequence + optional durable spill); leave broker impls to #92/#106.
- [ ] Implement `PubSubRehydrationLifecycleObserver : ILifecycleObserver`; register at a late lifecycle stage.
- [ ] DI extensions: `AddPersistentStreams(name)`, `AddPubSubStore()` in `PersistentStreamingServiceCollectionExtensions`.
- [ ] Implement `ResumeAsync` replay on `PersistentSubscriptionHandle<T>`.
- [ ] Tests per the test plan (unit, integration, codegen, Redis Testcontainers).
- [ ] Docs: `wiki/Streaming.md` persistent-streams section; `FEATURES.md` entry; sample update (extend `samples/Streaming` or the Persistence sample).
- [ ] Cross-link #63 (delivery mode) and #92/#106 (broker providers) in the PR body.

---

## Resolved design decisions (open questions from the issue answered)

1. **Rewind API shape.** *Q: add `SubscribeAsync(observer, token)`?* — **Yes, but as a minor addition, not the primary mechanism.** Live source already has (a) the delegate-form `SubscribeAsync` (per-item token, not rewind) and (b) `StreamSubscriptionHandle<T>.ResumeAsync(observer, token)` (the real rewind primitive, `StreamSubscriptionHandle.cs:12`). Recommendation: implement `ResumeAsync` on the persistent handle for re-attach, and add one `SubscribeAsync(observer, token)` overload for the initial rewind-subscribe (no handle exists after cold start). InMemory implements it as start-from-now. *Rationale:* the issue draft assumed no token overload existed; the correct fix reuses `ResumeAsync` and adds only the ergonomic initial-subscribe overload — least abstraction churn.

2. **Token durability / polymorphism.** *Q: how to persist `StreamSequenceToken`?* — **Persist the scalar `long`, not the abstract token.** `SequentialToken.SequenceNumber` is a `long`; brokers expose monotonic offsets that map to `long`. *Rationale:* avoids a polymorphic AOT-hostile codec; replay is index math; brokers reconstruct their native position from the `long`.

3. **PubSub backing store.** *Q: dedicated store vs reuse `IGrainStorage`?* — **Reuse `IGrainStorage` keyed `"PubSubStore"`.** One `PubSubStreamState` record per stream. *Rationale:* zero new persistence surface; instantly works across InMemory/Redis/AdoNet; matches the named-storage pattern (`AddInMemoryGrainStorage(name)`).

4. **Enumerating active streams for cold rehydration.** *Q: how, given `IGrainStorage` has no scan?* — **Maintain a per-namespace `PubSubDirectoryState` index**, updated transactionally with register/unregister; `GetActiveStreamsAsync` reads it. *Rationale:* `IGrainStorage.cs` exposes only Read/Write/Clear by key — no enumeration — so an explicit index is required; keeps the store provider-agnostic.

5. **When does rehydration re-activate grains?** *Q: on first publish (warm) or eagerly on start (cold)?* — **Both.** Warm path already exists (`PublishAsync` → `EnsureActivatedAsync`). Add cold path via `PubSubRehydrationLifecycleObserver` driven off `GetActiveStreamsAsync`. *Rationale:* durable subscriptions must survive a restart even when no producer publishes afterward; the warm path alone leaves them cold.

6. **Fan-out / delivery semantics.** *Q: fix blocking sequential fan-out here?* — **No — defer to #63.** The persistent provider reuses `StreamSubscriptionRegistry.PublishAsync` as-is and inherits whatever delivery mode #63 defines. *Rationale:* avoid two specs redefining fan-out; #63 owns backpressure/isolation.

7. **Attribute scanning for `[ImplicitStreamSubscription]`.** *Q: auto-discover subscribers?* — **No — keep explicit `AddImplicitStreamSubscription(ns, key)` registration.** *Rationale:* attribute scanning is trim-unsafe; the runtime is already wired for explicit registration (`InMemoryStreamingServiceCollectionExtensions.cs:34-50`). A future `BehaviorRegistrationGenerator` extension could emit these calls (codegen, not reflection) — noted as follow-up, out of scope here.

8. **Package placement.** *Q: where does the provider live?* — **New `Quark.Streaming.Persistent`**, depending only on abstractions (Streaming/Persistence/Core). *Rationale:* keeps `Quark.Runtime` free of a persistence→streaming coupling; the rehydration hook needs only `ILifecycleSubject` + `IImplicitStreamActivator`, both abstractions.

---

## Dependencies & related work
- **#63** (B5 — reminder CAS + defined stream delivery mode/backpressure): owns fan-out/delivery semantics this spec consumes. Cross-reference, do not duplicate.
- **#92** (Redis Streams provider) / **#106** (Kafka provider): implement the `IStreamEventLog` + `IStreamPubSubStore` contract defined here; specced separately in `2026-07-02-broker-stream-providers-design.md`.
- **#16 / F-07** (streaming + `[ImplicitStreamSubscription]`): completed and verified wired; this spec builds the cold-rehydration path on top.
- **#78** (`IGrainContext` stale/unwired): honored — no context extension; rehydration uses `IImplicitStreamActivator`.
- Persistence providers (`Quark.Persistence.InMemory`, `Quark.Persistence.Redis`): reused unchanged via keyed `IGrainStorage "PubSubStore"`.
- `IGrainCallInvoker` ValueTask convention: new store/log interfaces follow it (`ValueTask` returns).
