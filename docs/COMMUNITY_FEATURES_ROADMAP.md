# Community-Requested Features - Implementation Roadmap

**Document Version:** 1.0  
**Last Updated:** 2026-01-31  
**Status:** Planning Phase  
**Source:** docs/ENHANCEMENTS.md Section 10.7

## Executive Summary

This document provides a comprehensive implementation roadmap for the 6 remaining community-requested features from ENHANCEMENTS.md section 10.7. These features are considered **Tier 3 (Advanced Patterns)** and will significantly enhance Quark's capabilities for production-grade distributed systems.

### Feature Status Overview

| Feature | Status | Priority | Complexity | Estimated Effort |
|---------|--------|----------|------------|------------------|
| Stateless Workers | ✅ COMPLETE | - | - | - |
| Journaling (Event Sourcing) | 🟡 PARTIAL (40%) | HIGH | Medium | 2-3 weeks |
| Locality-Aware Repartitioning | ❌ NOT STARTED | MEDIUM | High | 3-4 weeks |
| Memory-Aware Rebalancing | ❌ NOT STARTED | MEDIUM | High | 3-4 weeks |
| Durable Jobs | ❌ NOT STARTED | HIGH | Medium | 2-3 weeks |
| Durable Tasks | ❌ NOT STARTED | HIGH | Medium | 2-3 weeks |
| Inbox/Outbox Pattern | ❌ NOT STARTED | HIGH | Medium | 2-3 weeks |

**Total Estimated Effort:** 14-19 weeks (3.5-4.75 months)

---

## 1. Journaling (Event Sourcing & Audit Trails)

### Current Status: 🟡 40% Complete

**Existing Implementation:**
- ✅ `Quark.EventSourcing` project exists with basic infrastructure
- ✅ `EventSourcedActor` base class for event-driven state
- ✅ `IEventStore` interface with snapshot support
- ✅ `InMemoryEventStore` for development/testing
- ✅ Optimistic concurrency control
- ✅ Snapshot management for performance

**Missing Components (60%):**

#### 1.1 Production Event Store Implementations

**Priority:** HIGH  
**Effort:** 1-2 weeks

**Required Implementations:**
- [ ] **EventStoreDB Integration**
  - Native event store with projections
  - Stream-based storage (actor ID = stream)
  - Built-in snapshot support
  - Subscription model for projections
  - NuGet: `EventStore.Client.Grpc.Streams`
  
- [ ] **PostgreSQL Event Store**
  - SQL-based event storage
  - Table schema: `events` (actor_id, sequence_number, event_type, payload, timestamp)
  - Index on (actor_id, sequence_number)
  - Snapshot table with versioning
  - Leverage existing `Quark.Storage.Postgres`
  
- [ ] **Redis Streams Event Store**
  - Redis Streams for event log
  - Stream key: `events:{actorId}`
  - Snapshot storage with TTL
  - Consumer groups for projections
  - Leverage existing `Quark.Storage.Redis`
  
- [ ] **Kafka Event Store**
  - Kafka topics per actor type
  - Partition key = actor ID
  - Compaction for snapshots
  - Consumer groups for CQRS projections
  - NuGet: `Confluent.Kafka`

**New Projects:**
```
src/Quark.EventSourcing.EventStoreDB/
src/Quark.EventSourcing.Postgres/
src/Quark.EventSourcing.Redis/
src/Quark.EventSourcing.Kafka/
```

#### 1.2 Replay & Debugging Capabilities

**Priority:** MEDIUM  
**Effort:** 1 week

**Features:**
- [ ] **Event Replay API**
  - Replay events from specific version
  - Replay events within time range
  - Dry-run mode (no state changes)
  - Progress reporting for long replays
  
- [ ] **Time-Travel Debugging**
  - Reconstruct actor state at any point in time
  - Debug historical state transitions
  - Compare state across versions
  
- [ ] **Event Stream Inspector**
  - Query events by actor ID
  - Filter events by type/time
  - Export event streams for analysis

**Implementation Approach:**
```csharp
// Add to IEventStore interface
public interface IEventStore
{
    // Existing methods...
    
    Task<IReadOnlyList<DomainEvent>> ReplayEventsAsync(
        string actorId,
        long fromVersion,
        long toVersion,
        CancellationToken cancellationToken = default);
    
    Task<IReadOnlyList<DomainEvent>> GetEventsByTimeRangeAsync(
        string actorId,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);
}
```

#### 1.3 CQRS Pattern Support

**Priority:** MEDIUM  
**Effort:** 1 week

**Features:**
- [ ] **Projection Engine**
  - Subscribe to event streams
  - Build read models from events
  - Automatic projection updates
  - Projection versioning and migration
  
- [ ] **Read Model Repository**
  - Separate read/write models
  - Optimized queries for read models
  - Cache integration
  
- [ ] **Eventual Consistency Handling**
  - Read-your-writes consistency
  - Stale read detection
  - Retry mechanisms for projections

**New Files:**
```
src/Quark.EventSourcing/IProjection.cs
src/Quark.EventSourcing/ProjectionEngine.cs
src/Quark.EventSourcing/IReadModelRepository.cs
```

**Architecture:**
```
[Actor] --write--> [Event Store] --publish--> [Projection Engine] --update--> [Read Models]
                       ↓
                 [Event Bus]
                       ↓
              [External Subscribers]
```

#### 1.4 Audit Trail & Compliance

**Priority:** LOW  
**Effort:** 1 week

**Features:**
- [ ] **Immutable Event Log**
  - Cryptographic hash chain
  - Tamper detection
  - Event signing (optional)
  
- [ ] **Audit Query API**
  - Who changed what and when
  - Causation and correlation tracking
  - Compliance reporting
  
- [ ] **Retention Policies**
  - Event expiration (GDPR)
  - Archival to cold storage
  - Selective event deletion

**Dependencies:**
- OpenTelemetry for tracing correlation IDs
- Cryptography libraries for hashing/signing

---

## 2. Locality-Aware Repartitioning

### Current Status: ❌ Not Started

**Priority:** MEDIUM  
**Complexity:** HIGH  
**Effort:** 3-4 weeks

### Overview

Intelligent actor placement optimization that minimizes cross-silo communication by analyzing communication patterns and placing frequently-communicating actors on the same silo.

### Architecture Components

#### 2.1 Communication Pattern Analysis

**Effort:** 1 week

**Features:**
- [ ] **Actor Communication Tracker**
  - Track message exchanges between actors
  - Measure message frequency and volume
  - Build communication graph
  - Identify "hot" actor pairs
  
- [ ] **Metrics Collection**
  - Per-actor message counters
  - Inter-silo communication costs
  - Network latency measurements
  - Bandwidth utilization

**Implementation:**
```csharp
public interface ICommunicationPatternAnalyzer
{
    void RecordInteraction(string fromActorId, string toActorId, long messageSize);
    Task<CommunicationGraph> GetCommunicationGraphAsync(TimeSpan window);
    Task<IReadOnlyList<ActorPair>> GetHotPairsAsync(int topN);
}

public class CommunicationGraph
{
    public Dictionary<string, Dictionary<string, CommunicationMetrics>> Edges { get; }
}

public class CommunicationMetrics
{
    public long MessageCount { get; set; }
    public long TotalBytes { get; set; }
    public TimeSpan AverageLatency { get; set; }
}
```

#### 2.2 Network Topology Awareness

**Effort:** 1 week

**Features:**
- [ ] **Data Center Topology**
  - Region/zone/rack awareness
  - Network latency matrix between silos
  - Bandwidth capacity modeling
  
- [ ] **Silo Affinity Groups**
  - Group silos by network proximity
  - Prefer placement within same group
  - Cross-group placement only when necessary

**Configuration:**
```csharp
public class SiloNetworkTopology
{
    public string Region { get; set; }
    public string AvailabilityZone { get; set; }
    public string Rack { get; set; }
    public Dictionary<string, TimeSpan> LatencyToSilos { get; set; }
}
```

#### 2.3 Intelligent Placement Policy

**Effort:** 1 week

**Features:**
- [ ] **Locality-Aware Placement**
  - Co-locate frequently communicating actors
  - Balance locality vs. load distribution
  - Consider network topology in decisions
  
- [ ] **Dynamic Repartitioning**
  - Detect suboptimal placements
  - Trigger actor migration
  - Minimize migration overhead
  
- [ ] **Graph Partitioning Algorithms**
  - METIS-style graph partitioning
  - Min-cut optimization
  - Balance constraints (load, memory)

**New Project:**
```
src/Quark.Placement.Locality/
  - LocalityAwarePlacementPolicy.cs
  - CommunicationGraphPartitioner.cs
  - ActorMigrationCoordinator.cs
```

#### 2.4 Testing & Validation

**Effort:** 1 week

**Features:**
- [ ] **Simulation Framework**
  - Simulate workloads with communication patterns
  - Compare placement strategies
  - Measure cross-silo traffic reduction
  
- [ ] **Benchmarks**
  - Before/after metrics
  - Target: 30-50% reduction in cross-silo traffic

**Dependencies:**
- `Quark.Placement.Abstractions` (existing)
- OpenTelemetry for metrics
- Graph algorithms library (e.g., QuikGraph)

---

## 3. Memory-Aware Rebalancing

### Current Status: ❌ Not Started

**Priority:** MEDIUM  
**Complexity:** HIGH  
**Effort:** 3-4 weeks

### Overview

Resource-conscious load balancing that prevents OOM conditions by monitoring memory usage and proactively migrating actors from memory-constrained silos.

### Architecture Components

#### 3.1 Memory Monitoring Infrastructure

**Effort:** 1 week

**Features:**
- [ ] **Per-Actor Memory Tracking**
  - Track heap memory per actor instance
  - Estimate actor memory footprint
  - Aggregate by actor type
  
- [ ] **Per-Silo Memory Metrics**
  - Total heap usage
  - GC pressure (Gen 0/1/2 collections)
  - Available memory
  - Memory growth rate
  
- [ ] **GC Metrics Integration**
  - Monitor GC pause times
  - Track allocation rate
  - Detect memory pressure signals

**Implementation:**
```csharp
public interface IMemoryMonitor
{
    long GetActorMemoryUsage(string actorId);
    MemoryMetrics GetSiloMemoryMetrics();
    Task<IReadOnlyList<ActorMemoryInfo>> GetTopMemoryConsumersAsync(int count);
}

public class MemoryMetrics
{
    public long TotalMemoryBytes { get; set; }
    public long AvailableMemoryBytes { get; set; }
    public double MemoryPressure { get; set; } // 0.0 - 1.0
    public int Gen0Collections { get; set; }
    public int Gen2Collections { get; set; }
    public TimeSpan LastGCPause { get; set; }
}

public class ActorMemoryInfo
{
    public string ActorId { get; set; }
    public string ActorType { get; set; }
    public long MemoryBytes { get; set; }
}
```

#### 3.2 Memory-Based Placement Policy

**Effort:** 1 week

**Features:**
- [ ] **Memory-Aware Actor Placement**
  - Consider available memory when placing actors
  - Reject placement if insufficient memory
  - Distribute memory-heavy actors
  
- [ ] **Configurable Memory Thresholds**
  - Warning threshold (trigger alerts)
  - Critical threshold (trigger rebalancing)
  - OOM prevention threshold (reject new actors)

**Configuration:**
```csharp
public class MemoryAwarePlacementOptions
{
    public long WarningThresholdBytes { get; set; }
    public long CriticalThresholdBytes { get; set; }
    public double MemoryReservationPercentage { get; set; } = 0.2; // 20% reserve
}
```

#### 3.3 Proactive Rebalancing

**Effort:** 1 week

**Features:**
- [ ] **Automatic Actor Migration**
  - Migrate actors from high-memory silos
  - Select best target silo (low memory usage)
  - Minimize migration disruption
  
- [ ] **Migration Strategies**
  - LRU-based (migrate least recently used actors)
  - Size-based (migrate largest actors first)
  - Criticality-based (never migrate critical actors)
  
- [ ] **Graceful Degradation**
  - Throttle new actor activations
  - Shed load before OOM
  - Circuit breaker for overloaded silos

**New Project:**
```
src/Quark.Placement.Memory/
  - MemoryAwarePlacementPolicy.cs
  - MemoryMonitor.cs
  - ActorMemoryEstimator.cs
  - RebalancingCoordinator.cs
```

#### 3.4 Testing & Validation

**Effort:** 1 week

**Features:**
- [ ] **Memory Pressure Tests**
  - Simulate high-memory workloads
  - Trigger rebalancing scenarios
  - Validate OOM prevention
  
- [ ] **Memory Leak Detection**
  - Detect actors with memory leaks
  - Alert on abnormal memory growth
  - Automatic isolation/restart

**Dependencies:**
- GC monitoring APIs (.NET diagnostics)
- Memory profiling tools
- Integration with `Quark.Profiling.*` projects

---

## 4. Durable Jobs

### Current Status: ❌ Not Started

**Priority:** HIGH  
**Complexity:** MEDIUM  
**Effort:** 2-3 weeks

### Overview

Long-running background tasks with persistent job queue, guaranteed execution, scheduling, and automatic retry with exponential backoff.

### Architecture Components

#### 4.1 Job Queue Infrastructure

**Effort:** 1 week

**Features:**
- [ ] **Persistent Job Queue**
  - Store jobs in durable storage (Redis, Postgres)
  - ACID guarantees for job state changes
  - Support for large job payloads
  
- [ ] **Job Lifecycle Management**
  - States: Pending → Running → Completed/Failed
  - Automatic retry on failure
  - Job timeout and cancellation
  - Progress tracking
  
- [ ] **Job Scheduling**
  - Immediate execution
  - Delayed execution (run at specific time)
  - Recurring jobs (cron-like)
  - Job priority levels

**Implementation:**
```csharp
public interface IJobQueue
{
    Task<string> EnqueueAsync(Job job, CancellationToken cancellationToken = default);
    Task<Job?> DequeueAsync(CancellationToken cancellationToken = default);
    Task CompleteAsync(string jobId, object? result = null, CancellationToken cancellationToken = default);
    Task FailAsync(string jobId, Exception exception, CancellationToken cancellationToken = default);
    Task<JobStatus> GetStatusAsync(string jobId, CancellationToken cancellationToken = default);
}

public class Job
{
    public string JobId { get; set; }
    public string JobType { get; set; }
    public object Payload { get; set; }
    public int Priority { get; set; }
    public DateTimeOffset? ScheduledAt { get; set; }
    public TimeSpan? Timeout { get; set; }
    public RetryPolicy RetryPolicy { get; set; }
    public JobDependencies? Dependencies { get; set; }
}

public enum JobStatus
{
    Pending,
    Running,
    Completed,
    Failed,
    Cancelled
}
```

#### 4.2 Job Orchestration

**Effort:** 1 week

**Features:**
- [ ] **Job Dependencies**
  - Parent-child job relationships
  - Wait for multiple jobs to complete
  - Conditional execution (if/then/else)
  
- [ ] **Workflow Coordination**
  - Job chains (sequential execution)
  - Job DAGs (directed acyclic graphs)
  - Fan-out/fan-in patterns
  
- [ ] **Job Context & Data Passing**
  - Pass data between jobs
  - Shared context across workflow
  - Result aggregation

**Workflow Example:**
```csharp
var workflow = new JobWorkflow()
    .AddJob("fetch-data", new FetchDataJob())
    .AddJob("transform", new TransformJob(), dependsOn: "fetch-data")
    .AddParallelJobs("process-1", "process-2", "process-3", dependsOn: "transform")
    .AddJob("aggregate", new AggregateJob(), dependsOn: ["process-1", "process-2", "process-3"]);

await jobOrchestrator.ExecuteWorkflowAsync(workflow);
```

#### 4.3 Job Execution

**Effort:** 1 week

**Features:**
- [ ] **Job Worker Pool**
  - Dedicated worker actors for job execution
  - Configurable worker count
  - Load balancing across workers
  
- [ ] **Progress Tracking**
  - Report progress percentage
  - Intermediate checkpoints
  - Cancel/pause/resume support
  
- [ ] **Automatic Retry**
  - Exponential backoff
  - Max retry limit
  - Dead letter queue for failed jobs

**New Project:**
```
src/Quark.Jobs/
  - Job.cs
  - IJobQueue.cs
  - JobOrchestrator.cs
  - JobWorkerActor.cs
  - JobWorkflow.cs
```

**Storage Projects:**
```
src/Quark.Jobs.Redis/
src/Quark.Jobs.Postgres/
```

#### 4.4 Testing & Examples

**Effort:** 3-5 days

**Features:**
- [ ] **Unit Tests**
  - Job lifecycle tests
  - Retry logic tests
  - Workflow orchestration tests
  
- [ ] **Integration Tests**
  - End-to-end job execution
  - Failure recovery
  - Concurrent job processing
  
- [ ] **Example Application**
  - `examples/Quark.Examples.DurableJobs/`
  - Image processing pipeline
  - Data ETL workflow

**Dependencies:**
- `Quark.Core.Reminders` (for scheduling)
- `Quark.Storage.*` (for persistence)
- Existing retry policy from DLQ implementation

---

## 5. Durable Tasks

### Current Status: ❌ Not Started

**Priority:** HIGH  
**Complexity:** MEDIUM  
**Effort:** 2-3 weeks

### Overview

Reliable asynchronous workflows with task continuations, automatic retry, state checkpointing, and recovery. Similar to Azure Durable Functions or Temporal.io workflows.

### Architecture Components

#### 5.1 Task Orchestration Engine

**Effort:** 1 week

**Features:**
- [ ] **Orchestration Actor**
  - Coordinate task execution
  - Manage task state and history
  - Handle task continuations
  
- [ ] **Task Definition DSL**
  - Fluent API for task workflows
  - Support for async/await patterns
  - Task composition (sequence, parallel, retry)
  
- [ ] **State Checkpointing**
  - Persist orchestration state
  - Resume from checkpoint on failure
  - Point-in-time recovery

**Implementation:**
```csharp
public abstract class OrchestrationBase
{
    protected async Task<T> CallActivityAsync<T>(string activityName, object input)
    {
        // Execute activity and checkpoint
    }
    
    protected async Task<T> CallSubOrchestrationAsync<T>(string orchestrationName, object input)
    {
        // Execute nested orchestration
    }
    
    protected async Task CreateTimerAsync(TimeSpan delay)
    {
        // Create durable timer
    }
    
    protected async Task<T> WaitForExternalEventAsync<T>(string eventName)
    {
        // Wait for external signal
    }
}

// Example usage
public class OrderProcessingOrchestration : OrchestrationBase
{
    public async Task<OrderResult> RunAsync(OrderRequest request)
    {
        // Call activities in sequence with automatic checkpointing
        var validated = await CallActivityAsync<bool>("ValidateOrder", request);
        if (!validated) return OrderResult.Invalid;
        
        var charged = await CallActivityAsync<bool>("ChargeCard", request.PaymentInfo);
        if (!charged) return OrderResult.PaymentFailed;
        
        // Parallel activities
        await Task.WhenAll(
            CallActivityAsync<bool>("ShipOrder", request.OrderId),
            CallActivityAsync<bool>("SendConfirmationEmail", request.Email)
        );
        
        return OrderResult.Success;
    }
}
```

#### 5.2 Activity Execution

**Effort:** 1 week

**Features:**
- [ ] **Activity Interface**
  - Define reusable activities
  - Input/output serialization
  - Activity versioning
  
- [ ] **Activity Execution**
  - Execute on worker actors
  - Automatic retry on failure
  - Timeout handling
  
- [ ] **Long-Running Activities**
  - Support for hours/days duration
  - Heartbeat mechanism
  - Abandon detection

**Activity Example:**
```csharp
public interface IActivity<TInput, TOutput>
{
    Task<TOutput> ExecuteAsync(TInput input, CancellationToken cancellationToken = default);
}

public class ChargeCardActivity : IActivity<PaymentInfo, bool>
{
    public async Task<bool> ExecuteAsync(PaymentInfo input, CancellationToken cancellationToken)
    {
        // Call payment gateway
        return await PaymentGateway.ChargeAsync(input);
    }
}
```

#### 5.3 Event-Driven Orchestration

**Effort:** 1 week

**Features:**
- [ ] **External Events**
  - Send signals to running orchestrations
  - Human-in-the-loop approval flows
  - External system integration
  
- [ ] **Timers and Delays**
  - Durable timers (survive restarts)
  - Timeout patterns
  - Scheduled executions
  
- [ ] **Sub-Orchestrations**
  - Nested workflows
  - Reusable workflow patterns
  - Fan-out/fan-in scenarios

**New Project:**
```
src/Quark.DurableTasks/
  - OrchestrationBase.cs
  - IActivity.cs
  - OrchestrationContext.cs
  - ActivityInvoker.cs
  - OrchestrationHistory.cs
  - OrchestrationState.cs
```

#### 5.4 Testing & Examples

**Effort:** 3-5 days

**Features:**
- [ ] **Orchestration Testing**
  - Mock activities for testing
  - Replay-based testing
  - Time travel for timer testing
  
- [ ] **Example Application**
  - `examples/Quark.Examples.DurableTasks/`
  - E-commerce order processing
  - Saga pattern implementation
  - Human approval workflow

**Dependencies:**
- `Quark.EventSourcing` (for orchestration history)
- `Quark.Core.Timers` (for durable timers)
- `Quark.Jobs` (for activity execution)

**Key Difference from Durable Jobs:**
- **Durable Tasks:** Focus on orchestration, continuations, and workflow state
- **Durable Jobs:** Focus on persistent queue, batch processing, and scheduling

---

## 6. Inbox/Outbox Pattern

### Current Status: ❌ Not Started

**Priority:** HIGH  
**Complexity:** MEDIUM  
**Effort:** 2-3 weeks

### Overview

Transactional messaging pattern that ensures atomic operations with message guarantees. Outbox ensures messages are sent exactly once, Inbox deduplicates incoming messages, enabling reliable distributed transaction coordination.

### Architecture Components

#### 6.1 Outbox Pattern

**Effort:** 1 week

**Features:**
- [ ] **Transactional Outbox**
  - Store outgoing messages in same transaction as state
  - Atomic state update + message enqueue
  - Guaranteed message delivery
  
- [ ] **Outbox Processor**
  - Background worker to send queued messages
  - Retry failed sends
  - Mark messages as sent
  
- [ ] **Message Deduplication**
  - Idempotency keys for messages
  - Prevent duplicate sends
  - TTL for processed message IDs

**Implementation:**
```csharp
public interface IOutbox
{
    Task EnqueueAsync(OutboxMessage message, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OutboxMessage>> GetPendingMessagesAsync(int batchSize, CancellationToken cancellationToken = default);
    Task MarkAsSentAsync(string messageId, CancellationToken cancellationToken = default);
}

public class OutboxMessage
{
    public string MessageId { get; set; }
    public string Destination { get; set; }
    public object Payload { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public int RetryCount { get; set; }
    public DateTimeOffset? SentAt { get; set; }
}

// Usage in actor
public class OrderActor : StatefulActorBase<OrderState>
{
    private readonly IOutbox _outbox;
    
    public async Task PlaceOrderAsync(OrderRequest request)
    {
        // Update state and enqueue message in same transaction
        using var transaction = await BeginTransactionAsync();
        
        State.OrderId = request.OrderId;
        State.Status = OrderStatus.Placed;
        await SaveStateAsync(transaction);
        
        await _outbox.EnqueueAsync(new OutboxMessage
        {
            MessageId = Guid.NewGuid().ToString(),
            Destination = "OrderConfirmationService",
            Payload = new OrderPlacedEvent { OrderId = request.OrderId }
        }, transaction);
        
        await transaction.CommitAsync();
    }
}
```

#### 6.2 Inbox Pattern

**Effort:** 1 week

**Features:**
- [ ] **Message Deduplication**
  - Track received message IDs
  - Reject duplicate messages
  - Configurable deduplication window
  
- [ ] **Idempotent Processing**
  - Process each message exactly once
  - Skip already-processed messages
  - Clean up old message IDs
  
- [ ] **Message Ordering**
  - Preserve message order (optional)
  - Sequence number tracking
  - Out-of-order detection

**Implementation:**
```csharp
public interface IInbox
{
    Task<bool> IsProcessedAsync(string messageId, CancellationToken cancellationToken = default);
    Task MarkAsProcessedAsync(string messageId, CancellationToken cancellationToken = default);
    Task CleanupOldEntriesAsync(TimeSpan retentionPeriod, CancellationToken cancellationToken = default);
}

// Usage in actor
public class PaymentActor : ActorBase
{
    private readonly IInbox _inbox;
    
    public async Task ProcessPaymentAsync(PaymentMessage message)
    {
        // Check if already processed
        if (await _inbox.IsProcessedAsync(message.MessageId))
        {
            _logger.LogInformation("Duplicate message {MessageId}, skipping", message.MessageId);
            return;
        }
        
        // Process payment
        await ProcessPaymentInternalAsync(message);
        
        // Mark as processed
        await _inbox.MarkAsProcessedAsync(message.MessageId);
    }
}
```

#### 6.3 Distributed Transaction Coordination

**Effort:** 1 week

**Features:**
- [ ] **Two-Phase Commit (2PC)**
  - Prepare phase
  - Commit/rollback phase
  - Transaction coordinator
  
- [ ] **Saga Pattern Integration**
  - Leverage `Quark.Sagas` project
  - Compensating transactions
  - Distributed rollback
  
- [ ] **Transaction Timeout**
  - Detect stuck transactions
  - Automatic rollback
  - Transaction monitoring

**New Project:**
```
src/Quark.Messaging.Transactions/
  - IOutbox.cs
  - IInbox.cs
  - OutboxProcessor.cs
  - InboxDeduplicator.cs
  - TransactionCoordinator.cs
```

**Storage Projects:**
```
src/Quark.Messaging.Transactions.Redis/
src/Quark.Messaging.Transactions.Postgres/
```

#### 6.4 Testing & Examples

**Effort:** 3-5 days

**Features:**
- [ ] **Unit Tests**
  - Outbox/Inbox behavior
  - Deduplication logic
  - Transaction coordination
  
- [ ] **Integration Tests**
  - End-to-end transactional messaging
  - Duplicate message handling
  - Failure recovery scenarios
  
- [ ] **Example Application**
  - `examples/Quark.Examples.Transactions/`
  - Bank transfer with rollback
  - Order processing with saga
  - Event-driven microservices

**Dependencies:**
- `Quark.Sagas` (existing, for saga pattern)
- `Quark.Storage.*` (for persistence)
- `Quark.EventSourcing` (for event log)

---

## Implementation Phases

### Phase 1: High-Priority Core Features (6-8 weeks)

**Goal:** Complete features essential for production workloads

1. **Complete Journaling** (2-3 weeks)
   - Production event stores (EventStoreDB, Postgres, Kafka)
   - Replay & debugging capabilities
   - CQRS support
   
2. **Durable Jobs** (2-3 weeks)
   - Job queue infrastructure
   - Job orchestration & dependencies
   - Progress tracking & retry
   
3. **Inbox/Outbox Pattern** (2-3 weeks)
   - Transactional outbox
   - Inbox deduplication
   - Transaction coordination

**Deliverables:**
- Production-ready event sourcing
- Reliable background job processing
- Transactional messaging guarantees

### Phase 2: Advanced Optimization Features (6-8 weeks)

**Goal:** Performance optimization and resource management

4. **Durable Tasks** (2-3 weeks)
   - Orchestration engine
   - Activity execution
   - Event-driven workflows
   
5. **Memory-Aware Rebalancing** (3-4 weeks)
   - Memory monitoring
   - Proactive rebalancing
   - OOM prevention
   
6. **Locality-Aware Repartitioning** (3-4 weeks)
   - Communication pattern analysis
   - Graph partitioning
   - Dynamic repartitioning

**Deliverables:**
- Complex workflow orchestration
- Intelligent resource management
- Optimized actor placement

---

## Architecture Principles

### 1. AOT Compatibility

All new features **MUST** maintain 100% Native AOT compatibility:
- ✅ No runtime reflection
- ✅ Source generation for serialization
- ✅ All types statically analyzable
- ✅ Test with `PublishAot=true`

### 2. Zero-Allocation Focus

Minimize allocations in hot paths:
- Object pooling for frequently allocated objects
- Span<T> and Memory<T> for buffers
- ValueTask for async operations
- Reuse collections

### 3. Incremental Source Generators

All code generation via incremental generators:
- Fast incremental compilation
- No build performance regression
- Generate factory methods, proxies, serializers

### 4. Integration with Existing Components

Leverage existing Quark infrastructure:
- Use `Quark.Storage.*` for persistence
- Use `Quark.Transport.Grpc` for communication
- Use `Quark.Core.Reminders` for scheduling
- Use `Quark.Placement.*` for actor placement
- Use `Quark.Sagas` for saga patterns
- Use `Quark.Profiling.*` for monitoring

### 5. Testing Strategy

Comprehensive test coverage:
- Unit tests for each component (xUnit)
- Integration tests with Testcontainers
- Example applications demonstrating usage
- Performance benchmarks
- Target: >90% code coverage

---

## Dependencies and Prerequisites

### External Dependencies

| Feature | Required NuGet Packages |
|---------|------------------------|
| EventStoreDB | `EventStore.Client.Grpc.Streams` (v23.0+) |
| Kafka | `Confluent.Kafka` (v2.3+) |
| Graph Algorithms | `QuikGraph` (v2.5+) or custom implementation |
| Cryptography (Audit) | Built-in `System.Security.Cryptography` |

### Internal Dependencies

- **All Features:** `Quark.Abstractions`, `Quark.Core`
- **Persistence:** `Quark.Core.Persistence`, `Quark.Storage.*`
- **Scheduling:** `Quark.Core.Reminders`
- **Monitoring:** `Quark.OpenTelemetry`, `Quark.Profiling.*`
- **Sagas:** `Quark.Sagas`
- **Queries:** `Quark.Queries`

---

## Success Metrics

### Technical Metrics

- [ ] All features maintain 100% AOT compatibility
- [ ] Test coverage >85% for new code
- [ ] Zero high-severity CodeQL alerts
- [ ] Performance benchmarks meet targets:
  - Event sourcing: <10ms read, <20ms write (p99)
  - Durable jobs: >1000 jobs/sec throughput
  - Memory rebalancing: <5s migration time
  - Locality optimization: >30% cross-silo traffic reduction

### Documentation Metrics

- [ ] Complete API documentation for all public APIs
- [ ] Example application for each feature
- [ ] Architecture decision records (ADRs)
- [ ] Integration guides

### Community Metrics

- [ ] Features align with Orleans community requests
- [ ] GitHub discussions/issues addressed
- [ ] User feedback incorporated

---

## Risk Assessment

### High Risk Areas

1. **Complexity of Locality-Aware Repartitioning**
   - Mitigation: Start with simple heuristics, iterate based on benchmarks
   - Consider using existing graph partitioning libraries
   
2. **Memory Monitoring Overhead**
   - Mitigation: Use sampling, not continuous tracking
   - Leverage existing .NET diagnostics APIs
   
3. **Transaction Coordination Complexity**
   - Mitigation: Start with simple 2PC, evolve to saga patterns
   - Leverage existing `Quark.Sagas` infrastructure

### Medium Risk Areas

1. **Performance Impact of Event Sourcing**
   - Mitigation: Snapshot optimization, async projections
   
2. **Durable Tasks State Management**
   - Mitigation: Efficient checkpointing, state compression

### Low Risk Areas

1. **Durable Jobs** - Well-understood pattern
2. **Inbox/Outbox** - Standard transactional pattern

---

## Next Steps

1. **Community Feedback (1 week)**
   - Share roadmap with community
   - Prioritize based on user needs
   - Refine estimates

2. **Prototype High-Priority Features (2 weeks)**
   - Spike Journaling production stores
   - Spike Durable Jobs MVP
   - Validate architectural decisions

3. **Begin Phase 1 Implementation (6-8 weeks)**
   - Start with Journaling completion
   - Parallel track: Durable Jobs
   - Weekly progress reviews

4. **Documentation & Examples (Ongoing)**
   - Create ADRs for major decisions
   - Build example applications
   - Update main documentation

---

## Appendix: Related Documentation

- `docs/ENHANCEMENTS.md` - Full enhancement roadmap
- `docs/PROGRESS.md` - Current implementation status
- `docs/SOURCE_GENERATOR_SETUP.md` - AOT and source generation guide
- `docs/ZERO_REFLECTION_ACHIEVEMENT.md` - Reflection-free architecture
- Existing implementation summaries: `PHASE*.md` files

---

## Change Log

| Date | Version | Changes |
|------|---------|---------|
| 2026-01-31 | 1.0 | Initial roadmap document |

---

**Document Status:** ✅ Ready for Review  
**Approvers:** Quark Core Team, Community Contributors  
**Next Review Date:** 2026-02-15
