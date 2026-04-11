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
| `Quark.Runtime` | Silo-side runtime: lifecycle, directory, activator, scheduling | 🔄 M3 In Progress |
| `Quark.Client` | Client-side runtime: connection, grain reference resolution | ⏳ M3 Planned |
| `Quark.Server` | Server hosting entry-point | ⏳ M3 Planned |
| `Quark.CodeGenerator` | Roslyn source generators: grain proxies + serializers + activators | 🔄 M2 In Progress |
| `Quark.Analyzers` | AOT-safety Roslyn analyzers (QRK0001-QRK0003) | ✅ M2 Done (scaffold) |
| `Quark.Testing` | Multi-silo in-process test harness | ⏳ M5 Planned |

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
- [ ] **`SerializerGenerator`** — real codegen: inspect `[Id]` members, emit `IFieldCodec<T>` + `IDeepCopier<T>`
- [ ] **`GrainProxyGenerator`** — real codegen: emit grain proxy classes per `IGrain` interface
- [ ] Activator generator (emits `IGrainActivatorFactory` registrations)
- [ ] CI gate: `dotnet publish --aot` smoke build + trim analyzer warnings
- [ ] `Quark.Tests.CodeGenerator` — Roslyn compilation-based generator tests

### 🔄 Milestone 3 — Runtime MVP (Tier 1)
- [x] `SiloAddress` — value type `(Host, Port)` with `ToString` and parse
- [x] `LifecycleSubject` — concrete ordered `ILifecycleSubject` (ascending start / descending stop)
- [x] `IGrainTypeRegistry` + `GrainTypeRegistry` — maps `GrainType` key → CLR `Type`
- [x] `IGrainActivator` + `DefaultGrainActivator` — DI-backed grain creation
- [x] `IGrainDirectory` + `InMemoryGrainDirectory` — `ConcurrentDictionary` backed
- [x] `GrainContext` — concrete `IGrainContext` with lifecycle and deactivation
- [x] `SiloRuntimeOptions` — cluster id, service id, silo name, endpoints
- [x] `SiloHostedService` — `IHostedService` driving silo lifecycle start/stop
- [x] `IGrainCallInvoker` (core abstractions) — routes calls from proxy to runtime
- [x] `RuntimeServiceCollectionExtensions` — `AddQuarkRuntime()`
- [ ] Message pump / dispatcher — receive `MessageEnvelope`, route to grain context
- [ ] Scheduler — per-grain single-threaded task execution
- [ ] Client connection — connect to silo, send calls, receive responses
- [ ] Placement engine — `RandomPlacement` + `LocalPlacement` strategies
- [ ] End-to-end integration test: client → silo → grain activation → response
- [ ] `Quark.Tests.Integration` project

### ⏳ Milestone 4 — Persistence & Provider Model (Tier 2)
- [ ] `Quark.Persistence.Abstractions`: `IStorage<TState>`, `IGrainStorage`, `StorageOptions`
- [ ] `Quark.Persistence.InMemory`: in-memory storage provider
- [ ] `Quark.Persistence.Redis` (or chosen durable backend): real persistence
- [ ] Testcontainers matrix for integration tests
- [ ] `IPersistentGrain<TState>` mixin on `Grain` base

### ⏳ Milestone 5 — Test Kit & Distributed Validation
- [ ] `Quark.Testing` full implementation: `TestCluster`, `TestSilo`, `TestClient`
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

# AOT smoke build (add to CI)
dotnet publish src/Quark.Runtime/Quark.Runtime.csproj -r linux-x64 --aot
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

*Last updated: Milestone 3 in progress*
