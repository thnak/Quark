# Quark — Implementation Plan & Workspace Tracker

**Goal:** Build a Native AOT-first Orleans-compatible distributed actor framework.  
**Compatibility contract:** Orleans mental model (Grain, Silo, Client, Placement, Persistence, Streams) with API tiers:
- **Drop-in** — same attribute/interface names, same method signatures
- **Minor-change** — same concept, different registration or DI wiring
- **Quark-native** — new concept without direct Orleans equivalent

---

## Package Map

| Package | Role | Status |
|---|---|---|
| `Quark.Core.Abstractions` | Grain identity, lifecycle, placement, hosting contracts | ✅ M1 Done |
| `Quark.Serialization.Abstractions` | `IFieldCodec`, `IDeepCopier`, `IGeneralizedCodec`, `CodecWriter/Reader` | ✅ M1 Done |
| `Quark.Transport.Abstractions` | `ITransport`, `ITransportConnection`, `MessageEnvelope` | ✅ M1 Done |
| `Quark.Serialization` | 18 primitive codecs, `CodecProvider`, `QuarkSerializer`, DI extensions | ✅ M1 Done |
| `Quark.Core` | `ISiloBuilder`, `IClientBuilder`, host-builder extensions | ✅ M1 Done |
| `Quark.Transport.Tcp` | TCP `ITransport` + `ITransportConnection` (System.IO.Pipelines) | ✅ M1 Done |
| `Quark.Persistence.Abstractions` | Persistence contracts and persistent grain abstractions | ✅ M4 Core Done |
| `Quark.Persistence.InMemory` | In-memory grain state persistence provider | ✅ M4 Core Done |
| `Quark.Persistence.Redis` | Redis-backed grain state persistence provider | ✅ M4 Provider Added |
| `Quark.Runtime` | Silo-side runtime: lifecycle, directory, activator, scheduling | ✅ M3 Core Done |
| `Quark.Client` | Client-side runtime: connection, grain reference resolution | ✅ M3 Core Done |
| `Quark.Server` | Server hosting entry-point | ⏳ M3 Planned |
| `Quark.CodeGenerator` | Roslyn source generators: grain proxies + serializers + activators | ✅ M2 Done |
| `Quark.Analyzers` | AOT-safety Roslyn analyzers (QRK0001-QRK0003) | ✅ M2 Done (scaffold) |
| `Quark.Testing` | Multi-silo in-process test harness | 🔄 M5 In Progress |

---

## Milestones

### ✅ Milestone 0 — Naming & Compatibility Contract
- [x] Retain Orleans mental model: `IGrain`, `Grain`, `ISilo`, placement attributes
- [x] API compatibility tiers defined (drop-in / minor-change / Quark-native)
- [x] `ITransport` chosen (not `ITransfer`), `TcpTransport` as default
- [x] Package map mirrors Orleans structure

### ✅ Milestone 1 — Foundation Abstractions (v0 freeze)
- [x] `Quark.Core.Abstractions`: `GrainId`, `GrainType`, `IGrain` + key-typed variants, `Grain` base, lifecycle interfaces, placement attributes, `IGrainFactory`, `IClusterClient`, `IGrainContext`
- [x] `Quark.Serialization.Abstractions`: `IGeneralizedCodec/Copier`, `IFieldCodec<T>`, `IDeepCopier<T>`, `ICodecProvider/CopierProvider`, `CodecWriter`/`CodecReader` (ZigZag+LEB128), `[GenerateSerializer]`/`[Id]`/`[Alias]`
- [x] `Quark.Transport.Abstractions`: `ITransport`, `ITransportListener`, `ITransportConnection` (IDuplexPipe), `MessageEnvelope`/`MessageHeaders`/`MessageType`
- [x] `Quark.Serialization`: 18 primitive codecs, `ImmutableCopier<T>`, `CodecProvider`, `QuarkSerializer`, AOT-safe DI extensions
- [x] `Quark.Transport.Tcp`: `TcpTransport`, `TcpTransportListener`, `TcpTransportConnection` (Pipelines)
- [x] **83 unit tests passing**

### 🔄 Milestone 2 — AOT-Safe Tooling & Codegen
- [x] `Quark.Analyzers` scaffold: QRK0001 (dynamic type), QRK0002 (Assembly.Load), QRK0003 (ISerializable)
- [x] `Quark.CodeGenerator` scaffold: `SerializerGenerator`, `GrainProxyGenerator` stubs
- [x] **`SerializerGenerator`** — real codegen: inspect `[Id]` members, emit `IFieldCodec<T>` + `IDeepCopier<T>`
- [x] **`GrainProxyGenerator`** — real codegen: emit grain proxy classes per `IGrain` interface
- [x] Activator generator (emits `IGrainActivatorFactory` registrations)
- [x] CI gate: `dotnet publish --aot` smoke build + trim analyzer warnings
- [x] `Quark.Tests.CodeGenerator` — Roslyn compilation-based generator tests

### ✅ Milestone 3 — Runtime MVP (Tier 1)
- [x] `SiloAddress` — value type `(Host, Port)` with `ToString` and parse
- [x] `LifecycleSubject` — concrete ordered `ILifecycleSubject` (ascending start / descending stop)
- [x] `IGrainTypeRegistry` + `GrainTypeRegistry` — maps `GrainType` key → CLR `Type`
- [x] `IGrainActivator` + `DefaultGrainActivator` — DI-backed grain creation
- [x] `IGrainDirectory` + `InMemoryGrainDirectory` — `ConcurrentDictionary` backed
- [x] `GrainContext` — concrete `IGrainContext` with lifecycle, deactivation, `GrainFactory`, `ServiceProvider`
- [x] `SiloRuntimeOptions` — cluster id, service id, silo name, endpoints
- [x] `SiloHostedService` — `IHostedService` driving silo lifecycle start/stop + deferred registration
- [x] `IGrainCallInvoker` (core abstractions) — routes calls from proxy to runtime
- [x] `RuntimeServiceCollectionExtensions` — `AddQuarkRuntime()`, `AddGrain<T>()`, `AddGrainMethodInvoker<TGrain,TInvoker>()`
- [x] **Orleans API alignment** — `PreferLocalPlacement`, `HashBasedPlacement`, public `DeactivationReason` ctor, `DeactivationReason.Force`, `Grain.GrainFactory`, `Grain.DeactivateOnIdle()`, `Grain.DelayDeactivation()`, `IGrainFactory` non-generic overloads, `UseQuark(IHostApplicationBuilder)`, `UseLocalhostClustering()` on `ISiloBuilder`
- [x] **`IGrainMethodInvoker`** + `GrainMethodInvokerRegistry` — per-grain-type method dispatcher interface
- [x] **`GrainActivation`** — grain instance + context + sequential `Channel<Func<Task>>` scheduler
- [x] **`GrainActivationTable`** — silo-local activation registry with lazy/atomic init
- [x] **`LocalGrainCallInvoker`** — in-process `IGrainCallInvoker`: find/activate grain, post to scheduler, return result
- [x] **`LocalGrainFactory`** — `IGrainFactory` using `GrainProxyFactoryRegistry` + `GrainInterfaceTypeRegistry`
- [x] **`LocalClusterClient`** — `IClusterClient` for in-process (cohosted) scenario
- [x] **`ClientServiceCollectionExtensions`** — `AddLocalClusterClient()`, `AddGrainProxy<TInterface,TProxy>()`
- [x] **End-to-end integration tests** — 8 tests: grain activate, increment, reset, same-key identity, different-key isolation, 20 concurrent serialised calls, void methods
- [x] Message pump / dispatcher — receive `MessageEnvelope`, route to grain context (network path — M4)
- [x] Placement engine — `RandomPlacement` + `PreferLocalPlacement` strategies (M4)
- [x] `Quark.Tests.Integration` project (separate from unit tests — M4)

### ⏳ Milestone 4 — Persistence & Provider Model (Tier 2)
- [x] `Quark.Persistence.Abstractions`: `IStorage<TState>`, `IGrainStorage`, `StorageOptions`
- [x] `Quark.Persistence.InMemory`: in-memory storage provider
- [x] `Quark.Persistence.Redis` (or chosen durable backend): real persistence
- [ ] Testcontainers matrix for integration tests
- [x] `IPersistentGrain<TState>` mixin on `Grain` base

### ⏳ Milestone 5 — Test Kit & Distributed Validation
- [x] `Quark.Testing` core harness baseline: `TestCluster`, `TestSilo`, `TestClient`
- [ ] Multi-silo in-process orchestration
- [ ] Placement validation tests (assert grain lands on expected silo)
- [ ] Persistence round-trip tests
- [ ] Failure/recovery tests (silo crash, restart, directory rebuild)
- [ ] Deterministic cluster helpers for CI

### ⏳ Milestone 6 — Advanced Reliability / Tier 3 Features
- [ ] Membership/clustering (gossip or external store)
- [ ] Reminders + timers
- [ ] Streaming baseline (`IAsyncStream<T>`)
- [ ] Observability hooks (metrics, traces, structured logging)
- [ ] Rolling upgrade / version tolerance for serializer contracts
- [ ] Performance + memory regression suite

### ⏳ Milestone 7 — Migration & Adoption
- [ ] "Orleans to Quark" migration guide (feature-tier table)
- [ ] Compatibility shims for common Orleans hosting + grain patterns
- [ ] Sample: minimal app
- [ ] Sample: multi-silo app
- [ ] Sample: persistent grain app
- [ ] Sample: client-heavy app

---

## Parallel Workstreams (after M1 freeze)

| Track | Focus | Status |
|---|---|---|
| A | Runtime messaging / scheduling | 🔄 M3 |
| B | Serialization built-ins + custom codec pipeline | ✅ M1 done; codegen in M2 |
| C | Transport (`TcpTransport` first, others later) | ✅ M1 TCP scaffold |
| D | Persistence providers + Testcontainers | ⏳ M4 |
| E | Source generators / analyzers + compatibility tooling | 🔄 M2 |
| F | Test Kit + distributed scenario suites | ⏳ M5 |

---

## Feature Tiers

| Tier | Features | Target Milestone |
|---|---|---|
| **Tier 1** (must-have) | Grain calls, multi-silo, client, basic placement, primitive serialization | M3 |
| **Tier 2** (production core) | Persistence, retries/timeouts, observability, test kit | M4–M5 |
| **Tier 3** (ecosystem parity) | Reminders, streams, advanced placement, transactions-like extensions | M6 |

---

## Remaining Tier Completion Plan (Orleans Concept Aligned)

### Tier 2 — Production Core (M4–M5)
- [ ] **Retry/timeout policy model** aligned to Orleans reliability expectations for grain calls and client calls
- [ ] **Observability baseline**: metrics + distributed tracing + structured logging hooks on runtime/client pipeline
- [ ] **Testcontainers matrix** for durable providers (persistence + reminders storage path)
- [ ] **Multi-silo test harness completion** using Orleans `TestCluster` style ergonomics in `Quark.Testing`
- [ ] **Placement validation suite** (random, prefer-local, hash-based) with deterministic assertions
- [ ] **Failure/recovery suite** (silo stop/restart, activation recovery, directory rebuild behavior)
- [ ] **CI deterministic distributed test helpers** for repeatable cluster outcomes

### Tier 3 — Ecosystem Parity (M6)
- [ ] **Membership/clustering provider model** (Orleans-style external membership stores such as ADO.NET/Consul/Redis class of providers)
- [ ] **Reminders + timers split** (persistent reminders vs in-memory timers, matching Orleans mental model)
- [ ] **Streaming baseline** (`IAsyncStream<T>`-equivalent abstraction + provider model + pub/sub store option)
- [ ] **Advanced placement strategies** (activation-count/resource-aware strategy family beyond random/prefer-local)
- [ ] **Version tolerance hardening** for serializer contracts and rolling upgrade scenarios
- [ ] **Transactional state extension point** (Orleans `ITransactionalState`-style optional capability)
- [ ] **Performance/memory regression suite** with tier-gated baselines

### Orleans concepts referenced from Context7 (for implementation framing)
- Cluster/Silo/Grain runtime model with built-in timers, reminders, streams, persistence, transactions
- Grain placement strategy model (default random + specialized placement attributes/strategies)
- Membership & clustering provider pattern (e.g., external coordination stores/providers)
- Timers and reminders as separate runtime services (ephemeral vs persistent scheduling)
- Streams as provider-backed reactive pipeline abstraction with grain/client symmetry
- TestCluster-based distributed test ergonomics for multi-silo validation
- Transactional state as distributed ACID optional subsystem

---

## Definition of Success
- [ ] AOT-first by default — no reflection-dependent runtime path required
- [ ] Multi-silo + client + persistence validated in CI via Testcontainers
- [ ] Users can port a basic Orleans app with minimal API changes

---

## Build & Test Commands

```bash
# Build the full solution
dotnet build Quark.slnx

# Run unit tests
dotnet test tests/Quark.Tests.Unit/Quark.Tests.Unit.csproj

# Run all tests
dotnet test Quark.slnx

# Run integration-tier tests
dotnet test tests/Quark.Tests.Integration/Quark.Tests.Integration.csproj

# AOT smoke build (Windows/local)
dotnet publish src/Quark.Runtime/Quark.Runtime.csproj -f net10.0 -c Release -r win-x64 /p:PublishAot=true

# AOT smoke build (Linux CI runner)
dotnet publish src/Quark.Runtime/Quark.Runtime.csproj -f net10.0 -c Release -r linux-x64 /p:PublishAot=true
```

---

## AOT Rules (for contributors)
1. Prefer source generation over runtime reflection
2. Annotate unavoidable dynamic behavior with `[RequiresUnreferencedCode]` / `[RequiresDynamicCode]`
3. Guard JIT-only paths with `RuntimeFeature.IsDynamicCodeSupported`
4. Prefer `[UnsafeAccessor]` over `DynamicMethod` for private member access
5. Never introduce `ISerializable`-based patterns
6. Use explicit provider registration — no trim-unsafe assembly scanning

---

*Last updated: Tier 2/3 remaining backlog expanded with Orleans concept alignment*
