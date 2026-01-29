# Actor Model in Quark

The actor model is a computational model that treats "actors" as the universal primitives of concurrent computation. In Quark, actors are lightweight, isolated units that process messages sequentially.

## Core Concepts

### What is an Actor?

An actor is an object that:
1. Has a unique identity (ActorId)
2. Maintains private state
3. Processes messages one at a time (turn-based concurrency)
4. Can create other actors
5. Can send messages to other actors

### Key Properties

- **Isolation**: Actors don't share memory - they communicate only through messages
- **Sequential Processing**: Each actor processes one message at a time, eliminating race conditions
- **Location Transparency**: Actors can be local or remote - the programming model is the same
- **Fault Tolerance**: Actor failures are contained and can be handled by supervisors

## Actor Lifecycle

### Lifecycle States

```
   Created
      ↓
  [Activate]
      ↓
   Active ←→ Processing Messages
      ↓
  [Deactivate]
      ↓
   Stopped
```

### Lifecycle Methods

```csharp
public class MyActor : ActorBase
{
    // Called once when the actor is first activated
    public override Task OnActivateAsync(CancellationToken cancellationToken = default)
    {
        // Initialize resources, load state, subscribe to streams
        return Task.CompletedTask;
    }

    // Called when the actor is being deactivated
    public override Task OnDeactivateAsync(CancellationToken cancellationToken = default)
    {
        // Clean up resources, save state, unsubscribe
        return Task.CompletedTask;
    }
}
```

## Creating Actors

### Using ActorFactory

```csharp
var factory = new ActorFactory();

// Create an actor with a unique ID
var actor = factory.CreateActor<MyActor>("my-actor-1");

// Activate it
await actor.OnActivateAsync();

// Use it
await actor.DoSomethingAsync();

// Deactivate when done
await actor.OnDeactivateAsync();
```

### Virtual Actor Pattern

In Quark, actors follow the "virtual actor" pattern (like Orleans):
- You don't need to explicitly create actors
- Referencing an actor ID automatically activates it
- Actors are automatically deactivated when idle
- The same actor ID always routes to the same instance

```csharp
// These all get the same actor instance
var user1 = factory.CreateActor<UserActor>("user:123");
var user2 = factory.CreateActor<UserActor>("user:123");

// user1 and user2 refer to the same logical actor
Assert.Equal(user1.ActorId, user2.ActorId);
```

## Actor Methods

### Synchronous Methods

For operations that don't require async:

```csharp
public class CounterActor : ActorBase
{
    private int _count;

    public void Increment() => _count++;
    public int GetCount() => _count;
}
```

### Asynchronous Methods

For I/O operations, external calls, or complex processing:

```csharp
public class OrderActor : ActorBase
{
    public async Task ProcessOrderAsync(Order order)
    {
        await ValidateOrderAsync(order);
        await ChargePaymentAsync(order);
        await ShipOrderAsync(order);
    }
}
```

### Best Practices

1. **Keep methods small**: Each method should do one thing
2. **Avoid blocking**: Use async/await for I/O operations
3. **Return values**: Methods can return values directly
4. **CancellationToken**: Always include for cancellable operations

```csharp
public async Task<OrderStatus> GetOrderStatusAsync(
    string orderId, 
    CancellationToken cancellationToken = default)
{
    // Implementation
}
```

## Turn-Based Concurrency

Actors process messages sequentially through a mailbox:

```
Messages arrive → [Mailbox Queue] → Actor processes one at a time
```

### Benefits

- **No locks needed**: Sequential processing eliminates race conditions
- **Predictable behavior**: State changes happen in order
- **Easier reasoning**: No complex synchronization logic

### Example

```csharp
public class BankAccountActor : ActorBase
{
    private decimal _balance;

    // No locks needed - each operation is processed sequentially
    public void Deposit(decimal amount)
    {
        _balance += amount; // Safe - no race conditions
    }

    public bool Withdraw(decimal amount)
    {
        if (_balance >= amount)
        {
            _balance -= amount; // Safe - no race conditions
            return true;
        }
        return false;
    }

    public decimal GetBalance() => _balance;
}
```

Even if multiple threads call these methods, the actor's mailbox ensures they execute one at a time.

## Reentrancy

By default, actors are **non-reentrant**:

```csharp
[Actor(Reentrant = false)]  // Default
public class MyActor : ActorBase { }
```

**Non-reentrant** means:
- The actor processes one message at a time
- While processing, new messages wait in the mailbox
- Prevents interleaving and state corruption

**Reentrant actors** (`Reentrant = true`):
- Can start processing a new message while waiting for async operations
- More complex but potentially higher throughput
- Requires careful state management

### When to Use Reentrancy

Use `Reentrant = true` when:
- Your actor makes many external async calls
- Operation order doesn't matter
- Performance is critical

Use `Reentrant = false` (default) when:
- State consistency is critical
- Operations must complete in order
- You want simpler reasoning about state

## Actor State

### Private State

Actors maintain private state that's not accessible from outside:

```csharp
public class UserActor : ActorBase
{
    private string _userName;      // Private state
    private DateTime _lastLogin;   // Private state
    private int _loginCount;       // Private state

    public void RecordLogin()
    {
        _lastLogin = DateTime.UtcNow;
        _loginCount++;
    }

    // Expose state through methods
    public DateTime GetLastLogin() => _lastLogin;
}
```

### Persistent State

For state that survives restarts, use `StatefulActorBase`:

```csharp
public class OrderActor : StatefulActorBase
{
    [QuarkState]
    public OrderData? Order { get; set; }

    public OrderActor(
        string actorId, 
        IActorFactory? actorFactory = null,
        IStateStorageProvider? stateStorageProvider = null) 
        : base(actorId, actorFactory, stateStorageProvider)
    {
    }
}
```

See [Persistence](Persistence) for details.

## Actor Identity and Grain Keys

Actor identity is crucial in distributed systems:

### String-Based IDs

```csharp
// User IDs
var user = factory.CreateActor<UserActor>("user:12345");

// Order IDs
var order = factory.CreateActor<OrderActor>("order:abc-123");

// Device IDs
var device = factory.CreateActor<DeviceActor>("device:sensor-42");
```

### ID Conventions

Use meaningful, structured IDs:

```csharp
// ✅ Good: Clear, structured
"user:12345"
"order:2024-01-29:abc-123"
"device:temperature:sensor-42"

// ❌ Avoid: Generic, unclear
"a1b2c3"
"actor1"
"temp"
```

### ID Implications

The actor ID determines:
- **Placement**: Where the actor runs in a cluster (via consistent hashing)
- **Routing**: How messages reach the actor
- **Identity**: Logical actor instance across restarts

## Actor Communication Patterns

### Direct Method Calls (Local)

```csharp
var counter = factory.CreateActor<CounterActor>("counter-1");
counter.Increment();
int count = counter.GetCount();
```

### Parent-Child Communication

```csharp
// Parent creates and manages children
var parent = factory.CreateActor<SupervisorActor>("supervisor-1");
var child = await parent.SpawnChildAsync<WorkerActor>("worker-1");

// Parent can send messages to children
await child.ProcessTaskAsync();
```

See [Supervision](Supervision) for details.

### Streaming (Pub/Sub)

```csharp
// Subscribe to a stream
[QuarkStream("orders/new")]
public class OrderProcessorActor : ActorBase, IStreamConsumer<Order>
{
    public async Task OnStreamMessageAsync(
        Order message, 
        StreamId streamId, 
        CancellationToken cancellationToken = default)
    {
        // Process order
    }
}

// Publish to the stream
await streamProvider.GetStream<Order>("orders/new", "key")
    .PublishAsync(new Order { Id = "123" });
```

See [Streaming](Streaming) for details.

## Performance Characteristics

### Memory Footprint

- **Actor instance**: ~1 KB overhead per actor
- **Mailbox**: Configurable capacity (default: 1000 messages)
- **State**: Your data + minimal framework overhead

### Throughput

- **Local calls**: ~1-2 million operations/second per actor
- **Message processing**: Limited by mailbox and your code
- **Distributed calls**: ~50-100K operations/second (network bound)

### Scalability

- **Vertical**: Millions of actors per silo (memory permitting)
- **Horizontal**: Unlimited actors across cluster (via consistent hashing)

## Common Patterns

### Request-Response

```csharp
public class QueryActor : ActorBase
{
    public async Task<QueryResult> QueryAsync(string query)
    {
        var result = await ExecuteQueryAsync(query);
        return result;
    }
}

// Usage
var actor = factory.CreateActor<QueryActor>("query-1");
var result = await actor.QueryAsync("SELECT * FROM users");
```

### Fire-and-Forget

```csharp
public class LoggerActor : ActorBase
{
    public void LogMessage(string message)
    {
        // No return value - just log it
        Console.WriteLine($"[{DateTime.UtcNow}] {message}");
    }
}

// Usage
var logger = factory.CreateActor<LoggerActor>("logger-1");
logger.LogMessage("Something happened"); // Fire and forget
```

### Aggregation

```csharp
public class StatsActor : ActorBase
{
    private readonly Dictionary<string, int> _counters = new();

    public void RecordEvent(string eventType)
    {
        if (!_counters.ContainsKey(eventType))
            _counters[eventType] = 0;
        _counters[eventType]++;
    }

    public Dictionary<string, int> GetStats() => _counters;
}
```

## Best Practices

1. **Keep actors focused**: One actor = one responsibility
2. **Avoid long-running operations**: Break into smaller steps
3. **Use meaningful IDs**: Structure for routing and placement
4. **Handle errors gracefully**: Use supervision for fault tolerance
5. **Consider reentrancy**: Default non-reentrant is usually best
6. **Don't share state**: Communicate through messages only
7. **Use async properly**: Don't block the mailbox

## Next Steps

- **[Supervision](Supervision)** - Learn about parent-child hierarchies and fault tolerance
- **[Persistence](Persistence)** - Add durable state to your actors
- **[Clustering](Clustering)** - Distribute actors across multiple machines

---

**Related**: [Getting Started](Getting-Started) | [Examples](Examples) | [API Reference](API-Reference)
