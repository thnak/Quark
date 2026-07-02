# Design: Broker-backed stream providers (Redis Streams, Kafka)
**Issues:** #92 (Quark.Streaming.Redis), #106 (Quark.Streaming.Kafka)
**Date:** 2026-07-02
**Status:** Draft — blocked on #41 (persistent-streams foundation)

---

## 0. Assumptions (state before invention)

The sibling spec `2026-07-02-persistent-streams-design.md` (#41) is being authored in
parallel and is not yet on disk. This spec designs the two broker providers as
**implementations of the #41 contract**. Where #41's details are not yet fixed, the
following assumptions are made explicitly rather than inventing a competing abstraction:

- **A1.** #41 adds a rewind overload `Task<StreamSubscriptionHandle<T>> SubscribeAsync(IAsyncObserver<T> observer, StreamSequenceToken? token)`
  to `IAsyncStream<T>`. Today the live interface (`src/Quark.Streaming.Abstractions/IAsyncStream.cs`)
  has **no** token-taking `SubscribeAsync`; only `StreamSubscriptionHandle<T>.ResumeAsync(observer, token)`
  exists. Broker providers implement both the (assumed) subscribe overload and the existing
  `ResumeAsync`. If #41 lands the overload with a different name/shape, only the two
  `IAsyncStream<T>` implementations here change — no broker logic changes.
- **A2.** #41 defines `IStreamPubSubStore` (durable subscription registry backed by `IGrainStorage`),
  the `StreamSubscription` DTO, `AddPersistentStreams("name")`, `AddPubSubStore()`, and an
  activation-time **rehydration** step. Both broker providers consume these as-is; neither
  invents its own subscription-durability mechanism.
- **A3.** #41's `PersistentStreamProvider` owns an in-process durable event log + bounded replay
  cache for the *in-memory-log* case. Broker providers **replace** that log with the broker itself
  (see §2.4); they still use `IStreamPubSubStore` for subscriber durability and rehydration.
- **A4.** `StreamSequenceToken` / `SequentialToken` are the token base and default (`long`-keyed)
  implementation. Both already exist and ship today.

> **Reconciliation note (2026-07-02, post-#41 finalization):** `2026-07-02-persistent-streams-design.md`
> is now on disk and **confirms all four assumptions**: A1 lands as the convenience
> `SubscribeAsync(observer, token)` overload for initial subscribe with
> `StreamSubscriptionHandle<T>.ResumeAsync(observer, token)` as the re-attach primitive (implement
> both, exactly as §1.9/§2 already do); A2 lands as specified (`IStreamPubSubStore`,
> `StreamSubscription`, `AddPersistentStreams`, `AddPubSubStore`, rehydration observer); A3 lands as
> `IStreamEventLog` (append / read-from-token) with the in-memory implementation only — broker
> providers implement `IStreamEventLog` over the broker per §2.4; A4 confirmed, and #41 additionally
> **mandates persisting the scalar `long` sequence** rather than the abstract token — Redis entry-IDs
> must map to/from `long` (see #41 "Resolved design decisions" and the risk it flags for Redis).

If any assumption is contradicted by the final #41 spec, the affected seam is called out in the
implementation checklist (§7) so it can be re-pointed without redesign.

---

## 1. Goals / Non-goals

### Goals
- Ship two silo-side durable stream providers that satisfy the #41 persistent-streams contract:
  - **`Quark.Streaming.Redis`** — Redis **Streams** (XADD / consumer groups), sibling to
    `Quark.Persistence.Redis` and `Quark.Reminders.Redis`.
  - **`Quark.Streaming.Kafka`** — Apache Kafka, durable/replayable, partitioned.
- Durability across silo restart: published events survive and recovered subscribers resume
  from a `StreamSequenceToken`.
- One **shared broker mapping** (§2) so both providers agree on how `StreamId`,
  `StreamSequenceToken`, and `SubscribeAsync` map onto broker primitives.
- AOT/trim-first: no reflection scanning, explicit provider registration, payloads serialized
  through the **registered** `IFieldCodec<T>` (`AddStreamableCodec<T,TCodec>`) — no runtime type
  discovery.
- Drop-in consumption surface: user code keeps calling `provider.GetStream<T>(id)` /
  `OnNextAsync` / `SubscribeAsync` unchanged.

### Non-goals
- No changes to the `IAsyncStream<T>` / `StreamSubscriptionHandle<T>` public surface here — those
  are owned by #41.
- No client-side broker consumers. Client-side stream consumption already flows through the TCP
  gateway (`Quark.Client.Tcp` + `GatewayMessagePump`); broker connections stay silo-side.
- No competing-consumer / load-balanced work queues. Quark stream semantics are **broadcast
  fan-out** (every subscriber sees every event), matching Orleans (§2.3).
- No broker-managed retention policy engine. Replay horizon = broker retention config (§2.4); Quark
  does not garbage-collect broker logs beyond an optional `MaxLength` trim hint.
- No schema-registry / Avro / Protobuf integration for Kafka — payloads are opaque `byte[]` produced
  by Quark codecs.
- No cross-provider token comparison (tokens from different providers are not comparable, §2.2).

**Compatibility tier (both providers): minor-change.** Orleans ships no Redis-Streams provider;
its Kafka provider is community-maintained. The consumed `IAsyncStream<T>` surface is **drop-in**;
the DI entry points (`AddRedisStreams` / `AddKafkaStreams`) are new but mirror the shape of the
existing drop-in `AddMemoryStreams(name)`.

---

## 2. Shared broker mapping

Both providers share these mapping rules. Provider-specific encodings follow in §3/§4.

### 2.1 `StreamId` → channel / partition

`StreamId` is `{ Namespace, Key }` (`src/Quark.Streaming.Abstractions/StreamId.cs`).

| | Redis Streams | Kafka |
|---|---|---|
| Unit | one **stream key** per `StreamId` | one **topic** per `Namespace`, one **partition** per `StreamId.Key` |
| Encoding | `"{KeyPrefix}:{Namespace}:{Key}"` (default prefix `quark:stream`) | topic = `map(Namespace)` (default `"{TopicPrefix}.{Namespace}"`); partition = `stableHash(Key) % PartitionCount` |
| Ordering | total order within the stream key | total order within the partition ⇒ per-`StreamId` order preserved |
| Rationale | matches Redis-storage key convention; unbounded key cardinality is fine in Redis | Kafka topics are heavyweight (metadata/controller cost); a topic **per StreamId** would explode. Namespace→topic + key→partition keeps topic count bounded while preserving per-stream ordering. |

`stableHash` must be deterministic across processes and .NET versions (do **not** use
`string.GetHashCode()`, which is randomized). Use the same FNV-1a / xxHash helper the placement
`[HashBasedPlacement]` path uses if one is already exposed; otherwise add a small internal
`StableHash(string)` to the provider package. **Open question OQ-1.**

### 2.2 `StreamSequenceToken` → offset / entry-id

Each provider ships a dedicated token type (not a reused `SequentialToken`) so that
`CompareTo`/`Newer` throw `ArgumentException` on a foreign token — the same isolation
`SequentialToken` already enforces, preventing accidental cross-provider comparison.

- **Redis:** `RedisStreamToken : StreamSequenceToken` wrapping the Redis entry ID `<ms>-<seq>`
  (two `long`s). `CompareTo` orders lexicographically by `(ms, seq)`. Entry IDs are broker-assigned
  and monotonic, so `OnNextAsync` returns the XADD-assigned ID as the token.
- **Kafka:** `KafkaStreamToken : StreamSequenceToken` wrapping `(int Partition, long Offset)`.
  `CompareTo` compares `Offset` (partition is fixed for a given `StreamId`, carried for validation).

Both token types are `[GenerateSerializer]` (Ids stable) so they can be persisted in a
`StreamSubscription` record (#41 A2) and survive rehydration.

### 2.3 Consumer-group semantics for `SubscribeAsync`

Quark streams are **broadcast**: each subscriber receives the full event sequence. This is *not*
competing-consumer load balancing. Therefore **each subscription gets its own broker consumer
group**, keyed by the subscription's `Guid` id:

- **Redis Streams:** consumer group name = `sub:{subscriptionId:N}`. `XGROUP CREATE key group start MKSTREAM`
  where `start` = token entry-id (replay), `"$"` (from-now, default), or `"0-0"` (from-beginning).
  Delivery loop: `XREADGROUP GROUP group consumer COUNT n BLOCK ms STREAMS key ">"`, deliver to the
  observer, then `XACK` on success. A crashed-but-rehydrated subscription re-attaches to its existing
  group (pending-entries list `XREADGROUP ... "0"` first to redrive un-ACKed items, then `">"`).
- **Kafka:** consumer `group.id` = `subscriptionId:N`; the consumer is **manually assigned** the
  single partition for the `StreamId` (not `Subscribe`-by-topic, to avoid rebalancing across
  unrelated streams). `Seek(token.Offset)` for replay, or `AutoOffsetReset` = Latest (default) /
  Earliest. Offsets committed per delivered message (or batched — OQ-3).

Fan-out to N grains on the same silo currently means N broker consumers. This is correct but does
not scale to very high subscription counts; a **pooled/multiplexed silo-level reader** that
demultiplexes to per-subscription observers is a documented v2 optimization (§9, OQ-4).

### 2.4 Durable event log ownership (relation to #41 A3)

For the in-memory-log variant, #41's `PersistentStreamProvider` keeps a per-stream durable event log
plus a bounded replay ring. **Broker providers delegate that responsibility to the broker**: the
Redis stream / Kafka partition *is* the durable, ordered, replayable log. Consequences:

- No separate `IGrainStorage`-backed event log is written by these providers — avoids double-writes.
- `SubscribeAsync(observer, token)` replay is broker-native (`XREADGROUP` from entry-id / Kafka
  `Seek`), so there is **no bounded in-process replay cache** and no cache-eviction race.
- **Replay horizon = broker retention**, not Quark: Redis stream `MAXLEN ~ N` trim (optional
  `MaxLength` option) / Kafka `retention.ms`/`retention.bytes`. A token older than the broker's
  retained window fails replay; the provider surfaces this as a typed
  `StreamRewindExpiredException` (new, in the provider package) rather than silently starting from
  now. **This is a semantic difference from the in-memory-log provider and must be documented.**
- `IStreamPubSubStore` (#41 A2) is **still used** — it records *which* grains subscribe and their
  last token, so rehydration on silo activation can re-create the broker consumer groups and
  re-bind `[ImplicitStreamSubscription]` grains (#16). The broker holds the *events*; the PubSub
  store holds the *subscriptions*.

---

## 3. Redis Streams provider — `Quark.Streaming.Redis` (first)

Mirrors `Quark.Persistence.Redis` conventions exactly: an `IRedis*Connection` testability seam,
`IOptions<T>`, `TryAddSingleton` + keyed registrations, `ISerializer` for payloads.

### 3.1 Options
```csharp
public sealed class RedisStreamOptions
{
    public string ConnectionString { get; set; } = "localhost:6379";
    public string KeyPrefix { get; set; } = "quark:stream";
    public int Database { get; set; }
    /// <summary>Optional approximate MAXLEN trim hint applied on XADD (0 = no trim).</summary>
    public int MaxLength { get; set; }
    /// <summary>XREADGROUP BLOCK timeout for the delivery pump.</summary>
    public int PollBlockMilliseconds { get; set; } = 5000;
    /// <summary>Default start when subscribing without a token.</summary>
    public StreamStartPosition DefaultStartPosition { get; set; } = StreamStartPosition.Latest;
}
public enum StreamStartPosition { Latest, Earliest }
```

### 3.2 Connection seam (mirrors `IRedisStorageConnection`)
```csharp
public interface IRedisStreamConnection
{
    Task<RedisStreamEntryId> AppendAsync(string key, ReadOnlyMemory<byte> payload, int maxLength, CancellationToken ct = default);
    Task EnsureGroupAsync(string key, string group, RedisStreamEntryId start, CancellationToken ct = default);
    Task<IReadOnlyList<RedisStreamMessage>> ReadGroupAsync(string key, string group, string consumer, int count, int blockMs, bool pendingFirst, CancellationToken ct = default);
    Task AckAsync(string key, string group, RedisStreamEntryId id, CancellationToken ct = default);
    Task DeleteGroupAsync(string key, string group, CancellationToken ct = default);
}
public readonly record struct RedisStreamEntryId(long Ms, long Seq);
public readonly record struct RedisStreamMessage(RedisStreamEntryId Id, byte[] Payload);
```
`RedisStreamConnection` wraps `ConnectionMultiplexer` (single multiplexer per named provider, as in
`RedisStorageConnection`) and implements XADD/XGROUP CREATE/XREADGROUP/XACK/XDEL. `EnsureGroupAsync`
tolerates `BUSYGROUP` (group already exists) on rehydration.

### 3.3 Types
- `RedisStreamProvider : IStreamProvider` — keyed by provider name; `GetStream<T>` returns a cached
  `RedisStream<T>` per `(StreamId, typeof(T))`, like `InMemoryStreamProvider`.
- `RedisStream<T> : IAsyncStream<T>` — `OnNextAsync` serializes `item` via the registered
  `IFieldCodec<T>` (through `ISerializer`) and calls `AppendAsync`, returning a `RedisStreamToken`.
  `SubscribeAsync(observer[, token])` allocates a `subscriptionId`, records it via
  `IStreamPubSubStore` (#41), `EnsureGroupAsync` at the resolved start, and launches a
  `RedisStreamPump`.
- `RedisStreamToken : StreamSequenceToken` — §2.2.
- `RedisStreamPump` — long-running loop (`Task.Run` / dedicated `Channel` drain) doing
  `ReadGroupAsync` → codec-decode → `observer.OnNextAsync(item, token)` → `AckAsync`. Backoff on
  broker errors; honors a `CancellationToken` tied to the handle/silo lifetime.
- `RedisStreamSubscriptionHandle<T> : StreamSubscriptionHandle<T>` — `UnsubscribeAsync` stops the
  pump, `DeleteGroupAsync`, and removes the PubSub entry; `ResumeAsync` re-launches from a token.
- `RedisStreamingServiceCollectionExtensions` — `AddRedisStreams(name, configure)` +
  `AddKeyedRedisStreams` (§5).

### 3.4 Serialization
Payload = `IFieldCodec<T>` output bytes only; the Redis entry-id **is** the token, so no token is
stored in the entry body. Element type is known statically from `GetStream<T>`; no runtime type tag
needed. Item types require `AddStreamableCodec<T,TCodec>()` — same rule as in-memory streams.

---

## 4. Kafka provider — `Quark.Streaming.Kafka` (second)

Same architectural shape as §3; broker-specific differences noted. Uses `Confluent.Kafka`.

### 4.1 Options
```csharp
public sealed class KafkaStreamOptions
{
    public string BootstrapServers { get; set; } = "localhost:9092";
    public string TopicPrefix { get; set; } = "quark.stream";
    public int PartitionCount { get; set; } = 16;
    public bool AutoCreateTopics { get; set; } = true;
    public short ReplicationFactor { get; set; } = 1;
    public StreamStartPosition DefaultStartPosition { get; set; } = StreamStartPosition.Latest;
    /// <summary>Escape hatch: mutate raw Confluent producer config.</summary>
    public Action<Confluent.Kafka.ProducerConfig>? ConfigureProducer { get; set; }
    public Action<Confluent.Kafka.ConsumerConfig>? ConfigureConsumer { get; set; }
}
```

### 4.2 Connection seam
```csharp
public interface IKafkaStreamConnection
{
    Task<KafkaStreamToken> ProduceAsync(string topic, string key, ReadOnlyMemory<byte> payload, CancellationToken ct = default);
    IKafkaPartitionReader CreateReader(string topic, int partition, string groupId, long? startOffset, StreamStartPosition defaultStart);
    Task EnsureTopicAsync(string topic, int partitions, short replication, CancellationToken ct = default);
}
public interface IKafkaPartitionReader : IDisposable
{
    bool TryConsume(int timeoutMs, out KafkaStreamMessage message);   // librdkafka is sync
    void Commit(long offset);
}
public readonly record struct KafkaStreamMessage(int Partition, long Offset, byte[] Payload);
```
Producers: `byte[]` value serializer (`Serializers.ByteArray`) — **bypasses Confluent's
reflection-based default serializers** (key AOT decision, §6). One shared producer per named
provider. Consumers: manual `Assign(new TopicPartition(topic, partition))`, `Seek` for replay,
manual offset commit.

### 4.3 Types
`KafkaStreamProvider`, `KafkaStream<T>`, `KafkaStreamToken`, `KafkaStreamPump` (owns a dedicated
thread because `Consume` blocks synchronously — do not spin it on the thread pool),
`KafkaStreamSubscriptionHandle<T>`, `KafkaStreamingServiceCollectionExtensions`
(`AddKafkaStreams(name, configure)`). Fan-out, replay, and PubSub-store integration are identical
in shape to §3.

### 4.4 Ordering / partitioning note
Ordering is guaranteed only within a partition. Because `StreamId.Key → partition` is a deterministic
hash (§2.1) and the producer sets the Kafka message key = `StreamId.Key`, all events for one stream
land on one partition ⇒ per-stream total order. Two different `StreamId`s may share a partition; that
is harmless (each subscription filters to its own key via the stream-key it read from) — but note
consumers assigned to a partition see **all** keys on it, so the pump **must drop messages whose key
≠ the subscription's `StreamId.Key`**. **Open question OQ-2** (accept the read-amplification, or add
a header filter, or one-topic-per-stream for low-cardinality namespaces).

---

## 5. DI & packaging

```csharp
// Redis
silo.Services.AddRedisStreams("chat", o => o.ConnectionString = "localhost:6379");
// Kafka
silo.Services.AddKafkaStreams("events", o => o.BootstrapServers = "localhost:9092");

// Both require, from #41:
silo.Services.AddPersistentStreams("chat");   // provider-agnostic persistent-stream plumbing
silo.Services.AddPubSubStore();               // durable subscription registry
silo.Services.AddRedisGrainStorage("PubSubStore"); // or InMemory / AdoNet

// Item codecs (unchanged, explicit — no discovery):
silo.Services.AddStreamableCodec<ChatMsg, ChatMsgCodec>();
```

Registration extension shape mirrors `AddMemoryStreams` + `AddRedisGrainStorage`:
`AddOptions<T>` → optional `Configure` → `TryAddSingleton` the connection seam →
`AddKeyedSingleton<IStreamProvider>(name, factory)`. A keyed `AddKeyedRedisStreams(name, …)` /
`AddKeyedKafkaStreams(name, …)` variant (per-provider connection + frozen options) matches
`AddKeyedRedisGrainStorage`.

### Package boundaries
| Package | References | Notes |
|---|---|---|
| `Quark.Streaming.Redis` | `Quark.Streaming.Abstractions`, `Quark.Serialization`, `StackExchange.Redis`, `Microsoft.Extensions.{DI,DI.Abstractions,Options}` | silo-side; **no** persistence coupling — PubSubStore wiring is the host's job (via #41 `AddPubSubStore`) |
| `Quark.Streaming.Kafka` | `Quark.Streaming.Abstractions`, `Quark.Serialization`, `Confluent.Kafka`, `Microsoft.Extensions.{DI,DI.Abstractions,Options}` | silo-side |

`TargetFrameworks` = `net9.0;net10.0` (matches sibling packages). Neither references
`Quark.Client*` or `Quark.Runtime` internals. `Confluent.Kafka` version pinned in
`Directory.Packages.props` (no inline `Version=`). `StackExchange.Redis` already pinned at `2.8.37`.

---

## 6. AOT & trim notes

- **Redis: clean.** Reuse `Quark.Persistence.Redis` conventions verbatim — managed-only
  StackExchange.Redis, `IRedisStreamConnection` seam, `IOptions`, no reflection. Set
  `IsTrimmable=true` / `EnableAotAnalyzer=true` (inherited from `Directory.Build.props`). Payload
  serialization goes through the registered `IFieldCodec<T>` / `ISerializer` — no runtime type
  discovery, no `ISerializable` (no QRK0003).
- **Kafka: needs verification, flag as risk.** `Confluent.Kafka` wraps native `librdkafka` via
  P/Invoke. Concerns to validate before declaring the package AOT-safe:
  1. **Native asset bundling** — `librdkafka` ships as a per-RID native `.so`/`.dll`
     (`runtime.linux-x64.native.*`). AOT single-file publish must include it; confirm the native
     asset flows through `dotnet publish /p:PublishAot=true` for each RID.
  2. **Reflection in config binding** — Confluent binds `ProducerConfig`/`ConsumerConfig` via
     property enumeration. Under trimming this may need `[DynamicDependency]` roots or a trimmer
     descriptor (`ILLink.Descriptors.xml`) so config properties survive.
  3. **Default serializers** — Confluent's built-in serializers use reflection / schema-registry.
     We **avoid them entirely** by producing/consuming `byte[]` (`Serializers.ByteArray` /
     `Deserializers.ByteArray`); payloads are Quark-codec bytes. This removes the largest reflection
     surface.
  4. **Recommendation** — run the AOT smoke publish (a silo referencing `Quark.Streaming.Kafka`)
     in CI and gate merge on it, exactly as the runtime smoke build. If `Confluent.Kafka` proves
     un-trimmable at the current version, fall back to `IsTrimmable=false` on this package **only**
     (with an explicit note), leaving Redis and the rest of the framework trim-clean.
- Tokens are small value types / `[GenerateSerializer]` records — no reflection.
- Background pumps use `Task.Run` / dedicated threads and `Channel<T>`; no reflection, no timers.

---

## 7. Test plan

All broker tests live in `tests/Quark.Tests.Integration`, tagged `[Trait("category","integration")]`
and using `Testcontainers`, skipped when infra is unavailable (house convention).

**Redis (Testcontainers Redis):**
- Publish → subscribe broadcast fan-out (2+ independent subscribers each receive all N events).
- Replay-from-token: XADD 5, subscribe from token#2 via `SubscribeAsync(observer, token)`, assert 3
  received in order.
- Survive provider/silo restart: subscribe, restart silo, rehydration (#41) re-creates the consumer
  group, assert events published during downtime are delivered (broker retained them).
- Rewind past retention (`MaxLength` trim) → asserts `StreamRewindExpiredException`.

**Kafka (Testcontainers — Redpanda image for fast startup, or Confluent):**
- Same fan-out / replay (via `Seek`) / restart matrix.
- Partition-per-key ordering: interleave two `StreamId`s that hash to the same partition; assert each
  subscription sees only its own key, in order (validates the §4.4 key-filter).

**Unit (no broker):**
- Pump logic against fake `IRedisStreamConnection` / `IKafkaStreamConnection` (in `Quark.Tests.Unit`
  or `Quark.Tests.Fault`) — verifies decode → deliver → ack ordering, error backoff, and un-ACKed
  redrive without a live broker.

**AOT smoke (CI):**
- `dotnet publish` a minimal silo referencing each provider with `/p:PublishAot=true` (Linux x64),
  gate merge. This is the primary guard for the Kafka trim risk (§6).

---

## 8. Implementation checklist (ordered — safe top-to-bottom, no circular deps)

**Prerequisite:** #41 landed — `IAsyncStream<T>.SubscribeAsync(observer, token)` (A1),
`IStreamPubSubStore` + `StreamSubscription` (A2), `AddPersistentStreams` / `AddPubSubStore`,
rehydration hook. If #41 is not merged, these providers cannot compile against the assumed surface.

Shared:
- [ ] Pin `Confluent.Kafka` in `Directory.Packages.props`.
- [ ] Add a deterministic `StableHash(string)` helper (or confirm reuse of the placement hash) — OQ-1.
- [ ] Add `StreamRewindExpiredException` (in each provider package, or a shared abstractions spot if
      #41 already defines a rewind-failure type — reconcile with #41).

`Quark.Streaming.Redis` (first — lowest risk, validates the shared mapping):
- [ ] `Quark.Streaming.Redis.csproj` (refs per §5; `net9.0;net10.0`).
- [ ] `RedisStreamOptions` + `StreamStartPosition`.
- [ ] `RedisStreamEntryId`, `RedisStreamMessage`, `IRedisStreamConnection`.
- [ ] `RedisStreamConnection` (StackExchange.Redis; XADD/XGROUP/XREADGROUP/XACK/XDEL; BUSYGROUP-tolerant).
- [ ] `RedisStreamToken : StreamSequenceToken` (`[GenerateSerializer]`).
- [ ] `RedisStreamPump`.
- [ ] `RedisStream<T>`, `RedisStreamSubscriptionHandle<T>`, `RedisStreamProvider`.
- [ ] `RedisStreamingServiceCollectionExtensions` (`AddRedisStreams` + `AddKeyedRedisStreams`).
- [ ] Integration + unit tests (§7); AOT smoke.

`Quark.Streaming.Kafka` (second — reuses proven shapes from the Redis provider):
- [ ] `Quark.Streaming.Kafka.csproj`.
- [ ] `KafkaStreamOptions`.
- [ ] `KafkaStreamMessage`, `IKafkaStreamConnection`, `IKafkaPartitionReader`.
- [ ] `KafkaStreamConnection` (byte[] serdes; manual assign/seek/commit; topic auto-create).
- [ ] `KafkaStreamToken : StreamSequenceToken` (`[GenerateSerializer]`).
- [ ] `KafkaStreamPump` (dedicated thread).
- [ ] `KafkaStream<T>`, `KafkaStreamSubscriptionHandle<T>`, `KafkaStreamProvider`.
- [ ] `KafkaStreamingServiceCollectionExtensions`.
- [ ] Integration + unit tests; **AOT smoke gating (trim risk)**.

Docs:
- [ ] `wiki/Streaming.md` — add "Broker-backed providers" section; note retention-bounded replay.
- [ ] `FEATURES.md` — mark #92 / #106 status.

---

## 9. Resolved design decisions

- **DD-1 (#92 core question) — Redis Pub/Sub vs Redis Streams → Streams.** `IAsyncStream<T>` +
  `StreamSequenceToken` imply replay/resume-from-token, which fire-and-forget Pub/Sub cannot provide
  (no durability, no offsets, missed messages on disconnect). Redis Streams give durable, ordered,
  offset-addressable logs with consumer groups — the exact primitives #41 needs. Pub/Sub is rejected.
- **DD-2 — one consumer group per subscription (broadcast), not competing consumers.** Quark stream
  semantics are fan-out: every subscriber sees every event. Group-per-`subscriptionId` gives each
  subscription an independent cursor. (§2.3)
- **DD-3 — dedicated token types per provider**, not reused `SequentialToken`, so cross-provider
  `CompareTo` throws (isolation), and each token carries broker-native positioning (entry-id / offset).
- **DD-4 — the broker is the durable event log; no separate `IGrainStorage` event log.** Avoids
  double-writes; replay is broker-native. Trade-off: replay horizon = broker retention, surfaced as
  `StreamRewindExpiredException`. `IStreamPubSubStore` is still used for subscription durability +
  rehydration. (§2.4)
- **DD-5 — Kafka: namespace→topic, key→partition** (deterministic hash), not topic-per-`StreamId`,
  to keep topic count bounded while preserving per-stream ordering. (§2.1, §4.4)
- **DD-6 — Kafka payloads are opaque `byte[]`** via Confluent's byte-array serdes, bypassing its
  reflection-based default serializers — the single biggest AOT win for the Kafka package. (§6)
- **DD-7 — providers are silo-side only.** Client consumers keep using the existing TCP gateway push.

---

## 10. Dependencies & related work

- **Blocked on #41** (persistent streams): the `SubscribeAsync(observer, token)` overload,
  `IStreamPubSubStore` + `StreamSubscription`, `AddPersistentStreams` / `AddPubSubStore`, and the
  activation-time rehydration step. This spec's assumptions A1–A4 track exactly what must be true in
  #41; reconcile on merge.
- **#16** (implicit-subscription rehydration) — broker providers re-bind `[ImplicitStreamSubscription]`
  grains on restart through the #41 rehydration hook; coordinate the re-bind contract.
- **Reuses `Quark.Persistence.Redis` conventions** — `IRedis*Connection` seam, `IOptions`,
  keyed-provider pattern, single multiplexer per named provider.
- **`Directory.Packages.props`** — add `Confluent.Kafka` pin; `StackExchange.Redis` already present.
- **Sibling providers for the PubSubStore** — `Quark.Persistence.InMemory` / `.Redis` / AdoNet
  (`2026-07-02-adonet-grain-storage-design.md`) all satisfy the `IGrainStorage`-backed PubSub store.

### Open questions
- **OQ-1** — is there an existing deterministic string-hash helper (placement `[HashBasedPlacement]`)
  to reuse for `StreamId.Key → partition`, or add a new internal `StableHash`?
- **OQ-2** — Kafka read-amplification when multiple `StreamId`s share a partition: accept the
  key-filter drop, add a header-based server filter, or allow one-topic-per-stream for
  low-cardinality namespaces via an option?
- **OQ-3** — Kafka offset commit cadence: per-message (safest, slowest) vs batched-on-interval
  (faster, at-least-once with wider redelivery window on crash)?
- **OQ-4** — v2 pooled/multiplexed silo-level broker reader to cap consumer count under high
  subscription fan-out — in scope for this issue or a follow-up?
- **OQ-5** — does #41 own `StreamRewindExpiredException` (shared) or does each broker provider define
  its own retention-expiry exception?
