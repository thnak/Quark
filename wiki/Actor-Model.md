# Actor Model in Quark

The actor model is a computational model where **actors** are the fundamental units of concurrent computation. In Quark, actors are lightweight, stateful objects that process messages sequentially, providing a simple yet powerful abstraction for building distributed systems.

## Table of Contents

1. [Introduction](#introduction)
2. [Virtual Actor Model](#virtual-actor-model)
3. [Actor Types](#actor-types)
4. [Actor Lifecycle](#actor-lifecycle)
5. [Turn-Based Concurrency](#turn-based-concurrency)
6. [Actor Identity and Placement](#actor-identity-and-placement)
7. [Message Passing](#message-passing)
8. [State Management](#state-management)
9. [Supervision](#supervision)
10. [Reentrancy](#reentrancy)
11. [Best Practices](#best-practices)
12. [Performance Characteristics](#performance-characteristics)

---

## Introduction

### What is an Actor?

An **actor** is an independent, encapsulated unit of computation that:

1. **Has a unique identity** (`ActorId`) that persists across activations
2. **Maintains private state** that cannot be accessed directly from outside
3. **Processes messages sequentially** through a mailbox (turn-based concurrency)
4. **Can create child actors** and manage them through supervision
5. **Communicates exclusively through messages** - no shared memory

### Why Use Actors?

**🔒 Isolation**  
Actors don't share memory. All communication happens through messages, eliminating entire classes of concurrency bugs.

**🔄 Sequential Processing**  
Each actor processes one message at a time. No locks, mutexes, or complex synchronization needed.

**🌐 Location Transparency**  
The same code works whether actors are local or distributed across machines. The programming model stays consistent.

**⚡ Fault Tolerance**  
Actor failures are isolated and handled by supervisors. One actor's failure doesn't bring down the system.

**📈 Scalability**  
Actors scale horizontally through clustering and vertically through efficient memory usage.

---

## Virtual Actor Model

Quark implements the **virtual actor pattern**, inspired by Microsoft Orleans. This model provides several key advantages over traditional actor systems (like Akka.NET):

### Key Characteristics

**1. Automatic Activation**  
You don't manually create or destroy actors. Referencing an actor ID automatically activates it if needed.

```csharp
var factory = new ActorFactory();

// First reference - actor is automatically activated
var user1 = factory.CreateActor<UserActor>("user:12345");

// Second reference - returns the same logical actor
var user2 = factory.CreateActor<UserActor>("user:12345");

// Both variables point to the same actor instance
Console.WriteLine(user1.ActorId == user2.ActorId); // Output: true
```

**2. Actor ID Determines Identity**  
An actor's ID is its permanent identity. The same ID always routes to the same logical actor, regardless of physical location.

```csharp
// These all refer to the same logical actor
factory.CreateActor<OrderActor>("order:12345");  // Node A
factory.CreateActor<OrderActor>("order:12345");  // Node B - same actor
```

**3. Transparent Lifecycle Management**  
The runtime manages actor activation and deactivation automatically:

- **Activation**: Actor is created in memory when first accessed
- **Processing**: Actor handles messages while active
- **Deactivation**: Actor is removed from memory after idle timeout
- **Reactivation**: Accessing an actor ID reactivates it with persisted state

**4. Single-Instance Semantics**  
For stateful actors, there is only one active instance per actor ID across the entire cluster. This eliminates distributed state conflicts.

### Comparison with Traditional Actor Systems

| Feature | Traditional Actors (Akka) | Virtual Actors (Quark/Orleans) |
|---------|---------------------------|--------------------------------|
| **Creation** | Manual (`ActorOf()`) | Automatic on first reference |
| **Lifecycle** | Explicitly managed | Automatically managed |
| **Identity** | Actor reference (object) | Actor ID (string) |
| **Location** | Must track actor references | ID-based routing finds actors |
| **Failure** | Must recreate actor reference | Reactivate with same ID |

---

## Actor Types

Quark provides **four actor base classes** for different use cases:

### 1. ActorBase - Stateful Actors

**Use Case**: General-purpose actors with in-memory state (not persisted).

**Characteristics**:
- Private mutable state
- Turn-based concurrency
- Supervision support
- DI container integration

```csharp
using Quark.Abstractions;
using Quark.Core.Actors;

[Actor(Name = "Counter", Reentrant = false)]
public class CounterActor : ActorBase
{
    private int _count;

    public CounterActor(string actorId) : base(actorId)
    {
        _count = 0;
    }

    public void Increment() => _count++;
    
    public int GetCount() => _count;

    public override Task OnActivateAsync(CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"CounterActor {ActorId} activated");
        return Task.CompletedTask;
    }
}
```

**When to Use**:
- Need in-memory state
- State doesn't need to survive restarts
- Standard actor behavior

---

### 2. StatefulActorBase - Persistent Actors

**Use Case**: Actors with durable state that survives deactivation and restarts.

**Characteristics**:
- All features of `ActorBase`
- Automatic state persistence
- Source-generated Load/Save methods
- Supports Redis, PostgreSQL, and custom storage

```csharp
using Microsoft.Extensions.DependencyInjection;
using Quark.Abstractions;
using Quark.Abstractions.Persistence;
using Quark.Core.Actors;

[Actor(Name = "Order", Reentrant = false)]
public class OrderActor : StatefulActorBase
{
    // Marked with [QuarkState] - persisted automatically
    [QuarkState(ProviderName = "redis")]
    public OrderData? Order { get; set; }

    public OrderActor(
        string actorId, 
        IActorFactory? actorFactory = null,
        IServiceScope? serviceScope = null) 
        : base(actorId, actorFactory, serviceScope)
    {
    }

    public async Task CreateOrderAsync(string customerId, decimal total)
    {
        Order = new OrderData(customerId, total, DateTime.UtcNow);
        
        // State is automatically saved when method completes
        await this.SaveStateAsync(); // Generated method
    }

    public override async Task OnActivateAsync(CancellationToken cancellationToken = default)
    {
        // Load state from storage
        await this.LoadStateAsync(cancellationToken); // Generated method
        
        if (Order != null)
            Console.WriteLine($"Order {ActorId} reactivated with data: {Order}");
    }
}

public record OrderData(string CustomerId, decimal Total, DateTime OrderTime);
```

**When to Use**:
- State must survive deactivation/restarts
- Long-lived entities (users, orders, sessions)
- Stateful workflows
- Audit trail requirements

**See also**: [Persistence](Persistence) for full state management details.

---

### 3. StatelessActorBase - Stateless Workers

**Use Case**: High-throughput compute workers without state.

**Characteristics**:
- No state persistence overhead
- Multiple instances per actor ID (load balancing)
- Minimal activation/deactivation cost
- Perfect for stateless processing

```csharp
using Quark.Abstractions;
using Quark.Core.Actors;

[Actor(Name = "ImageProcessor", Stateless = true)]
[StatelessWorker(MinInstances = 2, MaxInstances = 100)]
public class ImageProcessorActor : StatelessActorBase
{
    public ImageProcessorActor(string actorId) : base(actorId)
    {
    }

    // Pure function - no state
    public async Task<ImageResult> ResizeImageAsync(byte[] imageData, int width, int height)
    {
        // CPU-intensive stateless operation
        await Task.Delay(50); // Simulate processing
        
        return new ImageResult
        {
            Width = width,
            Height = height,
            ProcessedAt = DateTime.UtcNow,
            ProcessedBy = ActorId // Each instance has unique ID
        };
    }
}
```

**When to Use**:
- Stateless computation (image processing, validation, parsing)
- High-throughput request processing
- CPU-bound work without side effects
- Parallel processing workloads

**Scaling**: Multiple instances of the same actor type can run simultaneously, load-balanced automatically.

---

### 4. ReactiveActorBase<TIn, TOut> - Stream Processing

**Use Case**: Process asynchronous streams with backpressure and flow control.

**Characteristics**:
- Built-in buffering and backpressure
- Windowing and aggregation operators
- Async stream processing
- Flow control and overflow strategies

```csharp
using System.Runtime.CompilerServices;
using Quark.Abstractions;
using Quark.Abstractions.Streaming;
using Quark.Core.Actors;

[Actor(Name = "SensorAggregator")]
[ReactiveActor(BufferSize = 1000, BackpressureThreshold = 0.8)]
public class SensorAggregatorActor : ReactiveActorBase<SensorReading, SensorSummary>
{
    public SensorAggregatorActor(string actorId) : base(actorId)
    {
    }

    public override async IAsyncEnumerable<SensorSummary> ProcessStreamAsync(
        IAsyncEnumerable<SensorReading> stream,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Aggregate sensor readings every 5 seconds
        var window = new List<SensorReading>();
        var lastEmit = DateTime.UtcNow;

        await foreach (var reading in stream.WithCancellation(cancellationToken))
        {
            window.Add(reading);

            // Emit summary every 5 seconds
            if ((DateTime.UtcNow - lastEmit).TotalSeconds >= 5)
            {
                var avg = window.Average(r => r.Temperature);
                yield return new SensorSummary(ActorId, avg, window.Count);
                
                window.Clear();
                lastEmit = DateTime.UtcNow;
            }
        }
    }
}

public record SensorReading(string SensorId, double Temperature, DateTime Timestamp);
public record SensorSummary(string ActorId, double AvgTemperature, int Count);
```

**When to Use**:
- Real-time data processing (IoT sensors, logs, metrics)
- Stream aggregation and windowing
- Reactive event processing
- Backpressure-sensitive workloads

**See also**: [Streaming](Streaming) for reactive patterns.

---

### 5. IQuarkActor Interfaces - Type-Safe Proxies

**Use Case**: Remote actor invocation with compile-time type safety and AOT support.

**Characteristics**:
- Generates Protobuf contracts automatically
- Type-safe client proxies
- 100% AOT-compatible (no reflection)
- gRPC-based remote invocation

```csharp
// Define interface inheriting from IQuarkActor
public interface ICounterActor : IQuarkActor
{
    void Increment();
    int GetCount();
    Task<string> ProcessMessageAsync(string message);
}

// Implement the interface in your actor
[Actor(Name = "Counter")]
public class CounterActor : ActorBase, ICounterActor
{
    private int _count;

    public CounterActor(string actorId) : base(actorId) { }

    public void Increment() => _count++;
    public int GetCount() => _count;
    
    public async Task<string> ProcessMessageAsync(string message)
    {
        await Task.Delay(10);
        return $"Actor {ActorId} received: {message}";
    }
}

// Client code - works for local OR remote actors
ICounterActor counter = clusterClient.GetActor<ICounterActor>("counter-1");
counter.Increment();
int count = counter.GetCount(); // Type-safe, AOT-friendly
```

**When to Use**:
- Distributed actor invocation across silos
- Type-safe actor contracts
- Remote API-like actor interactions

**See also**: [Source Generators](Source-Generators) for proxy generation details.

---

## Actor Lifecycle

Actors transition through well-defined lifecycle states:

### Lifecycle States

```
   ┌─────────┐
   │ Created │  (Actor ID referenced)
   └────┬────┘
        │
        ▼
   ┌─────────────────┐
   │ OnActivateAsync │  (Initialize resources, load state)
   └────┬────────────┘
        │
        ▼
   ┌──────────────────┐
   │  Active/Ready    │ ◄──┐
   └────┬─────────────┘    │
        │                  │
        ▼                  │
   ┌──────────────────┐    │
   │ Processing Msg   │ ───┘ (Turn-based processing loop)
   └────┬─────────────┘
        │
        ▼
   ┌───────────────────┐
   │ OnDeactivateAsync │  (Clean up resources, save state)
   └────┬──────────────┘
        │
        ▼
   ┌─────────┐
   │ Stopped │  (Removed from memory)
   └─────────┘
```

### Lifecycle Methods

**OnActivateAsync** - Called once when the actor is first activated:

```csharp
public override async Task OnActivateAsync(CancellationToken cancellationToken = default)
{
    // 1. Load persistent state
    await this.LoadStateAsync(cancellationToken);
    
    // 2. Initialize resources
    _httpClient = new HttpClient();
    
    // 3. Subscribe to streams
    await streamProvider.SubscribeAsync<OrderEvent>("orders", ActorId);
    
    // 4. Start timers/reminders
    RegisterTimer(nameof(HeartbeatAsync), HeartbeatAsync, TimeSpan.FromSeconds(30));
    
    Console.WriteLine($"Actor {ActorId} activated");
}
```

**OnDeactivateAsync** - Called when the actor is deactivated (idle timeout or explicit stop):

```csharp
public override async Task OnDeactivateAsync(CancellationToken cancellationToken = default)
{
    // 1. Save persistent state
    await this.SaveStateAsync(cancellationToken);
    
    // 2. Clean up resources
    _httpClient?.Dispose();
    
    // 3. Unsubscribe from streams
    await streamProvider.UnsubscribeAsync<OrderEvent>("orders", ActorId);
    
    // 4. Cancel timers (automatic, but can be explicit)
    
    Console.WriteLine($"Actor {ActorId} deactivated");
}
```

### Important Lifecycle Notes

- `OnActivateAsync` is called **once** per activation (not per message)
- `OnDeactivateAsync` is called **once** when idle timeout expires or silo shuts down
- **State persists** between activations for `StatefulActorBase`
- Actors can be **reactivated** with the same ID after deactivation
- **DI scopes** are created per actor and disposed on deactivation

---

## Turn-Based Concurrency

Quark actors use **turn-based concurrency**: each actor processes one message at a time through a mailbox.

### How It Works

```
                        ┌──────────────┐
  Message A ────────►   │              │
  Message B ────────►   │   Mailbox    │   ───► Process one at a time
  Message C ────────►   │   (Queue)    │
                        └──────────────┘
                              │
                              ▼
                        ┌──────────────┐
                        │     Actor    │
                        │  Processing  │
                        │  Message A   │
                        └──────────────┘
```

### Benefits

**1. No Locks Required**  
State mutations are safe because only one message is processed at a time:

```csharp
public class BankAccountActor : ActorBase
{
    private decimal _balance;

    // No locks needed - sequential processing guarantees safety
    public void Deposit(decimal amount)
    {
        _balance += amount; // ✅ Safe
    }

    public bool Withdraw(decimal amount)
    {
        if (_balance >= amount)
        {
            _balance -= amount; // ✅ Safe - no interleaving
            return true;
        }
        return false;
    }

    public decimal GetBalance() => _balance;
}
```

**2. Predictable State Changes**  
State evolves predictably because operations execute in order:

```csharp
// Thread 1: counter.Increment() → Mailbox
// Thread 2: counter.Increment() → Mailbox
// Thread 3: count = counter.GetCount() → Mailbox

// Execution order: Increment → Increment → GetCount
// Result: count == 2 (deterministic)
```

**3. Easier Reasoning**  
No need to think about locks, races, deadlocks, or complex synchronization.

### The Mailbox

The **mailbox** is an internal message queue managed by the framework:

```csharp
public interface IMailbox
{
    string ActorId { get; }
    int MessageCount { get; }          // Current queue depth
    bool IsProcessing { get; }         // Is actor processing?
    
    ValueTask<bool> PostAsync(IActorMessage message);  // Enqueue message
    Task StartAsync(CancellationToken cancellationToken);
    Task StopAsync(CancellationToken cancellationToken);
}
```

**Default Configuration**:
- **Capacity**: 1000 messages (configurable)
- **Overflow**: Block or drop (configurable)
- **Processing**: One message at a time (turn-based)

---

## Actor Identity and Placement

### Actor Identity

An actor's **identity** is its `ActorId` - a unique string that remains constant across activations, deactivations, and even restarts.

**String-Based IDs**:

```csharp
// User actors
factory.CreateActor<UserActor>("user:12345");
factory.CreateActor<UserActor>("user:67890");

// Order actors
factory.CreateActor<OrderActor>("order:abc-123");
factory.CreateActor<OrderActor>("order:def-456");

// Device actors (IoT)
factory.CreateActor<DeviceActor>("device:sensor-42");
factory.CreateActor<DeviceActor>("device:sensor-99");
```

### ID Conventions

Use **structured, meaningful IDs** for better routing and debugging:

```csharp
// ✅ Good: Clear, structured, hierarchical
"user:12345"
"order:2024-01-29:abc-123"
"device:temperature:sensor-42"
"session:user:12345:session:xyz"

// ❌ Avoid: Generic, opaque, random
"a1b2c3"
"actor1"
"temp"
"abc123xyz"
```

### Placement and Routing

In a distributed cluster, the **actor ID determines placement**:

**1. Consistent Hashing**  
Actor IDs are hashed to determine which silo (node) hosts the actor:

```
ActorId "user:12345" → Hash(user:12345) → Silo 3
ActorId "user:67890" → Hash(user:67890) → Silo 1
```

**2. Sticky Routing**  
The same actor ID always routes to the same silo (until rebalancing):

```csharp
// All references to "user:12345" route to Silo 3
var user1 = factory.CreateActor<UserActor>("user:12345"); // Silo 3
var user2 = factory.CreateActor<UserActor>("user:12345"); // Silo 3
```

**3. Local Call Optimization**  
When an actor calls another actor on the **same silo**, Quark optimizes by skipping network serialization:

```csharp
// If both actors are on Silo 3, this is a fast in-memory call
var order = factory.CreateActor<OrderActor>("order:123");
var user = factory.CreateActor<UserActor>("user:456");
await order.NotifyUserAsync(user); // Local or remote? Framework decides
```

**Performance**: Local calls are **10-100x faster** than remote calls (no network, no serialization).

---

## Message Passing

Actors communicate exclusively through **messages** - method calls are converted to messages internally.

### Direct Method Calls

The simplest communication pattern:

```csharp
// Calling actor methods directly
var counter = factory.CreateActor<CounterActor>("counter-1");

counter.Increment();           // Void method → Fire-and-forget message
int count = counter.GetCount(); // Return value → Request-response message
```

**Behind the scenes**:
1. Method call is wrapped as a message
2. Message is posted to the actor's mailbox
3. Actor processes the message when its turn arrives
4. Result (if any) is returned to the caller

### Async Method Calls

For I/O-bound operations:

```csharp
var order = factory.CreateActor<OrderActor>("order-123");

// Async method call - awaitable
await order.ProcessOrderAsync(new Order { Id = "123" });

// Return async results
var status = await order.GetOrderStatusAsync();
```

### Fire-and-Forget

When you don't need a response:

```csharp
public class LoggerActor : ActorBase
{
    public void LogMessage(string message)
    {
        Console.WriteLine($"[{DateTime.UtcNow}] {message}");
        // No return value
    }
}

// Fire and forget - caller doesn't wait
logger.LogMessage("User logged in");
```

### Request-Response

When you need a result:

```csharp
public class CalculatorActor : ActorBase
{
    public int Add(int a, int b) => a + b;
    
    public async Task<double> ComputeComplexAsync(double input)
    {
        await Task.Delay(100); // Simulate work
        return Math.Sqrt(input);
    }
}

// Synchronous request-response
int result = calculator.Add(2, 3);

// Asynchronous request-response
double complexResult = await calculator.ComputeComplexAsync(42.0);
```

### Actor Context

Every message carries an **actor context** with tracing information:

```csharp
public interface IActorContext
{
    string ActorId { get; }        // Current actor ID
    string? CorrelationId { get; }  // For distributed tracing
    string? RequestId { get; }      // For request tracking
    IReadOnlyDictionary<string, object> Metadata { get; }
}
```

**Usage**:

```csharp
public class TracedActor : ActorBase
{
    public async Task ProcessAsync(string data)
    {
        var context = ActorContext.Current; // AsyncLocal propagation
        Console.WriteLine($"Actor: {context.ActorId}");
        Console.WriteLine($"CorrelationId: {context.CorrelationId}");
        Console.WriteLine($"RequestId: {context.RequestId}");
        
        // Context propagates to child calls
        await CallAnotherActorAsync();
    }
}
```

---

## State Management

Actors can manage state in two ways: **in-memory (transient)** or **persistent (durable)**.

### In-Memory State (ActorBase)

Transient state that **does not survive deactivation**:

```csharp
[Actor(Name = "Counter")]
public class CounterActor : ActorBase
{
    private int _count;               // Lost on deactivation
    private DateTime _lastAccess;     // Lost on deactivation

    public CounterActor(string actorId) : base(actorId)
    {
        _count = 0;
        _lastAccess = DateTime.UtcNow;
    }

    public void Increment()
    {
        _count++;
        _lastAccess = DateTime.UtcNow;
    }

    public int GetCount() => _count;
}
```

**When to Use**: Caches, temporary buffers, session data that can be rebuilt.

### Persistent State (StatefulActorBase)

Durable state that **survives deactivation and restarts**:

```csharp
[Actor(Name = "User")]
public class UserActor : StatefulActorBase
{
    // Persisted properties marked with [QuarkState]
    [QuarkState(ProviderName = "redis")]
    public UserProfile? Profile { get; set; }

    [QuarkState(ProviderName = "redis")]
    public int LoginCount { get; set; }

    public UserActor(
        string actorId, 
        IActorFactory? actorFactory = null,
        IServiceScope? serviceScope = null) 
        : base(actorId, actorFactory, serviceScope)
    {
    }

    public override async Task OnActivateAsync(CancellationToken ct = default)
    {
        // Load state from storage (generated method)
        await this.LoadStateAsync(ct);
        
        Console.WriteLine($"User {ActorId} loaded: {Profile?.Name}, Logins: {LoginCount}");
    }

    public async Task UpdateProfileAsync(string name, string email)
    {
        Profile = new UserProfile(name, email);
        LoginCount++;
        
        // Save state to storage (generated method)
        await this.SaveStateAsync();
    }

    public override async Task OnDeactivateAsync(CancellationToken ct = default)
    {
        // Save state on deactivation
        await this.SaveStateAsync(ct);
    }
}

public record UserProfile(string Name, string Email);
```

### Source-Generated State Methods

The `StateSourceGenerator` automatically generates:

- `LoadStateAsync()` - Load state from storage on activation
- `SaveStateAsync()` - Save state to storage
- `DeleteStateAsync()` - Delete state from storage

**Important**: These methods are **100% AOT-compatible** (zero reflection).

### State Storage Backends

Supported backends:

- **Redis** - High-performance in-memory storage
- **PostgreSQL** - Relational database storage
- **Custom** - Implement `IStateStorageProvider`

**See**: [Persistence](Persistence) for full state management details.

---

## Supervision

Actors form **parent-child hierarchies** where parents supervise children and handle their failures.

### Supervision Basics

**Supervisor Interface**:

```csharp
public interface ISupervisor
{
    Task<TChild> SpawnChildAsync<TChild>(string actorId) where TChild : IActor;
    IReadOnlyCollection<IActor> GetChildren();
    Task<SupervisionDirective> OnChildFailureAsync(ChildFailureContext context);
}
```

**Supervision Directives**:

```csharp
public enum SupervisionDirective
{
    Resume,     // Resume child, keep state
    Restart,    // Restart child, clear state
    Stop,       // Stop child permanently
    Escalate    // Escalate failure to parent's supervisor
}
```

### Spawning Child Actors

```csharp
public class SupervisorActor : ActorBase
{
    public SupervisorActor(string actorId, IActorFactory factory) 
        : base(actorId, factory)
    {
    }

    public async Task StartWorkAsync()
    {
        // Spawn child actors
        var worker1 = await SpawnChildAsync<WorkerActor>("worker-1");
        var worker2 = await SpawnChildAsync<WorkerActor>("worker-2");
        
        // Assign work to children
        await worker1.ProcessTaskAsync("task-1");
        await worker2.ProcessTaskAsync("task-2");
    }
}
```

### Handling Child Failures

```csharp
public class ResilientSupervisorActor : ActorBase
{
    public ResilientSupervisorActor(string actorId, IActorFactory factory) 
        : base(actorId, factory)
    {
    }

    public override Task<SupervisionDirective> OnChildFailureAsync(
        ChildFailureContext context,
        CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"Child {context.ChildActorId} failed: {context.Exception.Message}");
        
        // Decide what to do based on exception type
        return context.Exception switch
        {
            TimeoutException => Task.FromResult(SupervisionDirective.Restart),
            InvalidOperationException => Task.FromResult(SupervisionDirective.Stop),
            _ => Task.FromResult(SupervisionDirective.Escalate)
        };
    }
}
```

**Supervision Strategies**:

1. **Resume**: Ignore the error, keep child running
2. **Restart**: Restart child with clean state
3. **Stop**: Terminate child permanently
4. **Escalate**: Pass failure to parent's supervisor

**See**: [Supervision](Supervision) for detailed fault tolerance patterns.

---

## Reentrancy

**Reentrancy** controls whether an actor can start processing a new message while waiting for an async operation.

### Non-Reentrant (Default)

**Safe and predictable** - the actor finishes one message before starting the next:

```csharp
[Actor(Reentrant = false)]  // Default
public class SafeActor : ActorBase
{
    private int _state;

    public async Task ProcessAsync()
    {
        _state = 1;
        await Task.Delay(100);  // While waiting, no other messages are processed
        _state = 2;              // Guaranteed to execute next
    }
}
```

**Guarantees**:
- Messages complete fully before the next starts
- No interleaving of operations
- State mutations are atomic per message

### Reentrant Actors

**Higher throughput** but requires careful state management:

```csharp
[Actor(Reentrant = true)]
public class ReentrantActor : ActorBase
{
    private int _state;

    public async Task ProcessAsync()
    {
        _state = 1;
        await Task.Delay(100);  // Another message CAN start processing here
        _state = 2;              // ⚠️ _state might have been changed by another message
    }
}
```

**Characteristics**:
- Can process multiple messages concurrently
- Must protect shared state manually (locks, etc.)
- Higher throughput for I/O-bound operations

### When to Use Reentrancy

| Scenario | Reentrant | Non-Reentrant |
|----------|-----------|---------------|
| Simple state mutations | ❌ | ✅ Preferred |
| Heavy I/O operations | ✅ Better throughput | ⚠️ May block mailbox |
| Order-sensitive operations | ❌ | ✅ Guaranteed order |
| Stateless operations | ✅ Safe | ✅ Also safe |

**Recommendation**: Start with `Reentrant = false` (default). Only enable reentrancy if profiling shows it's needed.

---

## Best Practices

### Design Principles

**1. Single Responsibility**  
Each actor should have one clear purpose:

```csharp
// ✅ Good: Focused actors
public class OrderActor : ActorBase { /* Manages order lifecycle */ }
public class PaymentActor : ActorBase { /* Handles payment processing */ }
public class ShippingActor : ActorBase { /* Manages shipping */ }

// ❌ Bad: God actor
public class EverythingActor : ActorBase 
{ 
    /* Manages orders, payments, shipping, users, etc. */ 
}
```

**2. Meaningful Actor IDs**  
Structure IDs for debugging and routing:

```csharp
// ✅ Good: Self-documenting
"user:12345"
"order:2024-01-29:abc-123"
"device:sensor:temperature-42"

// ❌ Bad: Opaque
"actor1"
"xyz123"
```

**3. Use Supervision for Failures**  
Don't swallow exceptions - let supervisors handle them:

```csharp
// ✅ Good: Let supervisor handle failures
public async Task ProcessAsync()
{
    await RiskyOperationAsync(); // Throws exception → supervisor handles
}

// ❌ Bad: Silent failures
public async Task ProcessAsync()
{
    try
    {
        await RiskyOperationAsync();
    }
    catch
    {
        // Swallowed - no one knows it failed
    }
}
```

### Performance Guidelines

**1. Avoid Blocking Operations**  
Never block the mailbox:

```csharp
// ❌ Bad: Blocks the mailbox
public void ProcessAsync()
{
    Thread.Sleep(1000);  // Blocks mailbox!
}

// ✅ Good: Async waiting
public async Task ProcessAsync()
{
    await Task.Delay(1000);  // Mailbox can process other messages if reentrant
}
```

**2. Keep Message Handlers Short**  
Break long operations into smaller steps:

```csharp
// ❌ Bad: Long-running handler
public async Task ProcessLargeFileAsync(string filePath)
{
    var data = await File.ReadAllBytesAsync(filePath); // Could be huge!
    // Process entire file in one go
}

// ✅ Good: Chunked processing
public async Task ProcessFileChunksAsync(string filePath, int chunkSize)
{
    await foreach (var chunk in ReadChunksAsync(filePath, chunkSize))
    {
        ProcessChunk(chunk);
    }
}
```

**3. Use Stateless Actors for Compute**  
For CPU-bound work without state:

```csharp
[Actor(Stateless = true)]
[StatelessWorker(MinInstances = 2, MaxInstances = 100)]
public class DataValidatorActor : StatelessActorBase
{
    public Task<bool> ValidateAsync(string data)
    {
        // Stateless validation logic
        return Task.FromResult(IsValid(data));
    }
}
```

### Anti-Patterns to Avoid

| ❌ Anti-Pattern | ✅ Alternative |
|----------------|---------------|
| Sharing state between actors | Use messages to communicate |
| Creating actors in loops | Use stateless workers with load balancing |
| Swallowing exceptions | Let supervisors handle failures |
| Blocking calls (`Thread.Sleep`) | Use async/await |
| Giant god actors | Split into focused actors |
| Random actor IDs | Use meaningful, structured IDs |

---

## Performance Characteristics

### Memory Footprint

| Component | Size |
|-----------|------|
| Actor instance | ~1 KB overhead |
| Mailbox (default) | ~4 KB (1000 message capacity) |
| Per-actor DI scope | ~2-5 KB (if used) |
| State | Your data size + storage overhead |

**Example**: 1 million actors = ~7 GB RAM (without user state)

### Throughput

| Operation | Throughput |
|-----------|-----------|
| Local actor calls (same silo) | ~1-2 million ops/sec per actor |
| Remote calls (distributed) | ~50-100K ops/sec (network bound) |
| Stateless worker processing | Scales linearly with instances |
| Stream processing (reactive) | Depends on backpressure settings |

### Latency

| Operation | Latency |
|-----------|---------|
| Local call (same silo) | ~100-500 nanoseconds |
| Remote call (gRPC) | ~1-5 milliseconds |
| State persistence (Redis) | ~1-2 milliseconds |
| State persistence (Postgres) | ~5-10 milliseconds |

### Scalability

**Vertical Scaling** (single node):
- Millions of actors per silo (memory-limited)
- Thousands of messages per second per actor

**Horizontal Scaling** (cluster):
- Unlimited actors across cluster
- Consistent hashing distributes load
- Stateless workers scale linearly

---

## Next Steps

**📘 Learn More**:
- **[Supervision](Supervision)** - Parent-child hierarchies and fault tolerance
- **[Persistence](Persistence)** - Add durable state to your actors
- **[Streaming](Streaming)** - Reactive stream processing patterns
- **[Clustering](Clustering)** - Distribute actors across multiple machines
- **[Source Generators](Source-Generators)** - Understand AOT code generation

**💻 Try Examples**:
- **[Examples](Examples)** - Hands-on code samples
- **[Getting Started](Getting-Started)** - Build your first actor

**📚 API Reference**:
- **[API Reference](API-Reference)** - Detailed interface documentation

---

**Related**: [Getting Started](Getting-Started) | [Examples](Examples) | [API Reference](API-Reference)
