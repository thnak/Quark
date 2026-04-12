# Quark — Prioritized Implementation Ticket Backlog

**Scope:** Tier 2 (production core, M4–M5) and Tier 3 (ecosystem parity, M6) remaining work.  
**Priority key:** P1 = blocking or critical path · P2 = high value, independent · P3 = important, deferrable  
**Compatibility key:** Drop-in = same as Orleans · Minor-change = same concept, different wiring · Quark-native = new concept

---

## Tier 2 — Production Core (M4–M5)

### T2-01 · Testcontainers provider matrix  
**Priority:** P1  
**Milestone:** M4  
**Packages:** `Quark.Persistence.Redis`, `Quark.Tests.Integration`  
**Compatibility:** Quark-native  

**Description:**  
Wire up [Testcontainers for .NET](https://dotnet.testcontainers.org/) to spin up real Redis containers during integration test runs. The in-memory provider is already tested; this ticket validates the durable path.

**Acceptance criteria:**
- `Quark.Tests.Integration` uses `Testcontainers.Redis` to spin up a container per test collection
- Persistence round-trip tests pass against a live Redis instance in CI
- Container lifecycle is managed automatically (start on fixture create, stop on dispose)
- Test run remains green when no Redis is present by skipping with `[Trait("category","integration")]`

**Dependencies:** none — Redis provider is already implemented  
**Files / packages to touch:** `tests/Quark.Tests.Integration/`, `Directory.Packages.props`

---

### T2-02 · Multi-silo test harness completion (`Quark.Testing`)  
**Priority:** P1  
**Milestone:** M5  
**Packages:** `Quark.Testing`  
**Compatibility:** Drop-in (mirrors `Orleans.TestingHost.TestCluster`)  

**Description:**  
Complete `Quark.Testing` so that `TestCluster` can orchestrate multiple in-process `TestSilo` instances. Currently only the baseline scaffold exists. This is a prerequisite for all placement, failure, and distributed validation tests.

**Acceptance criteria:**
- `TestCluster.Deploy(siloCount: N)` starts N in-process silos sharing a local grain directory
- `TestCluster.GrainFactory` resolves grain proxies against the cluster
- `TestCluster.StopSiloAsync(index)` / `RestartSiloAsync(index)` work deterministically
- `TestCluster` implements `IAsyncDisposable` and cleans up on dispose
- Basic smoke test: 2-silo cluster, grain resolved, call routed, silo stopped gracefully

**Dependencies:** T2-01 (Testcontainers for durable fixtures is a parallel concern but harness must work standalone)  
**Files / packages to touch:** `src/Quark.Testing/`

---

### T2-03 · Placement validation test suite  
**Priority:** P1  
**Milestone:** M5  
**Packages:** `Quark.Tests.Integration`, `Quark.Runtime`  
**Compatibility:** Quark-native test coverage  

**Description:**  
Automated tests that assert a grain activation ends up on the expected silo given a declared placement strategy. Requires the multi-silo harness (T2-02) to be in place.

**Acceptance criteria:**
- `RandomPlacement` test: grains with random placement spread across ≥2 silos over N activations
- `PreferLocalPlacement` test: grain activated from silo A activates on silo A when possible
- `HashBasedPlacement` test: same grain key always activates on the same silo
- Tests are deterministic and repeatable in CI (no flakiness from timing)

**Dependencies:** T2-02  
**Files / packages to touch:** `tests/Quark.Tests.Integration/Placement/`

---

### T2-04 · Failure and recovery test suite  
**Priority:** P1  
**Milestone:** M5  
**Packages:** `Quark.Tests.Integration`, `Quark.Runtime`  
**Compatibility:** Quark-native test coverage  

**Description:**  
Tests that exercise silo crash/restart, activation loss, and grain directory rebuild. These are the distributed reliability gate for Tier 2.

**Acceptance criteria:**
- Stopping a silo mid-call returns a well-typed exception to the caller (not a raw connection error)
- After a silo stops, re-requesting the same grain re-activates it on a surviving silo
- `InMemoryGrainDirectory` correctly removes activations for the stopped silo
- A restarted silo re-registers its existing activations (or starts clean)
- All tests pass using the `TestCluster` harness (T2-02)

**Dependencies:** T2-02  
**Files / packages to touch:** `tests/Quark.Tests.Integration/Resilience/`

---

### T2-05 · Retry and timeout policy model  
**Priority:** P2  
**Milestone:** M4–M5  
**Packages:** `Quark.Core.Abstractions`, `Quark.Client`, `Quark.Runtime`  
**Compatibility:** Minor-change (Orleans uses `RequestContext` + silo options; Quark will use options pattern)  

**Description:**  
Add configurable call timeout and retry semantics for grain calls. In Orleans these are controlled via `SiloOptions.ResponseTimeout` and filter pipelines. Quark should expose a comparable `GrainCallOptions` / call interceptor hook.

**Acceptance criteria:**
- `GrainCallOptions` record in `Quark.Core.Abstractions` with `Timeout`, `MaxRetries`, `RetryDelay`
- `ISiloBuilder.ConfigureGrainCalls(Action<GrainCallOptions>)` extension
- `LocalGrainCallInvoker` respects the configured timeout (throws `TimeoutException` on breach)
- Retry policy is pluggable via an `IGrainCallFilter` interface (at minimum: before/after/on-exception hooks)
- Unit tests: timeout fires, retry succeeds on second attempt, retry exhaustion throws

**Dependencies:** none  
**Files / packages to touch:** `src/Quark.Core.Abstractions/`, `src/Quark.Runtime/`, `src/Quark.Client/`

---

### T2-06 · Observability baseline (metrics, tracing, logging)  
**Priority:** P2  
**Milestone:** M5–M6  
**Packages:** `Quark.Runtime`, `Quark.Client`  
**Compatibility:** Minor-change (Orleans uses `Orleans.Runtime.Telemetry` + OpenTelemetry; Quark will use `System.Diagnostics.Metrics` + `ActivitySource`)  

**Description:**  
Add structured observability hooks throughout the runtime and client pipeline, compatible with OpenTelemetry collection. Orleans exposes `System.Diagnostics.Metrics` meters and `Activity` spans; Quark should follow the same model.

**Acceptance criteria:**
- `Meter("Quark.Runtime")` emitted: `grain.activations`, `grain.deactivations`, `grain.calls`, `grain.call.duration`
- `ActivitySource("Quark.Runtime")` wraps grain call dispatch (start/stop with `GrainId` tag)
- `ILogger<T>` injected at all key lifecycle points (silo start/stop, activation, deactivation, call errors)
- `AddQuarkRuntime()` DI extension optionally accepts `Action<QuarkObservabilityOptions>` to configure meter/source names
- Verified by a unit test that instruments a `Meter.CreateObservableGauge` and checks it reports values

**Dependencies:** none  
**Files / packages to touch:** `src/Quark.Runtime/`, `src/Quark.Client/`

---

### T2-07 · CI deterministic distributed test helpers  
**Priority:** P2  
**Milestone:** M5  
**Packages:** `Quark.Testing`  
**Compatibility:** Quark-native  

**Description:**  
Helper utilities inside `Quark.Testing` that make multi-silo tests fully deterministic in CI: fixed-port allocation, controlled clock injection, and deterministic random seeds for placement.

**Acceptance criteria:**
- `TestClusterOptions.UseFixedPorts(startPort)` allocates sequential ports without OS contention
- Placement strategies accept an `IRandom` abstraction so tests can supply a seeded instance
- `TestClock` injectable into reminder/timer paths (foundation for T3-02)
- GitHub Actions workflow passes `Quark.Tests.Integration` on every PR without flakiness

**Dependencies:** T2-02  
**Files / packages to touch:** `src/Quark.Testing/`, `.github/workflows/`

---

## Tier 3 — Ecosystem Parity (M6)

### T3-01 · Membership and clustering provider model  
**Priority:** P1  
**Milestone:** M6  
**Packages:** `Quark.Core.Abstractions`, `Quark.Runtime`, new `Quark.Clustering.Abstractions`  
**Compatibility:** Minor-change (Orleans uses `IMembershipTable`; Quark will define `IClusterMembershipProvider`)  

**Description:**  
Introduce a membership provider abstraction so that silos can discover each other and maintain liveness state via an external store. Without this, multi-silo deployment beyond a single process is not possible. Orleans supports multiple backends (ADO.NET, Consul, Redis, Kubernetes, Azure); Quark should mirror the same provider pattern.

**Acceptance criteria:**
- `IClusterMembershipProvider` contract in `Quark.Clustering.Abstractions`: `RegisterSiloAsync`, `UnregisterSiloAsync`, `GetMembersAsync`, `WatchAsync`
- `InMemoryClusterMembershipProvider` for single-host / test use
- `ISiloBuilder.UseClusterMembership<TProvider>()` DI extension
- Silo startup registers itself; shutdown unregisters; crash detection via heartbeat TTL
- Foundation for at least one durable provider (Redis recommended as it is already a dependency)

**Dependencies:** T2-02 (test harness required for multi-silo integration testing)  
**Files / packages to touch:** new `src/Quark.Clustering.Abstractions/`, `src/Quark.Runtime/`

---

### T3-02 · Reminders and in-process timers  
**Priority:** P1  
**Milestone:** M6  
**Packages:** `Quark.Core.Abstractions`, `Quark.Runtime`, new `Quark.Reminders.Abstractions`  
**Compatibility:** Drop-in (Orleans: `IRemindable`, `IGrainReminder`, `IReminderRegistry`, `ITimerRegistry`, `RegisterGrainTimer`)  

**Description:**  
Implement the two scheduling primitives Orleans exposes. **Timers** are lightweight, ephemeral, in-process (`ITimerRegistry.RegisterGrainTimer`) — they do not survive silo restart. **Reminders** are durable, cluster-aware, and survive restarts (`IReminderRegistry.RegisterOrUpdateReminder`) — they require a persistent store (Redis/ADO.NET). Both must be grain-context-scoped and deactivated automatically when a grain deactivates.

**Acceptance criteria:**
- `ITimerRegistry.RegisterGrainTimer(IGrainContext, callback, state, GrainTimerCreationOptions)` in `Quark.Core.Abstractions`
- Timer fires at the configured `DueTime` + `Period`; cancelled on grain deactivation
- `IRemindable` marker interface; `Grain` can implement it to receive `ReceiveReminder(name, status)` callbacks
- `IReminderRegistry.RegisterOrUpdateReminder / UnregisterReminder` in `Quark.Core.Abstractions`
- In-memory reminder provider for testing (no persistence needed)
- Durable reminder provider contract wired into the membership/persistence model
- Unit tests: timer fires, timer cancels on deactivation; reminder fires after configured period

**Dependencies:** T3-01 for durable reminder store wiring; T2-07 for deterministic test clock  
**Files / packages to touch:** new `src/Quark.Reminders.Abstractions/`, `src/Quark.Runtime/`, `src/Quark.Core.Abstractions/`

---

### T3-03 · Streaming baseline (`IAsyncStream<T>`)  
**Priority:** P2  
**Milestone:** M6  
**Packages:** new `Quark.Streaming.Abstractions`, new `Quark.Streaming.InMemory`  
**Compatibility:** Drop-in (Orleans: `IAsyncStream<T>`, `StreamId`, `IStreamProvider`, `IAsyncObserver<T>`, `IAsyncObservable<T>`)  

**Description:**  
Add a reactive streaming subsystem. Streams are identified by `StreamId` (namespace + key), are provider-backed (pluggable queue/bus implementations), and can be consumed by both grains and clients symmetrically. The in-memory provider is the first target; external providers (Kafka, Azure Service Bus, etc.) come later.

**Acceptance criteria:**
- `IStreamProvider` with `GetStream<T>(StreamId)` in `Quark.Streaming.Abstractions`
- `IAsyncStream<T>` exposes `PublishAsync(item)` and `SubscribeAsync(IAsyncObserver<T>)`
- `StreamId` value type with `(string Namespace, string Key)`
- `IStreamProvider` registered via `ISiloBuilder.AddStreamProvider<TProvider>(name)`
- `Quark.Streaming.InMemory` in-memory pub/sub provider for tests
- Subscription survives grain deactivation/reactivation (persisted sub store optional for M6 baseline)
- Unit tests: publish, subscribe, receive; unsubscribe; multiple subscribers

**Dependencies:** T2-02 (multi-silo harness for producer/consumer on different silos)  
**Files / packages to touch:** new `src/Quark.Streaming.Abstractions/`, new `src/Quark.Streaming.InMemory/`

---

### T3-04 · Advanced placement strategies  
**Priority:** P2  
**Milestone:** M6  
**Packages:** `Quark.Core.Abstractions`, `Quark.Runtime`  
**Compatibility:** Drop-in (`[ActivationCountBasedPlacement]`, `[ResourceOptimizedPlacement]` in Orleans)  

**Description:**  
Extend the existing placement strategy family (`RandomPlacement`, `PreferLocalPlacement`, `HashBasedPlacement`) with activation-count-based and resource-aware strategies that enable load-driven grain distribution across silos.

**Acceptance criteria:**
- `IPlacementDirector` abstraction in `Quark.Core.Abstractions` (if not already present) with `ChooseSiloAsync(placementContext)`
- `ActivationCountPlacementStrategy` + `[ActivationCountBasedPlacement]` attribute: routes to silo with fewest activations
- `ResourceAwarePlacementStrategy` + `[ResourceOptimizedPlacement]` attribute: routes using CPU/memory heuristic hook (simple first implementation: forward to activation-count strategy)
- Each silo exposes an `ISiloStatsCollector` that publishes activation counts for placement decisions
- Placement validation tests (T2-03) extended to cover new strategies

**Dependencies:** T3-01 (cluster membership required so placement directors can enumerate live silos)  
**Files / packages to touch:** `src/Quark.Core.Abstractions/`, `src/Quark.Runtime/`

---

### T3-05 · Serializer version tolerance and rolling upgrade  
**Priority:** P2  
**Milestone:** M6  
**Packages:** `Quark.Serialization.Abstractions`, `Quark.Serialization`, `Quark.CodeGenerator`  
**Compatibility:** Drop-in (Orleans: `[Id]` field stability contract; forward/backward compatible field encoding)  

**Description:**  
Ensure that adding or removing `[Id]`-annotated fields in a grain state or message type does not break serialization across a rolling upgrade. This requires: unknown field skipping, missing field defaults, and a test matrix that serializes with version N and deserializes with version N+1 and N-1.

**Acceptance criteria:**
- `CodecReader` skips unknown field IDs (forward compatibility)
- Missing fields on deserialization use type default values (backward compatibility)
- `[Alias]` on a type survives a rename without breaking wire format
- Code generator emits stable IDs when `[Id]` is present; fails/warns on missing `[Id]` in `[GenerateSerializer]` types
- Rolling upgrade integration test: serialize with assembly V1 types, deserialize with V2 types in same test
- Analyzer QRK0004: warn on `[GenerateSerializer]` type with members missing `[Id]`

**Dependencies:** none (serialization is standalone)  
**Files / packages to touch:** `src/Quark.Serialization/`, `src/Quark.CodeGenerator/`, `src/Quark.Analyzers/`

---

### T3-06 · Transactional state extension point  
**Priority:** P3  
**Milestone:** M6  
**Packages:** new `Quark.Transactions.Abstractions`, `Quark.Runtime`  
**Compatibility:** Drop-in (Orleans: `ITransactionalState<T>`, `[TransactionalState]`, `[Transaction(TransactionOption)]`)  

**Description:**  
Add an optional, opt-in distributed ACID transaction subsystem. In Orleans, `ITransactionalState<T>` injects a transactional wrapper around grain state, and grains annotate methods with `[Transaction(TransactionOption.Create|Join|Suppress)]`. This is a complex, optional subsystem — the ticket covers the abstraction contracts and a single-silo implementation first.

**Acceptance criteria:**
- `ITransactionalState<T>` interface in `Quark.Transactions.Abstractions`: `PerformRead<TResult>`, `PerformUpdate`
- `[TransactionalState(name, storageName)]` attribute for DI injection
- `[Transaction(TransactionOption)]` method attribute; values: `Create`, `Join`, `Suppress`, `CreateOrJoin`
- Single-silo in-memory transaction coordinator (no distributed 2PC required for baseline)
- Grain can inject `ITransactionalState<T>` and participate in a transaction spanning multiple methods on the same silo
- Unit tests: read-within-transaction sees uncommitted writes; abort rolls back; commit makes changes visible
- Integration test: bank transfer pattern across two grains as in Orleans docs

**Dependencies:** T2-05 (call filter/interceptor needed to propagate transaction context)  
**Files / packages to touch:** new `src/Quark.Transactions.Abstractions/`, `src/Quark.Runtime/`

---

### T3-07 · Performance and memory regression suite  
**Priority:** P3  
**Milestone:** M6  
**Packages:** new `tests/Quark.Tests.Benchmarks`  
**Compatibility:** Quark-native  

**Description:**  
Add a BenchmarkDotNet benchmark suite that covers the hot paths in grain calls, serialization, and placement. Results are tracked in CI as a baseline to catch regressions before they merge.

**Acceptance criteria:**
- `Quark.Tests.Benchmarks` project using BenchmarkDotNet
- Benchmarks: single-grain round-trip call, serializer encode+decode for a 10-field type, grain activation latency, 100-concurrent-call throughput
- Memory diagnostics (`[MemoryDiagnoser]`) included on each benchmark
- CI job runs benchmarks in `--job Short` mode and publishes results as a PR comment or artifact
- Baseline established from first CI run; PR blocks if p95 latency regresses >20%

**Dependencies:** T2-02 (multi-silo harness for distributed benchmarks)  
**Files / packages to touch:** new `tests/Quark.Tests.Benchmarks/`, `.github/workflows/`

---

## Priority summary

| Ticket | Title | Tier | Priority | Milestone |
|--------|-------|------|----------|-----------|
| T2-01 | Testcontainers provider matrix | 2 | P1 | M4 |
| T2-02 | Multi-silo test harness completion | 2 | P1 | M5 |
| T2-03 | Placement validation test suite | 2 | P1 | M5 |
| T2-04 | Failure and recovery test suite | 2 | P1 | M5 |
| T2-05 | Retry and timeout policy model | 2 | P2 | M4–M5 |
| T2-06 | Observability baseline | 2 | P2 | M5–M6 |
| T2-07 | CI deterministic distributed test helpers | 2 | P2 | M5 |
| T3-01 | Membership and clustering provider model | 3 | P1 | M6 |
| T3-02 | Reminders and in-process timers | 3 | P1 | M6 |
| T3-03 | Streaming baseline | 3 | P2 | M6 |
| T3-04 | Advanced placement strategies | 3 | P2 | M6 |
| T3-05 | Serializer version tolerance | 3 | P2 | M6 |
| T3-06 | Transactional state extension point | 3 | P3 | M6 |
| T3-07 | Performance and memory regression suite | 3 | P3 | M6 |

---

## Dependency graph (critical path)

```
T2-01 (Testcontainers)
   └── T2-02 (TestCluster multi-silo)
          ├── T2-03 (placement validation)
          ├── T2-04 (failure/recovery)
          ├── T2-07 (CI helpers) ─────────── T3-02 (reminders clock)
          └── T3-01 (membership model)
                 ├── T3-02 (reminders)
                 └── T3-04 (advanced placement)

T2-05 (retry/timeout) ──── T3-06 (transactions)

T3-03 (streaming) ─── T2-02

T3-05 (version tolerance) — standalone

T2-06 (observability) — standalone
T3-07 (benchmarks) ─── T2-02
```

---

*Generated from `docs/plan.md` Tier 2/3 remaining backlog · Orleans concept reference: [Context7 /dotnet/orleans](https://context7.com/dotnet/orleans/llms.txt)*
