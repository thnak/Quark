# **⚛️ Quark**

**Quark** is a high-performance, distributed virtual actor framework built for **.NET 10+** with a focus on **Native AOT**, ultra-low latency, and modern developer experience.

Inspired by Microsoft Orleans and Akka.NET, Quark aims to bridge the gap between "Virtual Actors" and "Supervision Hierarchies" while maintaining a zero-reflection, hardware-accelerated footprint.

## **🚀 Key Philosophies**

* **AOT First:** No Reflection.Emit. Everything is generated at build time via Incremental Source Generators.  
* **Secure by Default:** UDP-based communication via QUIC (TLS 1.3).  
* **Control & Density:** Explicit lifecycle management to support high-density container environments.  
* **Plug-and-Play:** Modular persistence and clustering providers.

## **🗺️ Development Roadmap**

### **Phase 1: The Core "Static" Engine (Local Runtime)** ✅ COMPLETED

*Focus: AOT-compatible code generation and basic actor lifecycle.*

* \[✓\] **Source Generator V1:** Incremental generator for \[QuarkActor\] attributes.  
* \[✓\] **Turn-based Mailbox:** High-performance messaging via System.Threading.Channels.  
* \[✓\] **Explicit Lifecycle:** Support for ActivateAsync and DeactivateAsync (Explicit Stop).  
* \[✓\] **Local Context:** Request ID and Metadata propagation for tracing.
* \[✓\] **Supervision Hierarchies:** Parent-child actor relationships with failure handling.
* \[✓\] **Persistence Abstractions:** Multi-storage provider support with IStateStorage.

**Status:** All features implemented and tested. 33/33 tests passing.

### **Phase 2: The Cluster & Networking Layer** ✅ COMPLETED

*Focus: Silo-to-Silo communication over encrypted UDP.*

* \[✓\] **Transport Abstraction:** IQuarkTransport interface for bi-directional gRPC streaming.  
* \[✓\] **Consistent Hashing:** Hash ring with virtual nodes for even actor distribution.  
* \[✓\] **Silo Membership:** Redis-based distributed Silo table for node discovery.  
* \[✓\] **QuarkEnvelope:** Universal message wrapper for all actor invocations.  
* \[✓\] **Location Transparency:** Routing logic via consistent hash ring.  
* \[✓\] **gRPC Transport:** Bi-directional streaming over HTTP/3 (protobuf defined, client implementation complete).
* \[✓\] **Placement Policies:** Random, LocalPreferred, StatelessWorker, ConsistentHash.
* \[✓\] **Logging Source Generator:** High-performance logging with zero allocation.
* \[✓\] **JSON Source Generation:** Zero-reflection JSON serialization for AOT compatibility.
* \[✓\] **Redis Testcontainers:** Integration tests with real Redis instances.

**Status:** All features implemented and tested. 77/77 tests passing. **100% AOT compatible - zero reflection.**

**Architecture:**
- `Quark.Networking.Abstractions` - Core networking interfaces
- `Quark.Transport.Grpc` - gRPC/HTTP3 implementation (in progress)
- `Quark.Clustering.Redis` - Redis-based membership with consistent hashing

### **Phase 3: Reliability & Supervision (The "Power" Layer)** ✅ COMPLETED

*Focus: Advanced failure handling and reentrancy.*

* \[✓\] **Call-Chain Reentrancy:** Prevent deadlocks in circular actor calls using Chain IDs.  
* \[✓\] **Restart Strategies:** OneForOne, AllForOne, RestForOne with exponential backoff.
* \[✓\] **Supervision Options:** Configurable supervision with time windows and escalation.
* \[✓\] **Consistent Hashing:** Predictable actor placement (implemented in Phase 2).
* \[🚧\] **Cluster Health:** Advanced heartbeat monitoring and automatic silo eviction (future).

**Status:** Core reliability features complete. 77/77 tests passing.

### **Phase 4: Persistence & Temporal Services** ✅ COMPLETED

*Focus: Making state and time durable.*

* \[✓\] **Production-Grade State Generator:** Auto-generates JsonSerializerContext for AOT-safe serialization.
* \[✓\] **E-Tag / Optimistic Concurrency:** Version tracking prevents "Lost Updates" in distributed races.
* \[✓\] **Persistent Reminders:** Durable timers that survive cluster reboots.
* \[✓\] **Distributed Scheduler:** ReminderTickManager polls reminder table using consistent hashing.
* \[✓\] **Reminder Abstractions:** IReminderTable, IRemindable interfaces.
* \[✓\] **InMemoryReminderTable:** Implementation with consistent hash ring integration.
* \[✓\] **Timers:** Lightweight, in-memory volatile timers.
* \[ \] **State Providers:** Redis and Postgres storage with optimistic concurrency.
* \[ \] **Reminder Storage:** Redis and Postgres reminder tables.
* \[ \] **Event Sourcing:** Native journaling support for audit-logs and state replay.

**Status:** Core features complete with comprehensive tests (138/138 tests passing). Storage providers and event sourcing are deferred to future phases.

### **Phase 5: Reactive Streaming** ✅ COMPLETED

*Focus: Decoupled data broadcasting.*

* \[✓\] **Explicit Streams:** Manual Pub/Sub via StreamID.  
* \[✓\] **Implicit Streams:** Automatically activate actors based on stream namespaces.  
* \[✓\] **Source Generator:** Auto-generates stream-to-actor mappings at build time.
* \[✓\] **Analyzer:** Validates stream attribute usage and namespace formats.
* \[ \] **Backpressure:** Adaptive flow control for slow consumers (future enhancement).

**Status:** All core features complete. 26 streaming tests passing (164 total tests passing).

## **🏗️ Project Structure**

* Quark.Abstractions: Core interfaces, attributes, and shared models. (No dependencies).  
* Quark.Generators: The Roslyn Incremental Source Generator.  
* Quark.Core: The actor runtime, mailbox, and local scheduling.  
* Quark.Transport.Quic: UDP/QUIC networking implementation.  
* Quark.Clustering.\*: Membership providers (Redis, Kubernetes, etc.).

## **🛠️ Requirements**

* .NET 10.0 SDK+  
* Visual Studio 2026 or JetBrains Rider  
* (Optional) Docker for local clustering tests

## **🤝 Contributing**

Quark is currently in **Phase 0 (Architectural Planning)**. We welcome architectural feedback and discussions regarding the gRPC/QUIC transport layer.

*Generated by the Quark Development Team.*
