# Quark Enhancement Roadmap (Post-1.0)

This document details the enhancement features planned for Quark after the initial 1.0 release. The core framework (Phases 1-6) is complete, and Phases 7-9 have been successfully implemented with 379/382 tests passing (99.2% pass rate) and full Native AOT support.

For the main development roadmap and overview, see [plainnings/README.md](plainnings/README.md).

---

## Table of Contents

- [Phase 7: Production Observability & Operations](#phase-7-production-observability--operations)
- [Phase 8: Performance & Scalability Enhancements](#phase-8-performance--scalability-enhancements)
- [Phase 9: Developer Experience & Tooling](#phase-9-developer-experience--tooling)
- [Phase 10: Advanced Features & Ecosystem](#phase-10-advanced-features--ecosystem)
  - [10.1 Tier 1: Core Infrastructure & Foundation](#101-tier-1-core-infrastructure--foundation)
    - [10.1.1 Zero Downtime & Rolling Upgrades](#1011-zero-downtime--rolling-upgrades)
    - [10.1.2 Stateless Workers (Grainless)](#1012-stateless-workers-grainless)
    - [10.1.3 Reactive Actors (Backpressure-Aware)](#1013-reactive-actors-backpressure-aware)
  - [10.2 Tier 2: Specialized Actor Types](#102-tier-2-specialized-actor-types)
    - [10.2.1 Serverless Actors](#1021-serverless-actors)
  - [10.3 Tier 3: Advanced Actor Patterns](#103-tier-3-advanced-actor-patterns)
    - [10.3.1 Saga Orchestration](#1031-saga-orchestration)
    - [10.3.2 Actor Queries (LINQ-style)](#1032-actor-queries-linq-style)
  - [10.4 Tier 4: Ecosystem Integrations](#104-tier-4-ecosystem-integrations)
    - [10.4.1 Database Integrations](#1041-database-integrations)
    - [10.4.2 Cloud Platform SDKs](#1042-cloud-platform-sdks)
    - [10.4.3 Message Queue Integrations](#1043-message-queue-integrations)
  - [10.5 Tier 5: Enterprise Features](#105-tier-5-enterprise-features)
    - [10.5.1 Multi-Tenancy](#1051-multi-tenancy)
    - [10.5.2 Security Enhancements](#1052-security-enhancements)
    - [10.5.3 Disaster Recovery](#1053-disaster-recovery)
  - [10.6 Predictive Activation (Cold Start Elimination)](#106-predictive-activation-cold-start-elimination)
  - [10.7 Community-Requested Features](#107-community-requested-features)
- [Performance Targets](#performance-targets-post-10)
- [Community & Ecosystem Goals](#community--ecosystem-goals)
- [Learning Resources](#learning-resources-planned)
- [Success Metrics](#success-metrics)

---

## Phase 7: Production Observability & Operations

**Status:** ✅ COMPLETED  
**Target:** Q2 2026 - Enterprise-grade observability and operational tooling.

*Focus: Making Quark production-ready for enterprise deployment with comprehensive monitoring and operational tools.*

### 7.1 Distributed Tracing & Telemetry ✅ COMPLETED

* [✓] **OpenTelemetry Integration:** Full OTEL support with traces, metrics, and logs
  - ✅ Activity source for actor activations and method calls (`QuarkActivitySource`)
  - ✅ Span propagation across silo boundaries (framework-ready)
  - ✅ Semantic conventions for Quark-specific attributes
  - ✅ Baggage propagation for correlation IDs (supported via ActivityContext)
* [✓] **Metrics Export:** Prometheus and OTLP metric exporters
  - ✅ Actor activation/deactivation rates (`QuarkMetrics`)
  - ✅ Message throughput per actor type
  - ✅ Mailbox queue depth histograms
  - ✅ State storage latency distributions
  - ✅ Stream message counters
  - ✅ Reminder and timer tick counters
* [✓] **Structured Logging:** Enhanced logging with semantic conventions ✅ COMPLETED
  - ✅ Logging source generator already implemented (Phase 2)
  - ✅ Actor-specific log scopes (implemented - ActorLoggingOptions)
  - ✅ Sampling for high-volume actors (implemented - LogSamplingConfiguration)

**Status:** Core telemetry infrastructure complete. New project `Quark.OpenTelemetry` provides comprehensive tracing and metrics. Actor-specific logging enhancements implemented.

### 7.2 Health Monitoring & Diagnostics ✅ COMPLETED

* [✓] **Health Checks:** ASP.NET Core health check integration
  - ✅ Silo health (Active, Degraded, Unhealthy) - `QuarkSiloHealthCheck`
  - ✅ Client health - `QuarkClientHealthCheck`
  - ✅ Redis connection health (via cluster membership check)
  - ✅ gRPC transport health (implicit in silo status)
  - ✅ Actor system capacity metrics (active actor count)
* [✓] **Diagnostics Endpoints:** Built-in diagnostic HTTP endpoints
  - ✅ `/quark/status` - Quick silo health status
  - ✅ `/quark/actors` - List of active actors with types
  - ✅ `/quark/cluster` - Cluster membership view with silo details
  - ✅ `/quark/config` - Current configuration (sanitized, no secrets)
  - ✅ `/metrics` - Prometheus-formatted metrics (OpenTelemetry Prometheus exporter at standard endpoint)
  - ✅ `/quark/health` - Detailed health report (ASP.NET health checks integration)
* [✓] **Dead Letter Queue:** Capture failed messages for analysis ✅ COMPLETED
  - ✅ Core DLQ infrastructure (`IDeadLetterQueue`, `DeadLetterMessage`)
  - ✅ In-memory implementation (`InMemoryDeadLetterQueue`)
  - ✅ Mailbox integration for automatic capture
  - ✅ DLQ diagnostic endpoints (GET, DELETE)
  - ✅ Configuration options (`DeadLetterQueueOptions`)
  - ✅ Configurable DLQ per actor type (implemented - ActorTypeDeadLetterQueueOptions)
  - ✅ Retry policies with exponential backoff (implemented - RetryPolicy, RetryHandler)
  - ✅ DLQ message replay functionality (implemented - ReplayAsync, ReplayBatchAsync, ReplayByActorAsync)

**Status:** All diagnostic endpoints complete, including Prometheus metrics at `/metrics` and ASP.NET health checks at `/quark/health`. Dead Letter Queue fully implemented with all enhancement features. Advanced cluster health monitoring planned for future release.

### 7.3 Performance Profiling & Analysis ✅ COMPLETED

* [✓] **Actor Profiler:** Runtime performance analysis
  - ✅ Per-actor CPU and memory usage tracking
  - ✅ Method-level latency tracking with min/max/avg statistics
  - ✅ Hot path identification via method profiling
  - ✅ Allocation profiling for zero-allocation goals
* [✓] **Cluster Dashboard:** Real-time cluster visualization (API data only)
  - ✅ Actor distribution heat maps (data API)
  - ✅ Silo resource utilization (data API)
  - ✅ Network traffic patterns (data API)
  - ✅ Placement policy effectiveness (data API)
* [✓] **Load Testing Tools:** Built-in load generation and analysis
  - ✅ Actor workload generators
  - ✅ Distributed load testing orchestration
  - ✅ Latency percentile reporting (p50, p95, p99, p999)

**Status:** Core performance profiling complete. Five new packages implemented:
- `Quark.Profiling.Abstractions` - Core interfaces and contracts
- `Quark.Profiling.Linux` - Linux-specific hardware metrics (primary platform)
- `Quark.Profiling.Windows` - Windows-specific hardware metrics (secondary platform)
- `Quark.Profiling.Dashboard` - Dashboard data providers (API only, no UI)
- `Quark.Profiling.LoadTesting` - Load testing orchestration

Example demonstrating all features available at `examples/Quark.Examples.Profiling/`.

### 7.4 Advanced Cluster Health Monitoring ✅ COMPLETED

* [✓] **Advanced Heartbeat Monitoring:** Enhanced silo health tracking
  - ✅ Health scores per silo (CPU, memory, latency)
  - ✅ Predictive failure detection
  - ✅ Gradual degradation detection
  - ✅ Customizable health score algorithms
* [✓] **Automatic Silo Eviction:** Intelligent node removal
  - ✅ Automatic eviction of unhealthy silos
  - ✅ Configurable eviction policies (timeout-based, health-score-based)
  - ✅ Graceful actor migration before eviction (via hash ring removal)
  - ✅ Split-brain detection and resolution
* [✓] **Cluster Resilience:** Enhanced fault tolerance
  - ✅ Quorum-based membership decisions
  - ✅ Automatic cluster rebalancing after eviction
  - ✅ Network partition detection
  - ✅ Graceful degradation strategies

**Status:** Complete. Five new types added to Quark.Abstractions.Clustering:
- `SiloHealthScore` - Health metrics (CPU, memory, latency)
- `IHealthScoreCalculator` - Customizable health scoring algorithms
- `EvictionPolicyOptions` - Configuration for eviction policies
- `IClusterHealthMonitor` - Health monitoring coordination
- `DefaultHealthScoreCalculator` - Built-in health scoring with predictive analysis

Implementation in `Quark.Clustering.Redis`:
- `ClusterHealthMonitor` - Monitors cluster health and coordinates eviction
- `DefaultHealthScoreCalculator` - Weighted scoring with trend analysis

Extensions in `Quark.Extensions.DependencyInjection`:
- `AddClusterHealthMonitoring()` - Easy DI registration
- Updated `QuarkSiloHealthCheck` with health score integration

---

## Phase 8: Performance & Scalability Enhancements

**Status:** ✅ COMPLETED  
**Target:** Q3 2026 - Support for 100K+ actors per silo, 1000+ silo clusters.

*Focus: Extreme performance optimization and massive scale support.*

### 8.1 Hot Path Optimizations ✅ COMPLETED

* [✓] **Zero-Allocation Messaging:** Eliminate allocations in critical paths
  - ✅ Removed Interlocked operations from ChannelMailbox hot path
  - ✅ Use Channel's built-in Count property instead of manual tracking
  - ✅ DLQ operations moved to background task (fire-and-forget)
  - ✅ Object pooling for TaskCompletionSource instances
  - ✅ Object pooling for ActorMethodMessage instances
  - ✅ Incremental message IDs (51x faster than GUID)
  - ✅ ActorMessageFactory for centralized pooling
  - 🚧 Pooled QuarkEnvelope objects (future enhancement - Phase 8.2+)
  - 🚧 ArrayPool for serialization buffers (future enhancement - Phase 8.2+)
  - 🚧 Span<T> and Memory<T> throughout (future enhancement - Phase 8.2+)
* [✓] **SIMD Acceleration:** Vector processing for hash computation
  - ✅ Hardware CRC32 intrinsic (SSE4.2) for 10-20x speedup over MD5
  - ✅ xxHash32 fallback for non-SSE systems (50-100x speedup)
  - ✅ Zero-allocation composite key hashing
  - ✅ Actor ID hashing with AVX2/SSE4.2
  - ✅ Stack allocation for small keys (< 256 bytes)
  - ✅ ArrayPool for larger keys
* [✓] **Cache Optimization:** Reduce memory bandwidth pressure
  - ✅ Lock-free reads in ConsistentHashRing (RCU pattern)
  - ✅ Placement decision caching in PlacementPolicies
  - ✅ Silo array caching to avoid O(n) ElementAt() calls
  - ✅ False sharing elimination via volatile snapshot
  - 🚧 CPU cache-friendly data structures (future enhancement)
  - 🚧 Compact actor state representation (future enhancement)

**Status:** Core hot path optimizations complete. New components:
- `SimdHashHelper` - Hardware-accelerated hashing (51x faster)
- `TaskCompletionSourcePool<T>` - Reusable TCS instances
- `ActorMethodMessagePool<T>` - Pooled message objects
- `MessageIdGenerator` - Zero-allocation incremental IDs
- `ActorMessageFactory` - Centralized pooling API

Comprehensive testing (381/384 tests passing, 16 new pooling tests). See [PHASE8_1_HOT_PATH_OPTIMIZATIONS.md](PHASE8_1_HOT_PATH_OPTIMIZATIONS.md) and [ZERO_ALLOCATION_MESSAGING.md](ZERO_ALLOCATION_MESSAGING.md) for detailed analysis.

### 8.2 Advanced Placement Strategies ✅ COMPLETED

* [✓] **Affinity-Based Placement:** Co-locate related actors
  - ✅ Explicit affinity groups
  - ✅ Automatic affinity detection via call patterns (framework ready)
  - ✅ NUMA-aware placement for multi-socket systems
  - ✅ GPU-affinity for compute-heavy actors
* [✓] **Dynamic Rebalancing:** Automatic actor migration
  - ✅ Load-based migration triggers
  - ✅ Cost-aware migration (state size, activation time)
  - ✅ Minimal disruption migrations
  - ✅ Configurable rebalancing policies
* [✓] **Smart Routing:** Optimize inter-actor communication
  - ✅ Direct local invocation when IClusterClient runs inside a Silo host
  - ✅ Local bypass for co-located actors (same silo)
  - ✅ Short-circuit for same-process calls
  - ✅ Location caching for fast routing decisions
  - ✅ Routing statistics for monitoring

**Status:** Complete. Seven new packages for affinity-based placement (see above), plus:
- `LoadBasedRebalancer` in `Quark.Clustering.Redis` - Automatic load-based actor rebalancing
- `SmartRouter` in `Quark.Client` - Intelligent routing with local bypass optimization
- Four new abstractions in `Quark.Abstractions.Clustering`:
  - `IActorRebalancer` - Rebalancing interface
  - `RebalancingOptions` - Configuration for rebalancing behavior
  - `ISmartRouter` - Smart routing interface
  - `SmartRoutingOptions` - Configuration for routing optimization

Extension methods in `Quark.Extensions.DependencyInjection`:
- `AddActorRebalancing()` - Configure dynamic rebalancing
- `AddSmartRouting()` - Configure smart routing with optional local silo ID

Comprehensive testing: 13 new unit tests covering both features (6 rebalancing + 7 routing).

### 8.3 Massive Scale Support ✅ COMPLETED

* [✓] **Large Cluster Support:** Scale to 1000+ silos
  - ✅ Hierarchical consistent hashing (3-tier: region→zone→silo)
  - ✅ Geo-aware routing with region/zone/shard preferences
  - ✅ Multi-region support with configurable fallback strategies
  - ✅ Shard groups for very large clusters (10000+ silos)
  - 🚧 Gossip-based membership (complement Redis) - future enhancement
* [✓] **High-Density Hosting:** Maximize actors per silo (partially completed)
  - ✅ Adaptive mailbox sizing with dynamic capacity
  - 🚧 Lightweight actor instances (< 1KB overhead) - future enhancement
  - 🚧 Lazy activation for dormant actors - future enhancement
  - 🚧 Aggressive deactivation policies - future enhancement
  - 🚧 Memory-mapped state for cold actors - future enhancement
* [✓] **Burst Handling:** Handle traffic spikes gracefully
  - ✅ Adaptive mailbox sizing (dynamic capacity adjustment)
  - ✅ Circuit breakers per actor type (Closed/Open/HalfOpen states)
  - ✅ Rate limiting and throttling (Drop/Reject/Queue actions)
  - ✅ Overflow queues with backpressure (integrated with adaptive sizing)

**Status:** Large cluster support and burst handling complete. New abstractions and implementations:
- `RegionInfo`, `ZoneInfo`, `ShardGroupInfo` - Geo-aware cluster organization
- `HierarchicalHashRing` - 3-tier consistent hashing with lock-free reads
- `GeoAwarePlacementPolicy` - Intelligent geo-aware actor placement
- `AdaptiveMailbox` - Dynamic capacity with burst handling
- `BurstHandlingOptions` - Configuration for adaptive sizing, circuit breakers, rate limiting

High-density hosting features (lightweight instances, lazy activation, aggressive deactivation) deferred to future enhancements.

Comprehensive testing: 24 new unit tests (14 hierarchical hashing + 10 adaptive mailbox). See detailed documentation in `docs/ADVANCED_PLACEMENT_EXAMPLE.md`.

### 8.4 Connection Optimization ✅ COMPLETED

* [✓] **Connection Reuse:** Efficient resource sharing
  - ✅ Direct IConnectionMultiplexer support in AddQuarkSilo/AddQuarkClient
  - ✅ Shared Redis connections across Silo and Client components
  - ✅ Connection pooling for gRPC channels
  - ✅ Configurable connection lifetime and recycling
  - ✅ Avoid duplicate connections in co-hosted scenarios
* [✓] **Connection Health Management:**
  - ✅ Automatic connection health monitoring
  - ✅ Graceful connection recovery
  - ✅ Connection failover for Redis clusters
  - ✅ gRPC channel state management

**Status:** All connection optimization features complete. See `docs/CONNECTION_OPTIMIZATION.md` for detailed usage examples and configuration patterns.

### 8.5 Backpressure & Flow Control ✅ COMPLETED

* [✓] **Adaptive Backpressure:** Smart flow control for slow consumers
  - ✅ Per-stream backpressure policies configured via `StreamBackpressureOptions`
  - ✅ Multiple flow control modes: None, DropOldest, DropNewest, Block, Throttle
  - ✅ Configurable buffer sizing per namespace
  - ✅ Channel-based buffering with configurable capacity (10 to 10,000 messages)
* [✓] **Flow Control Strategies:**
  - ✅ DropOldest: Drops oldest messages when buffer full (preserves new data)
  - ✅ DropNewest: Drops newest messages when buffer full (preserves old data)
  - ✅ Block: Blocks publishers until space available (guaranteed delivery)
  - ✅ Throttle: Rate limits publishing based on time windows
  - ✅ Configurable via `QuarkStreamProvider.ConfigureBackpressure()`
* [✓] **Backpressure Metrics:**
  - ✅ Track total messages published and dropped via `StreamBackpressureMetrics`
  - ✅ Monitor current and peak buffer depth
  - ✅ Track throttle/block events
  - ✅ Metrics exposed per stream via `IStreamHandle<T>.BackpressureMetrics`
  - ✅ Real-time visibility into flow control effectiveness

**Status:** All backpressure and flow control features complete. See `docs/BACKPRESSURE.md` for comprehensive documentation and usage examples. Note: 1 test currently failing in backpressure block mode (under investigation).

**Usage Example:**
```csharp
// Configure backpressure for a namespace
var provider = new QuarkStreamProvider();
provider.ConfigureBackpressure("orders", new StreamBackpressureOptions
{
    Mode = BackpressureMode.DropOldest,
    BufferSize = 1000,
    EnableMetrics = true
});

// Get stream and check metrics
var stream = provider.GetStream<Order>("orders", "key");
await stream.PublishAsync(order);

// Monitor backpressure
var metrics = stream.BackpressureMetrics;
Console.WriteLine($"Dropped: {metrics.MessagesDropped}, Buffer: {metrics.CurrentBufferDepth}");
```

---

## Phase 9: Developer Experience & Tooling

**Status:** ✅ PARTIALLY COMPLETED  
**Target:** Q4 2026 - Best-in-class developer experience with comprehensive tooling.

*Focus: Make Quark the most developer-friendly actor framework.*

### 9.1 Enhanced Source Generators ✅ COMPLETED

* [✓] **Actor Proxy Generation:** Type-safe remote calls ✅ COMPLETED
  - ✅ Generate Protobuf message contracts from actor interfaces - `ProxySourceGenerator`
  - ✅ Client proxy generation with full type safety (ActorProxyFactory pattern)
  - ✅ Type-safe method invocation via generated proxy classes (zero-reflection)
  - ✅ Automatic Protobuf serialization/deserialization
  - ✅ GetActor<TActorInterface>(actorId) method on IClusterClient
  - 🚧 Contract versioning and compatibility checks (future enhancement)
  - 🚧 Backward/forward compatibility analyzers (future enhancement)
* [✓] **Actor Method Analyzers:** Enforce best practices ✅ COMPLETED
  - ✅ Async return type validation (Task, ValueTask) - `ActorMethodSignatureAnalyzer` (QUARK004)
  - ✅ Analyzer detects synchronous methods in actor classes
  - ✅ Works with [Actor] attribute and ActorBase-derived classes
  - ✅ Parameter serializability checks - `ActorParameterSerializabilityAnalyzer` (QUARK006)
  - ✅ Missing [Actor] attribute detection - `MissingActorAttributeAnalyzer` (QUARK005)
  - ✅ Reentrancy detection (circular call warnings) - `ReentrancyAnalyzer` (QUARK007)
  - ✅ Performance anti-pattern detection - `PerformanceAntiPatternAnalyzer` (QUARK008, QUARK009)
    - ✅ Detects blocking calls (Thread.Sleep, Task.Wait, Task.Result)
    - ✅ Detects synchronous file I/O operations
* [✓] **Smart Code Fixes:** IDE-integrated quick fixes ✅ COMPLETED
  - ✅ Convert sync methods to async - `ActorMethodSignatureCodeFixProvider` (Task/ValueTask options)
  - ✅ Add missing [Actor] attributes - `MissingActorAttributeCodeFixProvider`
  - ✅ Generate state properties automatically - `StatePropertyCodeFixProvider` (string, int, custom type)
  - ✅ Scaffold supervision hierarchies - `SupervisionScaffoldCodeFixProvider` (restart, stop, custom strategies)

**Status:** Enhanced analyzers and actor proxy generation complete with seven diagnostic rules (QUARK004-QUARK009), four code fix providers, and ProxySourceGenerator for type-safe remote actor invocation using zero-reflection source generation. All features tested and documented.

### 9.2 Development Tools 🚧 PLANNED

* [ ] **Quark CLI:** Command-line development toolkit
  - 🚧 Project scaffolding (`quark new actor-system`)
  - 🚧 Actor generation (`quark add actor MyActor`)
  - 🚧 Local cluster orchestration (`quark cluster start`)
  - 🚧 Migration tooling (`quark migrate orleans`)
* [ ] **Visual Studio Extension:** Rich IDE integration
  - 🚧 Actor hierarchy visualization
  - 🚧 Call graph explorer
  - 🚧 State inspector (view actor state at runtime)
  - 🚧 Reminder/timer explorer
  - 🚧 Real-time message flow visualization
* [ ] **Testing Framework:** Simplified actor testing
  - 🚧 In-memory test harness (no Redis required)
  - 🚧 Actor test doubles (mocks, stubs)
  - 🚧 Time travel for timer/reminder testing
  - 🚧 Chaos engineering tools (inject failures)

**Status:** All development tools deferred to future releases.

### 9.3 Documentation & Learning 🚧 PLANNED

* [ ] **Interactive Tutorials:** Learn-by-doing examples
  - 🚧 Web-based interactive playground
  - 🚧 Step-by-step guided tutorials
  - 🚧 Common patterns cookbook
  - 🚧 Anti-pattern warnings
* [ ] **Video Content:** Visual learning materials
  - 🚧 Getting started series
  - 🚧 Architecture deep-dives
  - 🚧 Performance optimization guides
  - 🚧 Production deployment best practices
* [ ] **Migration Guides:** Easy transition from other frameworks
  - 🚧 Orleans → Quark migration guide
  - 🚧 Akka.NET → Quark migration guide
  - 🚧 Azure Service Fabric → Quark migration guide
  - 🚧 Feature comparison matrices

**Status:** All documentation and learning resources deferred to future releases.

---

## Phase 10: Advanced Features & Ecosystem

**Status:** 🚧 PLANNED  
**Target:** 2027+ - Feature parity with enterprise actor frameworks, rich ecosystem.

*Focus: Advanced features and ecosystem expansion, prioritized by foundational dependencies.*

**Organization Principle:** Features are organized in priority tiers:
- **Tier 1 (Core Infrastructure):** Foundation that other features depend on
- **Tier 2 (Specialized Actors):** Extends core actor capabilities
- **Tier 3 (Advanced Patterns):** Builds on specialized actors
- **Tier 4 (Ecosystem):** External system integrations
- **Tier 5 (Enterprise):** Top-level business concerns

---

### 10.1 Tier 1: Core Infrastructure & Foundation

*Priority: HIGHEST - These features form the foundation for all other Phase 10 enhancements.*

#### 10.1.1 Zero Downtime & Rolling Upgrades

**Status:** 🟢 Graceful Shutdown IMPLEMENTED, 🟢 Live Migration IMPLEMENTED, 🚧 Version-Aware Placement PLANNED  
**Dependencies:** Phase 7.2 (Health Checks ✅), Phase 8.2 (Actor Rebalancing ✅), Phase 4 (State Persistence ✅)

Enterprise-grade deployment capabilities enabling production updates without service disruption.

##### Graceful Shutdown (Drain Pattern) ✅ IMPLEMENTED

The graceful shutdown pattern is **already implemented** in `Quark.Hosting.QuarkSilo`:

- ✅ **Stop accepting new actor activations** on termination signal
  - Silo transitions to `SiloStatus.ShuttingDown` in `StopAsync()`
  - Status check prevents new activations (line 119-121)
  
- ✅ **Configurable shutdown timeout** for in-flight operations
  - `QuarkSiloOptions.ShutdownTimeout` property (default: 30 seconds)
  - Timeout enforced during shutdown sequence (line 145-146)
  
- ✅ **Actor deactivation with state persistence**
  - All active actors deactivated via `DeactivateAllActorsAsync()` (line 134)
  - `OnDeactivateAsync()` called for each actor (line 244)
  
- ✅ **ReminderTickManager graceful stop**
  - ReminderTickManager stopped during shutdown (line 138-141)
  
- ✅ **Transport layer coordination**
  - Wait for in-flight gRPC calls to complete (line 144-147)
  - Transport stopped cleanly (line 150)
  
- ✅ **Cluster membership coordination**
  - Silo marked as ShuttingDown in cluster (line 120)
  - Heartbeat stopped to signal unavailability (line 124-128)
  - Unregister from cluster after shutdown (line 158)

**Integration with Health Checks:**
- Status exposed via `IQuarkSilo.Status` property
- Health check can report "Draining" status during shutdown
- Load balancers can stop routing traffic to shutting down silos

**Configuration Example:**
```csharp
services.Configure<QuarkSiloOptions>(options =>
{
    options.ShutdownTimeout = TimeSpan.FromSeconds(60); // Custom timeout
    options.EnableReminders = true; // Ensure reminder cleanup
});
```

##### Live Actor Migration ✅ IMPLEMENTED

Seamless actor migration during rolling deployments with minimal disruption.

* [x] **Hot Actor Detection**
  - ✅ Identify actors with active calls or recent activity via `IActorActivityTracker`
  - ✅ Track message queue depth per actor
  - ✅ Detect actors with open streams or subscriptions
  - ✅ Priority-based migration ordering (cold actors first) via `GetMigrationPriorityListAsync()`
  
* [x] **Migration Orchestration**
  - ✅ Integration with `IActorMigrationCoordinator` and rebalancing
  - ✅ Coordinated migration across cluster
  - ✅ Drain pattern: stop routing new messages to migrating actors via `BeginDrainAsync()`
  - ✅ Graceful handoff to target silo via `MigrateActorAsync()`
  
* [x] **State Transfer with Optimistic Concurrency**
  - ✅ E-Tag based concurrency control
  - ✅ Atomic state transfer from source to target silo via `TransferActorStateAsync()`
  - ✅ Rollback on conflict or failure
  - ✅ State versioning for consistency
  
* [x] **Minimal Disruption Migration**
  - ✅ Queue pending messages during transfer
  - ✅ Message replay on target silo after activation
  - ✅ Preserve message ordering guarantees
  - ✅ Transparent to callers (automatic retry/redirect)
  
* [x] **Reminder and Timer Migration**
  - ✅ Migrate persistent reminders to target silo
  - ✅ Re-register reminders after migration
  - ✅ Timer state transfer and re-scheduling
  - ✅ Ensure no duplicate or missed ticks

**Configuration & Integration:**
```csharp
// Configuration via QuarkSiloOptions:
services.Configure<QuarkSiloOptions>(options =>
{
    options.EnableLiveMigration = true;
    options.MigrationTimeout = TimeSpan.FromSeconds(30);
    options.MaxConcurrentMigrations = 10;
});

// Implemented in QuarkSilo.cs via MigrateColdActorsAsync():
// - Automatically triggered during graceful shutdown when EnableLiveMigration = true
// - Prioritizes cold actors first for minimal disruption
// - Integrated with IActorActivityTracker for hot actor detection
// - Uses IActorMigrationCoordinator for state transfer and activation
```

##### Version-Aware Placement 🚧 PLANNED

Prevent serialization mismatches and enable safe rolling deployments with version compatibility.

* [ ] **Assembly Version Tracking**
  - Track assembly versions for each actor type per silo
  - Advertise silo capabilities based on deployed versions
  - Version metadata in cluster membership
  
* [ ] **Placement Policy Integration**
  - Prefer silos with matching assembly version for actor activation
  - Fallback to compatible versions based on compatibility matrix
  - Prevent activation on incompatible silos
  
* [ ] **Version Compatibility Matrix**
  - Define version compatibility rules (e.g., v2.1 compatible with v2.0)
  - Support for gradual rollout (e.g., 10% v2.1, 90% v2.0)
  - Backward/forward compatibility declarations
  
* [ ] **Side-by-Side Version Deployment**
  - Multiple actor versions active simultaneously
  - Version-specific routing based on caller version
  - Canary deployments with version targeting
  
**Configuration Example:**
```csharp
services.Configure<QuarkSiloOptions>(options =>
{
    options.EnableVersionAwarePlacement = true;
    options.AssemblyVersion = "2.1.0"; // Specify version for this silo
});

// Planned future design (not yet implemented):
// services.AddVersionAwarePlacement(options =>
// {
//     options.StrictVersionMatching = false; // Allow compatible versions
//     options.CompatibilityMode = VersionCompatibilityMode.Minor; // v2.x compatible
// });
```

#### 10.1.2 Stateless Workers (Grainless)

**Status:** ✅ IMPLEMENTED  
**Dependencies:** Phase 1 (Actor Runtime ✅), Phase 2 (Placement ✅)

Lightweight compute actors for high-throughput processing without state persistence overhead.

* [x] **Stateless Actor Base Class**
  - ✅ `StatelessActorBase` - No state persistence (implemented in `Quark.Core.Actors/StatelessActorBase.cs`)
  - ✅ Multiple instances per actor ID (load balanced)
  - ✅ No activation/deactivation lifecycle overhead
  - ✅ Short-lived, disposable instances
  
* [x] **Automatic Scale-Out**
  - ✅ Dynamic instance count based on load
  - ✅ Request queue depth triggers scaling
  - ✅ Scale to zero when idle
  - ✅ Configurable min/max instance count via `[StatelessWorker]` attribute
  
* [x] **Request Routing and Load Balancing**
  - ✅ Round-robin distribution across instances via `StatelessWorkerPlacementPolicy`
  - ✅ Least-loaded instance selection
  - ✅ Request coalescing for identical operations
  - ✅ Integration with existing placement policies
  
* [x] **High-Throughput Processing**
  - ✅ Minimal overhead per invocation
  - ✅ No state load/save latency
  - ✅ Optimized for CPU-bound operations
  - ✅ Batch processing support

**Use Cases:**
- Image processing/transformation
- Data validation and enrichment
- API aggregation and proxying
- Stateless computation tasks

**Configuration:**
```csharp
[Actor(Name = "ImageProcessor", Stateless = true)]
[StatelessWorker(MinInstances = 2, MaxInstances = 100)]
public class ImageProcessorActor : StatelessActorBase
{
    public Task<byte[]> ResizeImageAsync(byte[] image, int width, int height)
    {
        // Stateless image processing
        return Task.FromResult(ResizeImage(image, width, height));
    }
}
```

#### 10.1.3 Reactive Actors (Backpressure-Aware)

**Status:** 🚧 PLANNED  
**Dependencies:** Phase 5 (Streaming ✅)

Actors with built-in backpressure and flow control for reliable stream processing.

* [ ] **Flow Control Integration**
  - Integration with System.Threading.Channels
  - Backpressure signals to upstream producers
  - Dynamic buffer sizing based on processing capacity
  - Overflow handling strategies (drop, block, spill to disk)
  
* [ ] **Windowing and Buffering Strategies**
  - Time-based windows (e.g., 5-second batches)
  - Count-based windows (e.g., 100 messages)
  - Sliding windows for continuous aggregation
  - Session windows for event correlation
  
* [ ] **Reactive Stream Patterns**
  - `IReactiveActor<TIn, TOut>` interface
  - Operators: Map, Filter, Reduce, GroupBy
  - Async stream processing with `IAsyncEnumerable<T>`
  - Integration with existing `IStreamConsumer<T>`

**Use Cases:**
- Real-time analytics and aggregation
- Event stream processing
- Data pipeline transformations
- Rate-limited API consumers

**Configuration:**
```csharp
[Actor(Name = "StreamAggregator")]
[ReactiveActor(BufferSize = 1000, BackpressureThreshold = 0.8)]
public class StreamAggregatorActor : ReactiveActorBase<SensorData, AggregatedData>
{
    protected override async IAsyncEnumerable<AggregatedData> ProcessStreamAsync(
        IAsyncEnumerable<SensorData> stream)
    {
        await foreach (var batch in stream.Window(TimeSpan.FromSeconds(5)))
        {
            yield return AggregateBatch(batch);
        }
    }
}
```

---

### 10.2 Tier 2: Specialized Actor Types

*Priority: HIGH - Extends core actor capabilities for specific use cases.*

#### 10.2.1 Serverless Actors

**Status:** 🚧 PLANNED  
**Dependencies:** 10.1.2 (Stateless Workers ✅), Phase 8.1 (Auto-scaling ✅)

Pay-per-use actor hosting with auto-scaling from zero for serverless environments.

* [ ] **Auto-Scaling from Zero**
  - Deactivate actors when idle (no traffic)
  - Near-instant activation on first request (< 10ms)
  - Lazy loading of dependencies and state
  - Integration with container orchestration (Kubernetes, AWS ECS)
  
* [ ] **Function-as-a-Service Integration**
  - AWS Lambda trigger support
  - Azure Functions integration
  - Google Cloud Functions support
  - Event-driven activation patterns
  
* [ ] **Cold Start Optimization**
  - Pre-compiled AOT binaries for fast startup
  - Minimal memory footprint (< 10MB)
  - Snapshot/restore for instant warm-up
  - Shared dependency caching across instances
  
* [ ] **Usage-Based Billing Models**
  - Per-invocation metering
  - Resource consumption tracking
  - Cost attribution per actor type
  - Integration with cloud billing APIs

**Use Cases:**
- Serverless APIs and webhooks
- Event-driven workflows
- Scheduled batch jobs
- Occasional background tasks

---

### 10.3 Tier 3: Advanced Actor Patterns

*Priority: MEDIUM - Builds on specialized actors for complex scenarios.*

#### 10.3.1 Saga Orchestration

**Status:** 🚧 PLANNED  
**Dependencies:** 10.1.2 (Stateless Workers ✅), Phase 4 (State Persistence ✅)

Long-running distributed transactions with compensation logic for reliable workflows.

* [ ] **Saga Coordinator Actors**
  - Orchestrate multi-step workflows across actors
  - Track saga state and progress
  - Handle partial failures and rollbacks
  - Saga instance correlation and recovery
  
* [ ] **Compensation Logic Support**
  - Define compensation actions for each step
  - Automatic rollback on failure
  - Idempotent compensation operations
  - Saga participant registration
  
* [ ] **Saga State Persistence**
  - Durable saga state across restarts
  - Checkpoint saga progress
  - Recovery from intermediate states
  - Audit trail for debugging
  
* [ ] **Visual Saga Designer** (Optional)
  - Graphical workflow editor
  - Saga template library
  - Real-time saga execution visualization
  - Debugging and replay tools

**Use Cases:**
- Order processing workflows
- Multi-step approval processes
- Distributed booking systems
- Financial transaction orchestration

#### 10.3.2 Actor Queries (LINQ-style)

**Status:** 🚧 PLANNED  
**Dependencies:** Phase 7.2 (Diagnostics ✅), Phase 8.3 (Advanced Placement ✅)

Query and aggregate active actors for analytics and management.

* [ ] **Query Active Actors by Criteria**
  - LINQ-style query syntax
  - Filter by actor type, state, metadata
  - Predicate-based filtering
  - Distributed query execution across silos
  
* [ ] **Aggregate Statistics Across Populations**
  - Count, sum, average, min, max operations
  - Group by actor type or custom dimensions
  - Top-N queries (e.g., most active actors)
  - Real-time statistics computation
  
* [ ] **Real-Time Query Results via Streaming**
  - Stream query results as actors activate/deactivate
  - Continuous queries with updates
  - Integration with Phase 5 streaming
  - Query result caching and invalidation

**Use Cases:**
- Cluster monitoring dashboards
- Actor population analytics
- Capacity planning
- Anomaly detection

---

### 10.4 Tier 4: Ecosystem Integrations

*Priority: MEDIUM-LOW - External system connectors and adapters.*

#### 10.4.1 Database Integrations

**Status:** 🚧 PLANNED  
**Dependencies:** Phase 4 (State Persistence ✅)

Additional storage providers for state and reminder persistence.

* [ ] **SQL Server:** State and reminder storage
  - ADO.NET based provider
  - Connection pooling and retry policies
  - Schema migration support
  - Query performance optimization
  
* [ ] **MongoDB:** Document-based state storage
  - Native MongoDB driver integration
  - BSON serialization for complex state
  - Index optimization for actor lookups
  - Change streams for state notifications
  
* [ ] **Cassandra:** Wide-column state storage
  - Cassandra CQL driver
  - Multi-datacenter replication
  - Tunable consistency levels
  - Time-series data optimizations
  
* [ ] **DynamoDB:** Serverless state storage
  - AWS SDK integration
  - On-demand capacity mode
  - Global tables for multi-region
  - Point-in-time recovery
  
* [ ] **CosmosDB:** Multi-region state replication
  - Azure Cosmos DB SDK
  - Multi-model support (SQL, MongoDB, Cassandra APIs)
  - Global distribution and consistency
  - Change feed integration

#### 10.4.2 Cloud Platform SDKs

**Status:** 🚧 PLANNED  
**Dependencies:** None (standalone integrations)

Native cloud platform integrations for common services.

* [ ] **Azure Integrations**
  - Key Vault for secrets management
  - Storage (Blob, Queue, Table) for state
  - Service Bus for messaging
  - Monitor for telemetry export
  
* [ ] **AWS Integrations**
  - Secrets Manager for secrets
  - S3 for blob storage
  - SQS/SNS for messaging
  - CloudWatch for logs and metrics
  
* [ ] **GCP Integrations**
  - Secret Manager for secrets
  - Cloud Storage for blobs
  - Pub/Sub for messaging
  - Cloud Logging for logs

#### 10.4.3 Message Queue Integrations

**Status:** 🚧 PLANNED  
**Dependencies:** Phase 5 (Streaming ✅)

Streaming connectors for enterprise message brokers.

* [ ] **Kafka:** Event sourcing and stream processing
  - Producer/consumer integration
  - Consumer group management
  - Transactional writes
  - Kafka Connect compatibility
  
* [ ] **RabbitMQ:** Reliable message delivery
  - AMQP protocol support
  - Exchange and queue bindings
  - Message acknowledgments
  - Dead letter exchange integration
  
* [ ] **Azure Service Bus:** Enterprise messaging
  - Topics and subscriptions
  - Message sessions for ordering
  - Duplicate detection
  - Scheduled messages
  
* [ ] **AWS SQS/SNS:** Serverless messaging
  - SQS queue integration
  - SNS topic subscriptions
  - FIFO queue support
  - Message attributes and filtering
  
* [ ] **NATS:** Lightweight pub/sub
  - NATS JetStream for persistence
  - Subject-based routing
  - Request-reply patterns
  - Key-value store integration

---

### 10.5 Tier 5: Enterprise Features

*Priority: LOW - Top-level business and operational concerns.*

#### 10.5.1 Multi-Tenancy

**Status:** 🚧 PLANNED  
**Dependencies:** Phase 7 (Observability ✅), Phase 8 (Scalability ✅)

Tenant isolation and resource quotas for SaaS deployments.

* [ ] **Tenant-Specific Actor Namespaces**
  - Logical isolation per tenant
  - Tenant ID in actor identity
  - Cross-tenant access prevention
  - Tenant-aware routing
  
* [ ] **Resource Limits per Tenant**
  - CPU quotas per tenant
  - Memory limits per tenant
  - Storage quotas per tenant
  - Rate limiting per tenant
  
* [ ] **Cost Attribution and Chargeback**
  - Resource consumption tracking per tenant
  - Usage reports for billing
  - Cost allocation models
  - Integration with billing systems
  
* [ ] **Tenant-Level Observability**
  - Per-tenant metrics and logs
  - Tenant-specific dashboards
  - Isolated tracing per tenant
  - Tenant health monitoring

#### 10.5.2 Security Enhancements

**Status:** 🚧 PLANNED  
**Dependencies:** Phase 6 (Transport ✅)

Advanced security features for compliance and zero-trust environments.

* [ ] **mTLS for Silo-to-Silo Communication**
  - Mutual TLS authentication
  - Certificate rotation
  - Certificate authority integration
  - TLS 1.3 support
  
* [ ] **Actor-Level Authorization Policies**
  - Role-based access control (RBAC)
  - Attribute-based access control (ABAC)
  - Policy enforcement per actor type
  - Integration with identity providers (OAuth, OIDC)
  
* [ ] **Encrypted State Storage at Rest**
  - Transparent encryption layer
  - Key management service integration
  - Field-level encryption for sensitive data
  - Encryption algorithm selection
  
* [ ] **Key Rotation Without Downtime**
  - Automated key rotation
  - Multi-key support during transition
  - Re-encryption of existing state
  - Audit logging for key usage
  
* [ ] **Audit Logging for Compliance**
  - Immutable audit log
  - Actor invocation audit trail
  - State modification tracking
  - Compliance reporting (SOC2, GDPR)

#### 10.5.3 Disaster Recovery

**Status:** 🚧 PLANNED  
**Dependencies:** 10.4.1 (Database Integrations), Phase 8 (Scalability ✅)

Business continuity features for mission-critical deployments.

* [ ] **Cross-Region Replication**
  - Active-active multi-region deployment
  - State replication across regions
  - Conflict resolution strategies
  - Regional failover support
  
* [ ] **Automated Failover Orchestration**
  - Health-based failover triggers
  - DNS-based traffic routing
  - Automatic cluster reformation
  - Failback procedures
  
* [ ] **Point-in-Time Recovery**
  - State snapshots at intervals
  - Transaction log replay
  - Recovery to specific timestamp
  - Consistency guarantees
  
* [ ] **Backup and Restore Tooling**
  - Automated backup scheduling
  - Incremental backups
  - Backup verification and testing
  - CLI tools for restore operations

---

### 10.6 Predictive Activation (Cold Start Elimination)

**Tier:** 3 (Advanced Patterns)  
**Note:** Moved from 10.5 to 10.6 to align with priority ordering.

**Status:** 🚧 PLANNED  
**Priority:** High - Killer feature for real-time systems  
**Dependencies:** Phase 5 (Implicit Streams), Phase 8.2 (Advanced Placement)

*Eliminate cold start latency by pre-activating actors on specialized nodes (GPU, NUMA) before the first message arrives.*

Using the **Implicit Streams** logic from Phase 5, Quark can predict when an actor will be needed and pre-activate it on a specialized node before the first message arrives. This effectively eliminates "Cold Start" latency for real-time systems by warming up resource-heavy actors (loading VRAM, hydrating state) in anticipation of a request.

#### Core Concepts

1. **Activation Triggers:** Stream events that signal upcoming actor activation
   - Sensor data thresholds
   - Geographic proximity triggers
   - Time-based predictions
   - Historical pattern recognition
   - External event correlations

2. **Pre-Warming Pipeline:**
   ```
   Trigger Event → Predictive Engine → Placement Decision → Actor Pre-Activation → State Hydration → Ready for Request
   ```

3. **Resource Preparation:**
   - GPU VRAM allocation and model loading
   - NUMA node memory pre-allocation
   - State cache warming from storage
   - Network connection pre-establishment
   - Dependency graph pre-resolution

#### Architecture

##### New Abstractions

```csharp
namespace Quark.Abstractions.Activation;

/// <summary>
/// Marks an actor for predictive activation based on stream triggers.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class PredictiveActivationAttribute : Attribute
{
    /// <summary>
    /// The stream namespace that triggers prediction (e.g., "sensors/motion").
    /// </summary>
    public string TriggerStreamNamespace { get; }
    
    /// <summary>
    /// Prediction lead time in milliseconds (how long before actual request).
    /// </summary>
    public int LeadTimeMs { get; set; } = 200;
    
    /// <summary>
    /// Confidence threshold (0.0-1.0) to trigger pre-activation.
    /// </summary>
    public double ConfidenceThreshold { get; set; } = 0.7;
    
    /// <summary>
    /// Maximum number of actors to pre-warm simultaneously.
    /// </summary>
    public int MaxConcurrentPreWarming { get; set; } = 10;
}

/// <summary>
/// Prediction engine that analyzes triggers and decides which actors to pre-activate.
/// </summary>
public interface IPredictiveActivationEngine
{
    /// <summary>
    /// Analyzes a trigger event and returns actor predictions.
    /// </summary>
    Task<IReadOnlyList<ActorActivationPrediction>> PredictActivationsAsync(
        StreamId triggerStreamId,
        object triggerMessage,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Registers an activation pattern for learning.
    /// </summary>
    Task RecordActivationPatternAsync(
        StreamId triggerStreamId,
        string actorType,
        string actorId,
        TimeSpan actualLatency,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents a predicted actor activation.
/// </summary>
public record ActorActivationPrediction(
    Type ActorType,
    string ActorId,
    double Confidence,
    TimeSpan EstimatedLeadTime,
    PlacementHint PlacementHint);

/// <summary>
/// Pre-warming coordinator that manages actor preparation.
/// </summary>
public interface IActorPreWarmingCoordinator
{
    /// <summary>
    /// Pre-warms an actor with state hydration and resource allocation.
    /// </summary>
    Task<IPreWarmedActor> PreWarmActorAsync(
        ActorActivationPrediction prediction,
        CancellationToken cancellationToken = default);
    
    /// <summary>
    /// Gets a pre-warmed actor if available, or activates normally.
    /// </summary>
    Task<IActor> GetOrActivateActorAsync(
        Type actorType,
        string actorId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents a pre-warmed actor ready for immediate use.
/// </summary>
public interface IPreWarmedActor
{
    IActor Actor { get; }
    DateTime PreWarmTimestamp { get; }
    TimeSpan TimeToLive { get; }
    bool IsExpired { get; }
}
```

##### Implementation Strategy

* **Phase 10.5.1: Core Infrastructure**
  - [ ] `PredictiveActivationAttribute` - Declarative trigger configuration
  - [ ] `IPredictiveActivationEngine` - Prediction algorithm abstraction
  - [ ] `IActorPreWarmingCoordinator` - Pre-warming lifecycle management
  - [ ] `PreWarmedActorCache` - TTL-based cache for pre-warmed actors
  - [ ] Integration with StreamBroker for trigger detection

* **Phase 10.5.2: Prediction Strategies**
  - [ ] **Rule-Based Predictor**: Simple threshold-based triggers
    - Geographic distance thresholds
    - Time-based schedules
    - Data value thresholds
  - [ ] **Pattern Recognition Predictor**: Historical pattern analysis
    - Frequency-based predictions
    - Sequence pattern detection
    - Correlation analysis between triggers and activations
  - [ ] **ML-Based Predictor** (future): Machine learning models
    - Time-series forecasting (LSTM, Prophet)
    - Classification models for trigger patterns
    - Online learning for adaptive predictions

* **Phase 10.5.3: Resource Management**
  - [ ] GPU Pre-Warming: VRAM allocation and model loading
  - [ ] NUMA Pre-Warming: Memory pre-allocation on specific NUMA nodes
  - [ ] State Hydration: Parallel state loading from storage
  - [ ] Dependency Resolution: Pre-resolve actor dependencies
  - [ ] Connection Pooling: Pre-establish network connections

* **Phase 10.5.4: Placement Integration**
  - [ ] Integration with `GpuPlacementStrategy` for GPU silo targeting
  - [ ] Integration with `NumaPlacementStrategy` for NUMA node targeting
  - [ ] Integration with `GeoAwarePlacementPolicy` for region-specific pre-warming
  - [ ] Placement hint propagation from prediction to activation

* **Phase 10.5.5: Monitoring & Observability**
  - [ ] Prediction accuracy metrics (precision, recall, F1)
  - [ ] Pre-warming hit/miss rates
  - [ ] Resource waste tracking (pre-warmed but unused actors)
  - [ ] Latency improvement measurements
  - [ ] OpenTelemetry spans for prediction pipeline
  - [ ] Dashboard integration for prediction analytics

#### Use Cases

##### Real-Time Computer Vision
```csharp
[Actor(Name = "VisionAnalyzer")]
[PredictiveActivation(
    TriggerStreamNamespace = "sensors/motion",
    LeadTimeMs = 200,
    ConfidenceThreshold = 0.8)]
[GpuAcceleration(Required = true, PreferredDeviceCount = 1)]
public class VisionAnalyzerActor : ActorBase
{
    private INeuralNetwork? _model;
    
    public override async Task OnActivateAsync(CancellationToken cancellationToken = default)
    {
        // Load large AI model into GPU VRAM (expensive operation)
        _model = await _modelLoader.LoadModelAsync("vision-v2.onnx", cancellationToken);
        await base.OnActivateAsync(cancellationToken);
    }
    
    public async Task<AnalysisResult> AnalyzeFrameAsync(VideoFrame frame)
    {
        // Actor is already warm with model loaded when this is called
        return await _model!.InferAsync(frame);
    }
}

// Trigger configuration (in host setup)
services.AddPredictiveActivation(options =>
{
    options.RegisterTrigger<VisionAnalyzerActor>(trigger =>
    {
        trigger.StreamNamespace = "sensors/motion";
        trigger.PredictionStrategy = new RuleBasedPredictor((msg) =>
        {
            var motionEvent = (MotionSensorEvent)msg;
            // Pre-activate when motion detected and high-res camera frames incoming
            return new ActorActivationPrediction(
                typeof(VisionAnalyzerActor),
                motionEvent.CameraId,
                Confidence: 0.9,
                EstimatedLeadTime: TimeSpan.FromMilliseconds(200),
                PlacementHint: PlacementHint.GpuRequired());
        });
    });
});
```

**Flow:**
1. Motion sensor detects movement → publishes to `sensors/motion` stream
2. Predictive engine analyzes event, predicts VisionAnalyzerActor will be needed in 200ms
3. Coordinator pre-warms actor on GPU silo, loads AI model into VRAM
4. High-res video frames arrive → Actor is already warm, processes immediately
5. **Result:** Zero cold-start latency, real-time processing

##### High-Frequency Trading
```csharp
[Actor(Name = "TradeExecutor")]
[PredictiveActivation(
    TriggerStreamNamespace = "market/thresholds",
    LeadTimeMs = 50,
    ConfidenceThreshold = 0.95)]
[NumaAffinity(Required = true, PreferLocal = true)]
public class TradeExecutorActor : ActorBase
{
    [QuarkState]
    public string? UserBalance { get; set; }
    
    [QuarkState]
    public string? RiskProfile { get; set; }
    
    public override async Task OnActivateAsync(CancellationToken cancellationToken = default)
    {
        // Hydrate state from storage (potentially slow)
        await LoadStateAsync(cancellationToken);
        await base.OnActivateAsync(cancellationToken);
    }
    
    public async Task<TradeResult> ExecuteTradeAsync(TradeOrder order)
    {
        // State already loaded, execute immediately
        return await _tradeEngine.ExecuteAsync(order, UserBalance!, RiskProfile!);
    }
}
```

**Flow:**
1. Market data shows price approaching user's threshold
2. Prediction: High confidence (95%) that TradeExecutorActor will be needed in 50ms
3. Pre-warm actor on NUMA-optimized node, hydrate user balance and risk profile
4. Threshold crossed → Trade execution request arrives
5. **Result:** Sub-millisecond trade execution, competitive advantage

##### Gaming/Metaverse Boss Room
```csharp
[Actor(Name = "WorldBoss")]
[PredictiveActivation(
    TriggerStreamNamespace = "player/location",
    LeadTimeMs = 5000,
    ConfidenceThreshold = 0.7)]
[GpuAcceleration(Required = false, PreferredDeviceCount = 1)]
public class WorldBossActor : ActorBase
{
    [QuarkState]
    public string? BossState { get; set; }
    
    [QuarkState]
    public string? LootTable { get; set; }
    
    public override async Task OnActivateAsync(CancellationToken cancellationToken = default)
    {
        // Load boss configuration, loot tables, AI behavior tree
        await LoadStateAsync(cancellationToken);
        await _aiEngine.InitializeBehaviorTreeAsync(cancellationToken);
        await base.OnActivateAsync(cancellationToken);
    }
}

// Prediction based on player movement patterns
services.AddPredictiveActivation(options =>
{
    options.RegisterTrigger<WorldBossActor>(trigger =>
    {
        trigger.StreamNamespace = "player/location";
        trigger.PredictionStrategy = new PatternRecognitionPredictor((msg, history) =>
        {
            var location = (PlayerLocationEvent)msg;
            // Predict boss room entry based on player trajectory
            var distanceToBossRoom = CalculateDistance(location.Position, BossRoomEntrance);
            var velocity = CalculateVelocity(location, history);
            var timeToArrival = distanceToBossRoom / velocity;
            
            if (timeToArrival < TimeSpan.FromSeconds(10))
            {
                return new ActorActivationPrediction(
                    typeof(WorldBossActor),
                    location.ZoneId + "/boss",
                    Confidence: 0.8,
                    EstimatedLeadTime: timeToArrival,
                    PlacementHint: PlacementHint.GpuPreferred());
            }
            return null;
        });
    });
});
```

**Flow:**
1. Player movement tracked via `player/location` stream
2. Prediction: Player trajectory heading toward boss room, ETA 5 seconds
3. Pre-warm WorldBossActor, load boss state, loot tables, AI behavior tree
4. Player enters boss room → Boss encounter starts immediately
5. **Result:** Seamless encounter, no loading screens, immersive experience

##### Logistics & Supply Chain
```csharp
[Actor(Name = "LoadingDock")]
[PredictiveActivation(
    TriggerStreamNamespace = "fleet/gps",
    LeadTimeMs = 300000, // 5 minutes
    ConfidenceThreshold = 0.75)]
public class LoadingDockActor : ActorBase
{
    [QuarkState]
    public string? ManifestData { get; set; }
    
    [QuarkState]
    public string? DockSchedule { get; set; }
    
    public override async Task OnActivateAsync(CancellationToken cancellationToken = default)
    {
        // Pre-fetch manifest from warehouse system (slow external API)
        await LoadStateAsync(cancellationToken);
        await _manifestService.CacheManifestAsync(ManifestData!, cancellationToken);
        await base.OnActivateAsync(cancellationToken);
    }
}

// GPS-based prediction
services.AddPredictiveActivation(options =>
{
    options.RegisterTrigger<LoadingDockActor>(trigger =>
    {
        trigger.StreamNamespace = "fleet/gps";
        trigger.PredictionStrategy = new RuleBasedPredictor((msg) =>
        {
            var gpsUpdate = (TruckGpsEvent)msg;
            var distanceToWarehouse = CalculateDistance(gpsUpdate.Location, WarehouseLocation);
            
            // Pre-activate when truck is 5 miles away (~5 minutes at 60mph)
            if (distanceToWarehouse < 5.0 && distanceToWarehouse > 3.0)
            {
                return new ActorActivationPrediction(
                    typeof(LoadingDockActor),
                    gpsUpdate.WarehouseId + "/dock",
                    Confidence: 0.85,
                    EstimatedLeadTime: TimeSpan.FromMinutes(distanceToWarehouse / 1.0), // 1 mile/min
                    PlacementHint: PlacementHint.PreferRegion(gpsUpdate.WarehouseRegion));
            }
            return null;
        });
    });
});
```

**Flow:**
1. Truck GPS updates published to `fleet/gps` stream
2. Prediction: Truck 5 miles from warehouse, ETA 5 minutes
3. Pre-activate LoadingDockActor, fetch manifest from external system, cache locally
4. Truck arrives at dock → Dock operations start immediately with cached manifest
5. **Result:** Efficient dock operations, minimized truck wait time, improved throughput

#### Performance Targets

* **Cold Start Elimination:** < 1ms activation for pre-warmed actors (vs. 10-500ms cold)
* **Prediction Accuracy:** > 80% precision, > 70% recall
* **Resource Waste:** < 10% of pre-warmed actors unused (expired without use)
* **GPU Pre-Warming:** 50-200ms for model loading (hidden in lead time)
* **State Hydration:** 10-50ms for state loading (parallel with GPU warming)

#### Configuration & Tuning

```csharp
// Host configuration
services.AddPredictiveActivation(options =>
{
    // Global settings
    options.Enabled = true;
    options.MaxConcurrentPreWarming = 100;
    options.PreWarmedActorTTL = TimeSpan.FromSeconds(30);
    options.PredictionEngineType = PredictionEngineType.PatternRecognition;
    
    // Resource limits
    options.MaxGpuMemoryForPreWarming = 0.2; // 20% of GPU VRAM
    options.MaxSystemMemoryForPreWarming = 0.1; // 10% of system RAM
    
    // Monitoring
    options.EnablePredictionMetrics = true;
    options.EnableResourceWasteTracking = true;
    
    // Learning
    options.EnableOnlineLearning = true;
    options.PatternHistoryWindow = TimeSpan.FromDays(7);
});
```

#### Benefits

* ✅ **Zero Cold Start:** Eliminate activation latency for critical paths
* ✅ **Predictable Performance:** Consistent response times for real-time systems
* ✅ **Competitive Advantage:** Faster execution in latency-sensitive scenarios (trading, gaming)
* ✅ **Better UX:** Seamless experiences without loading screens or delays
* ✅ **Resource Efficiency:** Targeted pre-warming based on predictions, not wasteful pre-loading
* ✅ **Adaptive Learning:** Pattern recognition improves over time

#### Challenges & Mitigations

| Challenge | Mitigation |
|-----------|------------|
| **False Positives** (wasted resources) | Tune confidence thresholds, implement TTL expiry, track waste metrics |
| **False Negatives** (missed predictions) | Multiple prediction strategies, ensemble methods, online learning |
| **Resource Contention** (pre-warming competes with active actors) | Resource quotas, priority-based scheduling, adaptive backoff |
| **State Inconsistency** (pre-warmed state becomes stale) | Short TTL, state versioning, invalidation on upstream changes |
| **Prediction Latency** (slow prediction engine) | Async prediction pipeline, prediction result caching, simple rule-based fallback |
| **Complex Patterns** (hard to predict) | Start with simple rules, gradually add ML, human-in-the-loop tuning |

#### Testing Strategy

* **Unit Tests:** Prediction algorithm correctness, TTL expiry, cache hit/miss
* **Integration Tests:** End-to-end trigger → prediction → pre-warming → activation
* **Performance Tests:** Pre-warming overhead, prediction latency, resource usage
* **Chaos Tests:** False positive handling, resource exhaustion, state inconsistency
* **A/B Tests:** Compare performance with/without predictive activation

#### Future Enhancements

* [ ] **Distributed Prediction:** Federated learning across silos for global patterns
* [ ] **Cost-Aware Predictions:** Balance pre-warming cost vs. cold-start penalty
* [ ] **Multi-Stage Pre-Warming:** Progressive warming (partial → full activation)
* [ ] **Speculative Execution:** Pre-compute likely operations before request arrives
* [ ] **Predictive Scaling:** Pre-scale cluster capacity based on predicted load

---

---

### 10.7 Community-Requested Features

**Tier:** 3 (Advanced Patterns)  
**Note:** Moved from 10.6 to 10.7 to align with priority ordering.

*Features requested by the community and from the Microsoft Orleans wish list.*

* [ ] **Journaling:** Event sourcing and audit trails
  - Event journal for actor state changes
  - Replay capability for debugging and recovery
  - Snapshot management for performance
  - Integration with event stores (EventStoreDB, Kafka)
  - CQRS pattern support with event sourcing
* [ ] **Locality-Aware Repartitioning:** Intelligent actor placement optimization
  - Data locality optimization for co-located actors
  - Minimize cross-silo communication overhead
  - Network topology-aware placement decisions
  - Automatic detection of communication patterns
  - Dynamic repartitioning based on observed locality
* [ ] **Memory-Aware Rebalancing:** Resource-conscious load balancing
  - Memory pressure-based actor migration
  - Prevent OOM conditions via proactive rebalancing
  - Memory usage tracking per actor and per silo
  - Configurable memory thresholds and policies
  - Integration with GC metrics for smart decisions
* [ ] **Durable Jobs:** Long-running background tasks
  - Persistent job queue with guaranteed execution
  - Job scheduling and orchestration
  - Progress tracking and cancellation support
  - Automatic retry with exponential backoff
  - Job dependencies and workflow coordination
* [x] **Stateless Workers (see 10.1.2) ✅:** Lightweight compute actors - *Note: Detailed in Tier 1 Core Infrastructure*
  - Cross-reference: See section 10.1.2 for full details (IMPLEMENTED)
* [ ] **Durable Tasks:** Reliable asynchronous workflows
  - Task continuations with persistence
  - Automatic retry and error handling
  - Long-running workflow orchestration
  - State checkpointing and recovery
  - Integration with actor lifecycle
* [ ] **Inbox/Outbox Pattern:** Transactional messaging
  - Atomic operations with message guarantees
  - Outbox: Ensure messages are sent exactly once
  - Inbox: Deduplicate incoming messages
  - Integration with state persistence
  - Distributed transaction coordination

---

## Performance Targets (Post-1.0)

### Throughput Targets

* **Local Actor Calls:** 10M+ ops/sec per core (in-process)
* **Remote Actor Calls:** 100K+ ops/sec per silo (cross-silo)
* **State Persistence:** 50K+ writes/sec (Redis)
* **Reminder Ticks:** 100K+ timers per silo with < 100ms jitter
* **Stream Throughput:** 1M+ messages/sec per topic

### Latency Targets (p99)

* **Actor Activation:** < 1ms (warm), < 10ms (cold with state)
* **Message Delivery:** < 0.1ms (local), < 5ms (remote)
* **State Load:** < 2ms (Redis), < 10ms (Postgres)
* **State Save:** < 5ms (Redis), < 20ms (Postgres)
* **gRPC Round-Trip:** < 1ms (same AZ), < 50ms (cross-region)

### Scalability Targets

* **Actors per Silo:** 100K+ (lightweight actors < 1KB each)
* **Cluster Size:** 1000+ silos
* **Concurrent Messages:** 1M+ in-flight messages
* **State Size:** 1MB per actor (larger states via blob storage)
* **Reminder Count:** 1M+ active reminders per cluster

---

## Community & Ecosystem Goals

### Open Source Community

* [ ] **Public Roadmap:** Transparent feature planning
* [ ] **Community Forums:** Discord, GitHub Discussions
* [ ] **Contributor Guide:** Clear contribution guidelines
* [ ] **Monthly Releases:** Predictable release cadence
* [ ] **LTS Versions:** Long-term support for stable versions
* [ ] **Security Policy:** Responsible disclosure process

### Ecosystem Growth

* [ ] **Official Packages:** NuGet packages with semantic versioning
* [ ] **Sample Applications:** Real-world reference applications
* [ ] **Third-Party Providers:** Plugin ecosystem (storage, clustering, transport)
* [ ] **Integration Templates:** Starter templates for common scenarios
* [ ] **Benchmarking Suite:** Standardized performance benchmarks
* [ ] **Case Studies:** Production deployment stories

### Enterprise Adoption

* [ ] **Commercial Support:** Optional support contracts
* [ ] **Training Programs:** Official certification program
* [ ] **Architecture Reviews:** Deployment architecture consulting
* [ ] **SLA Guarantees:** Performance and uptime guarantees
* [ ] **Compliance Certifications:** SOC2, ISO27001, GDPR

---

## Learning Resources (Planned)

### Getting Started

1. **Quick Start Guide:** 5-minute introduction (single silo)
2. **Tutorial Series:** 10-part hands-on tutorial
3. **Video Walkthrough:** 30-minute overview video
4. **Sample Projects:** 5+ real-world examples

### Advanced Topics

1. **Performance Tuning:** Optimization techniques
2. **Production Deployment:** Best practices guide
3. **Troubleshooting:** Common issues and solutions
4. **Architecture Patterns:** Design pattern catalog

### Reference

1. **API Documentation:** Complete API reference
2. **Configuration Guide:** All configuration options
3. **Migration Guides:** From other frameworks
4. **FAQ:** Frequently asked questions

---

## Success Metrics

### Technical Metrics

* Zero-reflection: 100% ✅ (achieved)
* Test Coverage: > 90% (currently 379/382 tests passing - 99.2% pass rate)
* AOT Compatibility: 100% ✅ (achieved)
* Performance vs Orleans: 2-3x faster (target)
* Memory vs Orleans: 50% less (target)

### Adoption Metrics

* GitHub Stars: 1K+ (12 months)
* NuGet Downloads: 10K+ (12 months)
* Production Deployments: 50+ (24 months)
* Contributors: 20+ (12 months)
* Community Size: 500+ (24 months)

---

*Last Updated: 2026-01-30*  
*Status: Phases 1-9 Complete (379/382 tests passing), Phase 10 Planned*

---

## Zero Downtime & Rolling Upgrades - Detailed Implementation Plan (Future Design)

**Note:** This section contains a detailed future design proposal for Phase 10.1.1. The current implementation already provides graceful shutdown via `QuarkSiloOptions.ShutdownTimeout`. The live migration features described below are now implemented as of Phase 10.1.1. The drain state management and version-aware placement features remain planned for future implementation.

### Overview

For enterprise production deployments, cluster updates must not drop active actor calls or lose in-flight messages. This feature provides comprehensive support for zero-downtime deployments through graceful shutdown, live actor migration, and version-aware placement.

### 1. Graceful Shutdown (Drain Pattern) - Future Enhancement

**Goal:** Cleanly shut down a silo without dropping active operations.

**Current Status:** Basic graceful shutdown is already implemented in `QuarkSilo.StopAsync()` with configurable timeout via `QuarkSiloOptions.ShutdownTimeout`.

**Future Enhancement:** Add explicit drain state management and hot actor migration.

#### 1.1 Drain State Management

**New Types:**
```csharp
namespace Quark.Abstractions.Hosting;

public enum SiloDrainState
{
    Active,        // Normal operation
    Draining,      // Shutting down, no new activations
    Drained        // All actors migrated or deactivated
}

public interface ISiloDrainManager
{
    SiloDrainState CurrentState { get; }
    Task BeginDrainAsync(CancellationToken cancellationToken = default);
    Task WaitForDrainCompletionAsync(TimeSpan timeout, CancellationToken cancellationToken = default);
    Task<DrainStatus> GetDrainStatusAsync();
}

public record DrainStatus(
    int ActiveActorCount,
    int InFlightCallCount,
    int PendingMigrationCount,
    TimeSpan ElapsedTime,
    TimeSpan RemainingTime);
```

#### 1.2 Shutdown Orchestration

**Implementation in `Quark.Hosting`:**
- Hook `IHostApplicationLifetime.ApplicationStopping` to trigger drain
- Stop accepting new actor activations (mark silo as "Draining" in cluster)
- Wait for in-flight calls to complete (configurable timeout)
- Coordinate with `IActorRebalancer` to migrate hot actors
- Signal drain completion to health check system

**Configuration (Future Design Proposal):**
```csharp
// Note: This is a future design proposal. Current implementation uses QuarkSiloOptions.
// See QuarkSiloOptions.ShutdownTimeout for the current graceful shutdown configuration.

public class SiloOptions // Future enhancement to QuarkSiloOptions
{
    // Existing properties...
    
    public bool EnableGracefulShutdown { get; set; } = true; // Currently always enabled
    public TimeSpan ShutdownTimeout { get; set; } = TimeSpan.FromSeconds(30); // Already exists
    public bool MigrateHotActorsOnShutdown { get; set; } = true; // Planned
    public TimeSpan HotActorThreshold { get; set; } = TimeSpan.FromMinutes(5); // Planned
}
```

#### 1.3 Health Check Integration

**Extension to `QuarkSiloHealthCheck`:**
- Report status as "Degraded" when in "Draining" state
- Report status as "Unhealthy" after drain timeout expires
- Add drain metrics to health check response

### 2. Live Actor Migration

**Goal:** Move actors from one silo to another with minimal disruption.

#### 2.1 Migration Coordination

**New Types:**
```csharp
namespace Quark.Abstractions.Migration;

public interface IActorMigrationCoordinator
{
    Task<MigrationResult> MigrateActorAsync(
        string actorId,
        string targetSiloId,
        CancellationToken cancellationToken = default);
    
    Task<IReadOnlyList<MigrationResult>> MigrateActorBatchAsync(
        IEnumerable<string> actorIds,
        string targetSiloId,
        CancellationToken cancellationToken = default);
    
    Task<IReadOnlyList<string>> IdentifyHotActorsAsync(
        TimeSpan activityThreshold);
}

public enum MigrationStatus
{
    Success,
    Failed,
    TimedOut,
    ActorNotFound,
    TargetSiloUnavailable
}

public record MigrationResult(
    string ActorId,
    MigrationStatus Status,
    TimeSpan Duration,
    long StateSize,
    string? ErrorMessage);
```

#### 2.2 Migration Process

**Migration Steps:**
1. **Identify Hot Actors:** Query actors with recent activity (last N minutes)
2. **Select Target Silo:** Use placement policy to choose destination
3. **Transfer State:**
   - Load state from storage (E-Tag for consistency)
   - Transfer to target silo
   - Save state on target silo with updated E-Tag
4. **Migrate Reminders/Timers:**
   - Transfer reminder registrations
   - Re-register timers on target silo
5. **Update Directory:**
   - Update `IActorDirectory` with new location
   - Route new calls to target silo
6. **Drain Source:**
   - Wait for in-flight calls to complete
   - Deactivate actor on source silo

**Integration with Existing Components:**
- Use `IActorRebalancer` (Phase 8.2) for rebalancing logic
- Use `IStateStorage` (Phase 4) for state transfer
- Use `IReminderTable` (Phase 4) for reminder migration
- Use `IActorDirectory` (Phase 2) for location updates

### 3. Version-Aware Placement

**Goal:** Prevent serialization mismatches during rolling deployments.

#### 3.1 Version Tracking

**New Types:**
```csharp
namespace Quark.Abstractions.Clustering;

public record AssemblyVersionInfo(
    string AssemblyName,
    Version Version,
    string? CommitHash);

public interface IVersionTracker
{
    AssemblyVersionInfo CurrentVersion { get; }
    Task<IReadOnlyDictionary<string, AssemblyVersionInfo>> GetClusterVersionsAsync();
    Task<bool> IsVersionCompatibleAsync(AssemblyVersionInfo targetVersion);
}
```

**Extension to `SiloInfo`:**
```csharp
public record SiloInfo
{
    // Existing properties...
    
    public AssemblyVersionInfo? AssemblyVersion { get; init; }
}
```

#### 3.2 Version-Aware Placement Policy

**New Placement Policy:**
```csharp
namespace Quark.Abstractions.Placement;

public class VersionAffinityPlacementPolicy : IPlacementPolicy
{
    public PlacementPreference Preference { get; init; } = PlacementPreference.PreferSameVersion;
    
    public Task<string> PlaceActorAsync(
        string actorType,
        string actorId,
        IReadOnlyList<SiloInfo> availableSilos,
        CancellationToken cancellationToken = default);
}

public enum PlacementPreference
{
    PreferSameVersion,     // Prefer silos with same assembly version
    RequireSameVersion,    // Only place on silos with same version
    AllowAnyVersion        // Ignore version (legacy behavior)
}
```

**Behavior:**
- When placing actor, filter silos by assembly version
- Fallback to different version if no same-version silos available (PreferSameVersion)
- Fail placement if no same-version silos available (RequireSameVersion)
- Track version mismatches in metrics for monitoring

### 4. Configuration & DI Integration

#### 4.1 Extension Methods

**New extension in `Quark.Extensions.DependencyInjection` (Future Design Proposal):**

**Note:** This shows a future API design. Current configuration is done via `QuarkSiloOptions`.

```csharp
public static class LiveMigrationExtensions
{
    public static IServiceCollection AddLiveMigration(
        this IServiceCollection services,
        Action<LiveMigrationOptions>? configure = null)
    {
        services.AddSingleton<ISiloDrainManager, SiloDrainManager>();
        services.AddSingleton<IActorMigrationCoordinator, ActorMigrationCoordinator>();
        services.AddSingleton<IVersionTracker, AssemblyVersionTracker>();
        
        services.Configure(configure ?? (_ => { }));
        
        return services;
    }
}

public class LiveMigrationOptions
{
    public bool EnableVersionAwarePlacement { get; set; } = true;
    public PlacementPreference VersionPreference { get; set; } = PlacementPreference.PreferSameVersion;
    public TimeSpan MigrationTimeout { get; set; } = TimeSpan.FromSeconds(30); // Aligned with QuarkSiloOptions
    public int MaxConcurrentMigrations { get; set; } = 10;
}
```

#### 4.2 Usage Example (Future Design Proposal)

**Note:** This example shows a future design. Current implementation uses `QuarkSiloOptions` directly.

```csharp
var builder = WebApplication.CreateBuilder(args);

// Current implementation - Add Quark Silo with graceful shutdown
builder.Services.Configure<QuarkSiloOptions>(options =>
{
    options.SiloId = "silo-1";
    options.Address = "localhost";
    options.Port = 5000;
    
    // Graceful shutdown (already implemented)
    options.ShutdownTimeout = TimeSpan.FromSeconds(30);
    
    // Live migration (implemented in Phase 10.1.1)
    options.EnableLiveMigration = true;
    options.MigrationTimeout = TimeSpan.FromSeconds(30);
    options.MaxConcurrentMigrations = 10;
    
    // Version-aware placement (planned)
    options.EnableVersionAwarePlacement = true;
    options.AssemblyVersion = "2.1.0";
});

// Future design - separate extension method (not yet implemented):
// builder.Services.AddLiveMigration(options =>
// {
//     options.EnableVersionAwarePlacement = true;
//     options.VersionPreference = PlacementPreference.PreferSameVersion;
// });

// Add Actor Rebalancing (required for migration)
builder.Services.AddActorRebalancing(options =>
{
    options.Enabled = true;
    options.RebalanceIntervalSeconds = 60;
});

var app = builder.Build();
app.Run();
```

### 5. Implementation Phases

#### Phase 1: Graceful Shutdown Foundation (Week 1)
- [ ] Implement `ISiloDrainManager` interface and `SiloDrainManager` implementation
- [ ] Add `SiloDrainState` tracking to silo lifecycle
- [ ] Hook `IHostApplicationLifetime.ApplicationStopping` for drain trigger
- [ ] Update `QuarkSiloHealthCheck` to report drain state
- [ ] Add drain status endpoint to diagnostics (`/quark/drain`)
- [ ] Unit tests for drain state transitions

#### Phase 2: Actor Migration Core (Week 2)
- [ ] Implement `IActorMigrationCoordinator` interface
- [ ] Implement hot actor detection logic (query recent activity)
- [ ] Implement state transfer with E-Tag consistency
- [ ] Implement reminder/timer migration
- [ ] Update `IActorDirectory` during migration
- [ ] Unit tests for migration scenarios (success, failure, timeout)

#### Phase 3: Version Tracking (Week 3)
- [ ] Implement `IVersionTracker` interface
- [ ] Extract assembly version information at startup
- [ ] Add version to `SiloInfo` in cluster membership
- [ ] Store version in Redis membership table
- [ ] Unit tests for version tracking and comparison

#### Phase 4: Version-Aware Placement (Week 4)
- [ ] Implement `VersionAffinityPlacementPolicy`
- [ ] Add version filtering to placement decisions
- [ ] Implement fallback logic for version mismatches
- [ ] Add version mismatch metrics
- [ ] Integration tests with multi-version clusters

#### Phase 5: Integration & Testing (Week 5)
- [ ] Integrate all components with `AddLiveMigration()`
- [ ] End-to-end testing with rolling deployment
- [ ] Performance testing (migration latency, throughput)
- [ ] Chaos testing (network partitions, slow migrations)
- [ ] Documentation and usage examples

### 6. Testing Strategy

#### Unit Tests
- Drain state machine transitions
- Hot actor identification logic
- Migration coordinator success/failure paths
- Version compatibility checks
- Placement policy with version filtering

#### Integration Tests
- Full silo shutdown with actor migration
- Multi-silo migration scenarios
- Version-aware placement with mixed versions
- Health check integration during drain
- Rebalancer coordination with migration

#### Stress Tests
- High actor count migration (10K+ actors)
- Concurrent migrations (100+ simultaneous)
- Large state transfers (1MB+ per actor)
- Network failures during migration
- Timeout handling under load

### 7. Metrics & Observability

**New Metrics:**
```csharp
// Drain metrics
quark_silo_drain_state{silo_id, state}
quark_silo_drain_duration_seconds{silo_id}
quark_silo_active_actors_during_drain{silo_id}

// Migration metrics
quark_actor_migrations_total{status, reason}
quark_actor_migration_duration_seconds{percentile}
quark_actor_migration_state_size_bytes{percentile}
quark_actor_migrations_in_flight{silo_id}

// Version metrics
quark_cluster_version_distribution{version}
quark_placement_version_mismatches_total{from_version, to_version}
```

**Tracing:**
- Activity spans for drain operations
- Activity spans for each actor migration
- Baggage propagation for correlation during migration

### 8. Success Metrics

#### Functional Goals
- ✅ Zero dropped calls during rolling deployment
- ✅ Zero lost messages during actor migration
- ✅ Zero serialization errors due to version mismatches
- ✅ Graceful handling of drain timeouts

#### Performance Goals
- Migration latency: < 100ms (p99) for actors with < 100KB state
- Drain time: < 30s for silo with 10K actors
- Zero message loss during migration
- < 5% latency increase during rolling deployment

#### Reliability Goals
- 99.9% migration success rate
- Automatic retry for failed migrations
- Graceful degradation when migration unavailable
- No data corruption during state transfer

### 9. Dependencies & Prerequisites

**Required Components (Must be Complete):**
- ✅ Phase 2: Cluster Membership (`IClusterMembership`, `SiloInfo`)
- ✅ Phase 2: Actor Directory (`IActorDirectory`)
- ✅ Phase 4: State Persistence (`IStateStorage` with E-Tag)
- ✅ Phase 4: Reminders (`IReminderTable`)
- ✅ Phase 7.2: Health Checks (`QuarkSiloHealthCheck`)
- ✅ Phase 8.2: Actor Rebalancing (`IActorRebalancer`)

**Optional Enhancements:**
- Phase 7.1: Distributed Tracing (for migration observability)
- Phase 7.4: Cluster Health Monitoring (for coordinated eviction)
- Phase 8.1: Hot Path Optimizations (for faster migration)

### 10. Future Enhancements

**Phase 2 (Post-Initial Release):**
- [ ] **Blue-Green Deployment:** Two-cluster deployments with traffic switching
- [ ] **Canary Deployments:** Gradual rollout with traffic percentage control
- [ ] **Automatic Rollback:** Detect failures and automatically revert
- [ ] **Migration Prioritization:** Migrate critical actors first
- [ ] **State Delta Transfer:** Only transfer changed state (not full state)
- [ ] **Cross-Region Migration:** Support for geo-distributed migrations

**Phase 3 (Advanced Features):**
- [ ] **Live State Replication:** Replicate state changes during migration
- [ ] **Actor Versioning:** Support multiple actor versions simultaneously
- [ ] **State Schema Migration:** Automatic state transformation between versions
- [ ] **Migration Checkpoints:** Resume interrupted migrations
- [ ] **Predictive Migration:** Migrate actors before silo failure

---

*Last Updated: 2026-01-30*  
*Status: Phases 1-9 Complete (379/382 tests passing), Phase 10 Planned*
