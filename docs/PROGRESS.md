# Quark Development Progress

## Overview
Quark is a high-performance, distributed virtual actor framework for .NET 10+ with Native AOT support.

## Current Status: Phase 2 In Progress

### ✅ Phase 1: Core Local Runtime - COMPLETE
**Goal:** AOT-compatible code generation and basic actor lifecycle

#### Completed Features:
1. **Source Generation (AOT-First)**
   - ActorSourceGenerator - Generates factory methods for [Actor] attribute
   - StateSourceGenerator - Generates Load/Save/Delete methods for [QuarkState]
   - Module initializer for auto-registration
   - No reflection at runtime

2. **Turn-based Mailbox**
   - IMailbox interface
   - ChannelMailbox using System.Threading.Channels
   - High-performance bounded channels
   - Turn-based processing (one message at a time)
   - Graceful start/stop with cancellation

3. **Actor Lifecycle Management**
   - OnActivateAsync / OnDeactivateAsync
   - DI scoping support with IServiceScope
   - Service lifetime management per actor
   - Automatic cleanup on deactivation

4. **Local Context & Tracing**
   - IActorContext interface
   - ActorContext with AsyncLocal propagation
   - CorrelationId for distributed tracing
   - RequestId for request tracking
   - Metadata dictionary for custom context

5. **Supervision Hierarchies**
   - ISupervisor interface
   - OnChildFailureAsync
   - SpawnChildAsync / GetChildren
   - SupervisionDirective (Resume, Restart, Stop, Escalate)
   - Parent-child actor relationships

6. **Persistence Abstractions**
   - IStateStorage<T> interface
   - IStateStorageProvider registry
   - InMemoryStateStorage implementation
   - StateStorageProvider with multi-storage support
   - StatefulActorBase for actors with state

**Test Coverage:** 33/33 tests passing ✅

---

### 🚧 Phase 2: Cluster & Networking Layer - IN PROGRESS
**Goal:** Silo-to-Silo communication and distributed actor routing

#### Completed Abstractions:
1. **Cluster Membership**
   - SiloInfo - Node representation (SiloId, Address, Port, Status, LastHeartbeat)
   - IClusterMembership - Membership management interface
   - SiloStatus enum (Joining, Active, ShuttingDown, Dead)
   - Events for silo join/leave

2. **Actor Directory**
   - ActorLocation - Actor placement tracking
   - IActorDirectory - Actor location lookup
   - Support for distributed actor routing

3. **Transport Layer**
   - ActorInvocationRequest - Remote method invocation
   - ActorInvocationResponse - Success/failure responses
   - IActorTransport - Pluggable transport abstraction

#### Next Steps:
- [ ] Implement InMemoryClusterMembership (for testing)
- [ ] Implement InMemoryActorDirectory
- [ ] Implement InMemoryTransport
- [ ] Add placement policies (Random, PreferLocal, StatelessWorker)
- [ ] Implement QUIC-based transport
- [ ] Add remote actor proxy generation

---

### 📋 Phase 3: Reliability & Supervision (Planned)
**Goal:** Advanced failure handling and reentrancy

- Call-Chain Reentrancy prevention
- Consistent Hashing for placement
- Cluster health monitoring
- Automatic silo eviction

---

### 📋 Phase 4: Persistence & Temporal Services (Planned)
**Goal:** Making state and time durable

- Reminders (persistent timers)
- Timers (in-memory volatile timers)
- State Providers for SQL, Redis, Mongo
- Event Sourcing support

---

### 📋 Phase 5: Reactive Streaming (Planned)
**Goal:** Decoupled data broadcasting

- Explicit Streams (manual Pub/Sub)
- Implicit Streams (auto-activation)

**Note:** Advanced backpressure and flow control deferred to Phase 8.

---

## Project Structure

```
Quark/
├── src/
│   ├── Quark.Abstractions/           # Pure interfaces and contracts
│   │   ├── IActor, ISupervisor
│   │   ├── IActorContext, IMailbox
│   │   ├── Clustering/               # Cluster abstractions
│   │   ├── Transport/                # Transport abstractions
│   │   └── Persistence/              # State management
│   │
│   ├── Quark.Core.Actors/            # Actor runtime
│   │   ├── ActorBase, StatefulActorBase
│   │   ├── ActorFactory, ActorContext
│   │   ├── ChannelMailbox
│   │   └── ActorMessage
│   │
│   ├── Quark.Core.Persistence/       # Persistence implementations
│   │   ├── InMemoryStateStorage
│   │   └── StateStorageProvider
│   │
│   ├── Quark.Generators/             # Source generators
│   │   ├── ActorSourceGenerator
│   │   └── StateSourceGenerator
│   │
│   └── Quark.Core/                   # Meta-package
│
├── tests/
│   └── Quark.Tests/                  # Unit tests (33 tests)
│
└── examples/
    ├── Quark.Examples.Basic/         # Basic usage
    └── Quark.Examples.Supervision/   # Supervision hierarchies
```

---

## Key Technical Decisions

1. **No Reflection** - 100% source generation for AOT compatibility
2. **Clean Architecture** - Clear separation of concerns
3. **System.Threading.Channels** - High-performance mailbox
4. **AsyncLocal** - Context propagation across async boundaries
5. **DI Scoping** - One scope per actor instance
6. **Pluggable Everything** - Transport, membership, storage

---

## Test Status

| Phase | Tests | Status |
|-------|-------|--------|
| Phase 1 | 33/33 | ✅ PASS |
| Phase 2 | TBD | 🚧 |

---

## Building & Running

```bash
# Build
dotnet build

# Run tests
dotnet test

# Run basic example
dotnet run --project examples/Quark.Examples.Basic

# Run supervision example
dotnet run --project examples/Quark.Examples.Supervision
```

---

## Documentation

- [Source Generator Setup](SOURCE_GENERATOR_SETUP.md)
- [Development Roadmap](plainnings/README.md)
- [README](../README.md)

---

*Last Updated: 2026-01-29*
*Status: Phase 1 Complete, Phase 2 In Progress*
