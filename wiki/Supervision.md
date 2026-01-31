# Supervision in Quark

Supervision is Quark's built-in fault tolerance mechanism, inspired by Akka and Erlang/OTP's "let it crash" philosophy. Instead of defensive error handling everywhere, supervisors create hierarchies of actors where parent actors monitor and recover their children when failures occur.

## Table of Contents

1. [What is Supervision?](#what-is-supervision)
2. [Supervision Directives](#supervision-directives)
3. [The ISupervisor Interface](#the-isupervisor-interface)
4. [Creating Parent-Child Relationships](#creating-parent-child-relationships)
5. [Supervision Strategies](#supervision-strategies)
6. [Implementing a Supervisor](#implementing-a-supervisor)
7. [Failure Handling Patterns](#failure-handling-patterns)
8. [Supervision Hierarchies](#supervision-hierarchies)
9. [Best Practices](#best-practices)
10. [Common Patterns](#common-patterns)
11. [Troubleshooting](#troubleshooting)

## What is Supervision?

Supervision is a pattern where:
1. **Parent actors** spawn and monitor **child actors**
2. When a child fails, the **parent decides** how to handle it
3. Failures are **contained** and don't crash the entire system
4. **Error handling is separated** from business logic

This creates a **supervision tree** where each level handles failures appropriately:

```
          RootSupervisor
               │
       ┌───────┼───────┐
       ↓       ↓       ↓
   Worker1 Worker2  ManagerActor
                         │
                    ┌────┼────┐
                    ↓    ↓    ↓
                  Task1 Task2 Task3
```

### The "Let It Crash" Philosophy

Rather than defensive programming with try-catch everywhere:
- Let actors crash when they encounter errors
- Supervisors handle recovery automatically
- Business logic stays clean and focused
- Failures are isolated to minimize impact

**Traditional Approach (Defensive):**
```csharp
public async Task ProcessOrder(Order order)
{
    try {
        ValidateOrder(order);
        await SaveToDatabase(order);
        await SendNotification(order);
    } catch (ValidationException ex) {
        // Log and return error
    } catch (DatabaseException ex) {
        // Retry logic here
    } catch (NetworkException ex) {
        // More retry logic
    }
}
```

**Supervision Approach:**
```csharp
// Worker: Focus on business logic
public async Task ProcessOrder(Order order)
{
    ValidateOrder(order);         // Let it throw
    await SaveToDatabase(order);  // Let it throw
    await SendNotification(order); // Let it throw
}

// Supervisor: Handle failures
public override Task<SupervisionDirective> OnChildFailureAsync(
    ChildFailureContext context, CancellationToken ct = default)
{
    return context.Exception switch
    {
        ValidationException => SupervisionDirective.Stop,      // Bad data
        DatabaseException => SupervisionDirective.Restart,     // Transient
        NetworkException => SupervisionDirective.Resume,       // Retry
        _ => SupervisionDirective.Escalate
    };
}
```

## Supervision Directives

When a child actor fails, the supervisor's `OnChildFailureAsync` method is called. The supervisor returns one of four **SupervisionDirective** values that determine what happens next.

### 1. Resume

**Continue processing** with the current state intact.

```csharp
public override Task<SupervisionDirective> OnChildFailureAsync(
    ChildFailureContext context,
    CancellationToken cancellationToken = default)
{
    if (context.Exception is TimeoutException)
    {
        // Transient error - keep going
        return Task.FromResult(SupervisionDirective.Resume);
    }
    // ...
}
```

**When to use:**
- ✅ Transient errors (timeouts, rate limits, temporary network issues)
- ✅ Errors that don't corrupt actor state
- ✅ Errors that can be safely ignored

**Effect:**
- Actor keeps its internal state
- Actor continues processing messages
- No downtime or interruption
- The exception is effectively "swallowed"

**Example scenario:** A worker making HTTP calls encounters a timeout. The request can be retried later, and the worker's state is still valid.

### 2. Restart

**Stop and restart** the child actor with fresh state (default behavior).

```csharp
public override Task<SupervisionDirective> OnChildFailureAsync(
    ChildFailureContext context,
    CancellationToken cancellationToken = default)
{
    if (context.Exception is InvalidOperationException)
    {
        // State corrupted - restart with clean slate
        return Task.FromResult(SupervisionDirective.Restart);
    }
    // ...
}
```

**When to use:**
- ✅ State corruption or inconsistency
- ✅ Recoverable errors requiring clean state
- ✅ "Turn it off and on again" scenarios

**Effect:**
- Actor loses all internal state
- `OnDeactivateAsync()` called on old instance
- New instance created with same Actor ID
- `OnActivateAsync()` called on new instance
- Brief downtime during restart

**Example scenario:** A cache actor's internal state becomes corrupted. Restarting it clears the cache and starts fresh.

### 3. Stop

**Terminate** the child actor permanently.

```csharp
public override Task<SupervisionDirective> OnChildFailureAsync(
    ChildFailureContext context,
    CancellationToken cancellationToken = default)
{
    if (context.Exception is OutOfMemoryException)
    {
        // Unrecoverable - stop the actor
        return Task.FromResult(SupervisionDirective.Stop);
    }
    // ...
}
```

**When to use:**
- ✅ Unrecoverable errors
- ✅ Resource exhaustion (out of memory, too many handles)
- ✅ Actor no longer needed or viable

**Effect:**
- Actor is permanently removed
- `OnDeactivateAsync()` is called
- Actor is removed from supervisor's children
- Cannot be restarted (must spawn a new actor)

**Example scenario:** A worker consistently fails validation checks on startup, indicating configuration is fundamentally broken.

### 4. Escalate

**Pass the error up** to the parent's parent supervisor.

```csharp
public override Task<SupervisionDirective> OnChildFailureAsync(
    ChildFailureContext context,
    CancellationToken cancellationToken = default)
{
    if (context.Exception is DatabaseConnectionException)
    {
        // Can't handle at this level - escalate
        return Task.FromResult(SupervisionDirective.Escalate);
    }
    // ...
}
```

**When to use:**
- ✅ Errors the current supervisor can't handle
- ✅ System-wide issues (database down, network partition)
- ✅ Errors requiring higher-level coordination

**Effect:**
- Error is passed to the supervisor's own supervisor
- Propagates up the supervision hierarchy
- Can cascade to the root supervisor
- Allows for centralized handling of infrastructure failures

**Example scenario:** All database workers fail because the database server is down. The database pool supervisor escalates to a system supervisor that can coordinate a wider response.

### Quick Reference Table

| Directive | State | Lifecycle | Downtime | Use Case |
|-----------|-------|-----------|----------|----------|
| **Resume** | ✅ Preserved | No change | None | Transient errors |
| **Restart** | ❌ Lost | Recreated | Brief | State corruption |
| **Stop** | ❌ Lost | Terminated | Permanent | Unrecoverable |
| **Escalate** | Depends on parent | Depends on parent | Depends on parent | Infrastructure failures |

## The ISupervisor Interface

All actors in Quark inherit from `ActorBase`, which implements the `ISupervisor` interface. This means **every actor can supervise child actors**.

### Interface Definition

```csharp
public interface ISupervisor : IActor
{
    // Called when a child actor fails
    Task<SupervisionDirective> OnChildFailureAsync(
        ChildFailureContext context,
        CancellationToken cancellationToken = default);

    // Spawns a new child actor under this supervisor
    Task<TChild> SpawnChildAsync<TChild>(
        string actorId,
        CancellationToken cancellationToken = default) where TChild : IActor;

    // Gets all child actors currently supervised
    IReadOnlyCollection<IActor> GetChildren();
}
```

### ChildFailureContext

The `ChildFailureContext` provides information about what failed:

```csharp
public sealed class ChildFailureContext
{
    public IActor Child { get; }        // The failed actor instance
    public Exception Exception { get; }  // The exception that was thrown
}
```

### Using the Context

```csharp
public override Task<SupervisionDirective> OnChildFailureAsync(
    ChildFailureContext context,
    CancellationToken cancellationToken = default)
{
    // Access the failed child
    var childActorId = context.Child.ActorId;
    var childType = context.Child.GetType().Name;
    
    // Access the exception
    var errorMessage = context.Exception.Message;
    var errorType = context.Exception.GetType().Name;

    Console.WriteLine($"Actor {childActorId} ({childType}) failed:");
    Console.WriteLine($"  Error: {errorType} - {errorMessage}");

    // Make decisions based on context
    if (context.Child is CriticalWorkerActor)
    {
        return Task.FromResult(SupervisionDirective.Restart);
    }

    return context.Exception switch
    {
        TimeoutException => Task.FromResult(SupervisionDirective.Resume),
        DatabaseException => Task.FromResult(SupervisionDirective.Restart),
        _ => Task.FromResult(SupervisionDirective.Escalate)
    };
}
```

### Default Behavior

If you don't override `OnChildFailureAsync`, the default behavior is:

```csharp
// From ActorBase.cs
public virtual Task<SupervisionDirective> OnChildFailureAsync(
    ChildFailureContext context,
    CancellationToken cancellationToken = default)
{
    // Default: restart the failed child
    return Task.FromResult(SupervisionDirective.Restart);
}
```

## Creating Parent-Child Relationships

### Spawning Child Actors

Use `SpawnChildAsync<T>()` to create a child under a supervisor:

```csharp
[Actor(Name = "Supervisor")]
public class SupervisorActor : ActorBase
{
    public SupervisorActor(string actorId, IActorFactory actorFactory) 
        : base(actorId, actorFactory)
    {
    }

    public override async Task OnActivateAsync(CancellationToken ct = default)
    {
        // Spawn children during activation
        await SpawnChildAsync<WorkerActor>("worker-1");
        await SpawnChildAsync<WorkerActor>("worker-2");
        await SpawnChildAsync<WorkerActor>("worker-3");

        Console.WriteLine($"Supervisor spawned {GetChildren().Count} workers");
    }
}
```

### Accessing Children

```csharp
// Get all children
var children = supervisor.GetChildren();
Console.WriteLine($"Supervisor has {children.Count} children");

// Iterate over children
foreach (var child in children)
{
    Console.WriteLine($"  Child: {child.ActorId}");
}

// Cast to specific type if needed
var workers = children.OfType<WorkerActor>();
foreach (var worker in workers)
{
    await worker.DoWorkAsync("task");
}
```

### Important Constraints

1. **Unique Actor IDs**: Each child must have a unique ID within its supervisor
   ```csharp
   // ✅ Good
   await supervisor.SpawnChildAsync<WorkerActor>("worker-1");
   await supervisor.SpawnChildAsync<WorkerActor>("worker-2");

   // ❌ Bad - throws InvalidOperationException
   await supervisor.SpawnChildAsync<WorkerActor>("worker-1");
   await supervisor.SpawnChildAsync<WorkerActor>("worker-1"); // Duplicate!
   ```

2. **Requires IActorFactory**: You must pass an `IActorFactory` to the parent actor
   ```csharp
   var factory = new ActorFactory();
   
   // ✅ Good - factory provided
   var supervisor = factory.CreateActor<SupervisorActor>("supervisor-1");
   
   // ❌ Bad - no factory
   var supervisor = new SupervisorActor("supervisor-1");
   await supervisor.SpawnChildAsync<WorkerActor>("worker-1"); // Throws!
   ```

3. **AOT Compatibility**: Child actor types must have source generator support
   ```csharp
   // Both supervisor and child need [Actor] attribute
   [Actor(Name = "Parent")]
   public class ParentActor : ActorBase { }

   [Actor(Name = "Child")]
   public class ChildActor : ActorBase { }
   ```

### Full Example

```csharp
using Quark.Abstractions;
using Quark.Core.Actors;

// Create factory
var factory = new ActorFactory();

// Create supervisor
var supervisor = factory.CreateActor<SupervisorActor>("supervisor-1");
await supervisor.OnActivateAsync();

// Spawn children
var worker1 = await supervisor.SpawnChildAsync<WorkerActor>("worker-1");
var worker2 = await supervisor.SpawnChildAsync<WorkerActor>("worker-2");

Console.WriteLine($"Spawned {supervisor.GetChildren().Count} workers");

// Use the children
await worker1.DoWorkAsync("task-A");
await worker2.DoWorkAsync("task-B");
```

## Supervision Strategies

Quark defines three **restart strategies** that determine how failures affect sibling actors. These are defined in the `RestartStrategy` enum but must be implemented manually in your supervision logic.

### OneForOne (Isolated Restart)

When one child fails, **only that child** is affected. Siblings continue running.

**Use case:** Workers are independent and failures don't affect each other.

```
Before failure:      After restart:
Worker1 (✓)          Worker1 (✓)
Worker2 (✗) ──────► Worker2 (✓) [restarted]
Worker3 (✓)          Worker3 (✓)
```

**Implementation:**

```csharp
[Actor(Name = "OneForOneSupervisor")]
public class OneForOneSupervisor : ActorBase
{
    public OneForOneSupervisor(string actorId, IActorFactory factory) 
        : base(actorId, factory) { }

    public override async Task OnActivateAsync(CancellationToken ct = default)
    {
        // Spawn independent workers
        await SpawnChildAsync<OrderWorkerActor>("worker-1");
        await SpawnChildAsync<OrderWorkerActor>("worker-2");
        await SpawnChildAsync<OrderWorkerActor>("worker-3");
    }

    public override Task<SupervisionDirective> OnChildFailureAsync(
        ChildFailureContext context,
        CancellationToken cancellationToken = default)
    {
        // Only the failed child is restarted
        Console.WriteLine($"Restarting only {context.Child.ActorId}");
        return Task.FromResult(SupervisionDirective.Restart);
    }
}
```

**Advantages:**
- ✅ High availability - other workers keep running
- ✅ Failures are isolated
- ✅ Good for stateless or independent workers

**Example:** Payment processing workers handling different orders.

### AllForOne (Coordinated Restart)

When one child fails, **all children** are restarted to maintain consistency.

**Use case:** Children share state or must be synchronized.

```
Before failure:      After restart:
Worker1 (✓)          Worker1 (✓) [restarted]
Worker2 (✗) ──────► Worker2 (✓) [restarted]
Worker3 (✓)          Worker3 (✓) [restarted]
```

**Implementation:**

```csharp
[Actor(Name = "AllForOneSupervisor")]
public class AllForOneSupervisor : ActorBase
{
    public AllForOneSupervisor(string actorId, IActorFactory factory) 
        : base(actorId, factory) { }

    public override async Task OnActivateAsync(CancellationToken ct = default)
    {
        // Spawn coordinated workers
        await SpawnChildAsync<DatabaseReaderActor>("reader");
        await SpawnChildAsync<DatabaseWriterActor>("writer");
        await SpawnChildAsync<CacheActor>("cache");
    }

    public override async Task<SupervisionDirective> OnChildFailureAsync(
        ChildFailureContext context,
        CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"{context.Child.ActorId} failed - restarting ALL children");
        
        // Restart all children to ensure consistency
        var children = GetChildren().ToList();
        
        foreach (var child in children)
        {
            await child.OnDeactivateAsync(cancellationToken);
        }
        
        foreach (var child in children)
        {
            await child.OnActivateAsync(cancellationToken);
        }

        return SupervisionDirective.Resume; // Already handled restart
    }
}
```

**Advantages:**
- ✅ Consistent state across all children
- ✅ Useful for tightly coupled components
- ✅ Prevents partial failure states

**Example:** Database reader/writer/cache that must stay synchronized.

### RestForOne (Sequential Restart)

When one child fails, **restart that child and all children spawned after it**.

**Use case:** Pipeline processing where later stages depend on earlier ones.

```
Before failure:        After restart:
Worker1 (✓)            Worker1 (✓)
Worker2 (✗) ────────► Worker2 (✓) [restarted]
Worker3 (✓)            Worker3 (✓) [restarted]
```

**Implementation:**

```csharp
[Actor(Name = "RestForOneSupervisor")]
public class RestForOneSupervisor : ActorBase
{
    private readonly List<(string Id, IActor Actor)> _childrenInOrder = new();

    public RestForOneSupervisor(string actorId, IActorFactory factory) 
        : base(actorId, factory) { }

    public override async Task<TChild> SpawnChildAsync<TChild>(
        string actorId,
        CancellationToken cancellationToken = default)
    {
        // Track order of child creation
        var child = await base.SpawnChildAsync<TChild>(actorId, cancellationToken);
        _childrenInOrder.Add((actorId, child));
        return child;
    }

    public override async Task OnActivateAsync(CancellationToken ct = default)
    {
        // Spawn pipeline stages in order
        await SpawnChildAsync<IngestionActor>("ingestion");
        await SpawnChildAsync<TransformActor>("transform");
        await SpawnChildAsync<ValidationActor>("validation");
        await SpawnChildAsync<OutputActor>("output");
    }

    public override async Task<SupervisionDirective> OnChildFailureAsync(
        ChildFailureContext context,
        CancellationToken cancellationToken = default)
    {
        // Find the failed child's position
        int failedIndex = _childrenInOrder.FindIndex(
            c => c.Id == context.Child.ActorId);

        Console.WriteLine(
            $"{context.Child.ActorId} failed - restarting it and {_childrenInOrder.Count - failedIndex - 1} subsequent children");

        // Restart this child and all subsequent children
        for (int i = failedIndex; i < _childrenInOrder.Count; i++)
        {
            var child = _childrenInOrder[i].Actor;
            await child.OnDeactivateAsync(cancellationToken);
            await child.OnActivateAsync(cancellationToken);
        }

        return SupervisionDirective.Resume; // Already handled restart
    }
}
```

**Advantages:**
- ✅ Maintains ordering dependencies
- ✅ Useful for pipelines and workflows
- ✅ Avoids restarting independent earlier stages

**Example:** Data processing pipeline: Ingest → Transform → Validate → Output.

### Strategy Comparison

| Strategy | Restarts | Best For | Downtime |
|----------|----------|----------|----------|
| **OneForOne** | Failed child only | Independent workers | Minimal |
| **AllForOne** | All children | Coordinated components | High |
| **RestForOne** | Failed + subsequent | Sequential pipelines | Medium |

## Implementing a Supervisor

### Basic Supervisor


Here's a complete supervisor implementation from the Quark examples:

```csharp
using Quark.Abstractions;
using Quark.Core.Actors;

namespace Quark.Examples.Supervision.Actors;

/// <summary>
/// A supervisor actor with custom supervision strategies.
/// </summary>
[Actor(Name = "CustomSupervisor", Reentrant = false)]
public class CustomSupervisorActor : ActorBase
{
    public CustomSupervisorActor(string actorId, IActorFactory actorFactory) 
        : base(actorId, actorFactory)
    {
    }

    public override async Task OnActivateAsync(CancellationToken ct = default)
    {
        Console.WriteLine($"CustomSupervisor {ActorId} activating");
        
        // Spawn workers during activation
        await SpawnChildAsync<WorkerActor>("worker-1");
        await SpawnChildAsync<WorkerActor>("worker-2");
        await SpawnChildAsync<WorkerActor>("worker-3");
        
        Console.WriteLine($"Spawned {GetChildren().Count} workers");
    }

    public override Task<SupervisionDirective> OnChildFailureAsync(
        ChildFailureContext context,
        CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"Child {context.Child.ActorId} failed: {context.Exception.GetType().Name}");
        Console.WriteLine($"  Message: {context.Exception.Message}");

        // Custom supervision logic based on exception type
        var directive = context.Exception switch
        {
            TimeoutException => SupervisionDirective.Resume,           // Retry
            OutOfMemoryException => SupervisionDirective.Stop,         // Give up
            InvalidOperationException => SupervisionDirective.Escalate, // Pass up
            _ => SupervisionDirective.Restart                          // Default
        };

        Console.WriteLine($"  Directive: {directive}");
        return Task.FromResult(directive);
    }

    public override Task OnDeactivateAsync(CancellationToken ct = default)
    {
        Console.WriteLine($"CustomSupervisor {ActorId} deactivating");
        return base.OnDeactivateAsync(ct);
    }
}
```

### Worker Actor

```csharp
using Quark.Abstractions;
using Quark.Core.Actors;

namespace Quark.Examples.Supervision.Actors;

/// <summary>
/// A simple worker actor that can be supervised.
/// </summary>
[Actor(Name = "Worker", Reentrant = false)]
public class WorkerActor : ActorBase
{
    public WorkerActor(string actorId, IActorFactory actorFactory) 
        : base(actorId, actorFactory)
    {
    }

    public async Task<string> DoWorkAsync(string task)
    {
        Console.WriteLine($"Worker {ActorId} processing: {task}");
        await Task.Delay(100); // Simulate work
        return $"Worker {ActorId} completed: {task}";
    }

    public override Task OnActivateAsync(CancellationToken ct = default)
    {
        Console.WriteLine($"  Worker {ActorId} activated");
        return base.OnActivateAsync(ct);
    }

    public override Task OnDeactivateAsync(CancellationToken ct = default)
    {
        Console.WriteLine($"  Worker {ActorId} deactivated");
        return base.OnDeactivateAsync(ct);
    }
}
```

### Usage Example

```csharp
using Quark.Abstractions;
using Quark.Core.Actors;

// Create factory
var factory = new ActorFactory();

// Create and activate supervisor
var supervisor = factory.CreateActor<CustomSupervisorActor>("supervisor-1");
await supervisor.OnActivateAsync();

// Simulate a failure
var worker = await supervisor.SpawnChildAsync<WorkerActor>("worker-custom");

// Create failure context
var exception = new TimeoutException("Request timed out");
var failureContext = new ChildFailureContext(worker, exception);

// Supervisor decides what to do
var directive = await supervisor.OnChildFailureAsync(failureContext);
Console.WriteLine($"Directive: {directive}"); // Output: Resume
```

### Output

```
CustomSupervisor supervisor-1 activating
  Worker worker-1 activated
  Worker worker-2 activated
  Worker worker-3 activated
Spawned 3 workers
  Worker worker-custom activated
Child worker-custom failed: TimeoutException
  Message: Request timed out
  Directive: Resume
Directive: Resume
```

## Failure Handling Patterns

### Pattern 1: Exception Type-Based Routing

Map exception types to supervision directives:

```csharp
public override Task<SupervisionDirective> OnChildFailureAsync(
    ChildFailureContext context,
    CancellationToken cancellationToken = default)
{
    return context.Exception switch
    {
        // Transient - retry
        TimeoutException => Task.FromResult(SupervisionDirective.Resume),
        HttpRequestException => Task.FromResult(SupervisionDirective.Resume),
        
        // State corruption - restart
        InvalidOperationException => Task.FromResult(SupervisionDirective.Restart),
        NullReferenceException => Task.FromResult(SupervisionDirective.Restart),
        
        // Unrecoverable - stop
        OutOfMemoryException => Task.FromResult(SupervisionDirective.Stop),
        StackOverflowException => Task.FromResult(SupervisionDirective.Stop),
        
        // Infrastructure - escalate
        DatabaseException => Task.FromResult(SupervisionDirective.Escalate),
        RedisException => Task.FromResult(SupervisionDirective.Escalate),
        
        // Default
        _ => Task.FromResult(SupervisionDirective.Restart)
    };
}
```

### Pattern 2: Child Type-Based Routing

Different supervision for different actor types:

```csharp
public override Task<SupervisionDirective> OnChildFailureAsync(
    ChildFailureContext context,
    CancellationToken cancellationToken = default)
{
    return context.Child switch
    {
        // Critical actors - always restart
        CriticalWorkerActor => Task.FromResult(SupervisionDirective.Restart),
        
        // Best-effort actors - resume on errors
        OptionalWorkerActor => Task.FromResult(SupervisionDirective.Resume),
        
        // Temporary actors - stop on failure
        TemporaryWorkerActor => Task.FromResult(SupervisionDirective.Stop),
        
        // Default
        _ => Task.FromResult(SupervisionDirective.Restart)
    };
}
```

### Pattern 3: Restart Count Limiting

Prevent infinite restart loops with exponential backoff:

```csharp
[Actor(Name = "SmartSupervisor")]
public class SmartSupervisor : ActorBase
{
    private readonly Dictionary<string, int> _restartCounts = new();
    private readonly Dictionary<string, DateTime> _lastRestarts = new();

    public SmartSupervisor(string actorId, IActorFactory factory) 
        : base(actorId, factory) { }

    public override async Task<SupervisionDirective> OnChildFailureAsync(
        ChildFailureContext context,
        CancellationToken cancellationToken = default)
    {
        var actorId = context.Child.ActorId;

        // Track restart count
        _restartCounts.TryGetValue(actorId, out int count);
        _restartCounts[actorId] = count + 1;

        // Too many restarts - give up
        if (count >= 5)
        {
            Console.WriteLine($"Actor {actorId} failed {count} times - stopping");
            _restartCounts.Remove(actorId);
            _lastRestarts.Remove(actorId);
            return SupervisionDirective.Stop;
        }

        // Calculate exponential backoff: 1s, 2s, 4s, 8s, 16s
        var delay = TimeSpan.FromSeconds(Math.Pow(2, count));

        // Wait before restarting
        if (_lastRestarts.TryGetValue(actorId, out var lastRestart))
        {
            var timeSinceRestart = DateTime.UtcNow - lastRestart;
            if (timeSinceRestart < delay)
            {
                var remainingWait = delay - timeSinceRestart;
                Console.WriteLine($"Waiting {remainingWait.TotalSeconds:F1}s before restart #{count + 1}");
                await Task.Delay(remainingWait, cancellationToken);
            }
        }

        _lastRestarts[actorId] = DateTime.UtcNow;
        Console.WriteLine($"Restarting {actorId} (attempt #{count + 1})");
        
        return SupervisionDirective.Restart;
    }

    // Call this when a child succeeds to reset counters
    public void RecordSuccess(string actorId)
    {
        _restartCounts.Remove(actorId);
        _lastRestarts.Remove(actorId);
    }
}
```

### Pattern 4: Time Window-Based Limiting

Stop restarting if too many failures occur within a time window:

```csharp
[Actor(Name = "WindowedSupervisor")]
public class WindowedSupervisor : ActorBase
{
    private readonly Dictionary<string, Queue<DateTime>> _failureHistory = new();
    private readonly TimeSpan _timeWindow = TimeSpan.FromMinutes(1);
    private readonly int _maxFailures = 3;

    public WindowedSupervisor(string actorId, IActorFactory factory) 
        : base(actorId, factory) { }

    public override Task<SupervisionDirective> OnChildFailureAsync(
        ChildFailureContext context,
        CancellationToken cancellationToken = default)
    {
        var actorId = context.Child.ActorId;
        var now = DateTime.UtcNow;

        // Initialize history for this actor
        if (!_failureHistory.ContainsKey(actorId))
        {
            _failureHistory[actorId] = new Queue<DateTime>();
        }

        var history = _failureHistory[actorId];
        
        // Remove failures outside the time window
        while (history.Count > 0 && (now - history.Peek()) > _timeWindow)
        {
            history.Dequeue();
        }

        // Record this failure
        history.Enqueue(now);

        // Check if we've exceeded the limit
        if (history.Count >= _maxFailures)
        {
            Console.WriteLine(
                $"Actor {actorId} failed {history.Count} times in {_timeWindow.TotalSeconds}s - stopping");
            _failureHistory.Remove(actorId);
            return Task.FromResult(SupervisionDirective.Stop);
        }

        Console.WriteLine(
            $"Actor {actorId} failed {history.Count}/{_maxFailures} times in window - restarting");
        return Task.FromResult(SupervisionDirective.Restart);
    }
}
```

### Pattern 5: Circuit Breaker

Temporarily stop restarting after repeated failures, then try again:

```csharp
[Actor(Name = "CircuitBreakerSupervisor")]
public class CircuitBreakerSupervisor : ActorBase
{
    private readonly Dictionary<string, CircuitState> _circuits = new();

    private enum CircuitStatus { Closed, Open, HalfOpen }

    private class CircuitState
    {
        public CircuitStatus Status { get; set; } = CircuitStatus.Closed;
        public int FailureCount { get; set; }
        public DateTime LastFailure { get; set; }
        public DateTime CircuitOpenedAt { get; set; }
    }

    public CircuitBreakerSupervisor(string actorId, IActorFactory factory) 
        : base(actorId, factory) { }

    public override Task<SupervisionDirective> OnChildFailureAsync(
        ChildFailureContext context,
        CancellationToken cancellationToken = default)
    {
        var actorId = context.Child.ActorId;

        if (!_circuits.ContainsKey(actorId))
        {
            _circuits[actorId] = new CircuitState();
        }

        var circuit = _circuits[actorId];
        var now = DateTime.UtcNow;

        switch (circuit.Status)
        {
            case CircuitStatus.Closed:
                // Normal operation - count failures
                circuit.FailureCount++;
                circuit.LastFailure = now;

                if (circuit.FailureCount >= 3)
                {
                    // Open the circuit
                    circuit.Status = CircuitStatus.Open;
                    circuit.CircuitOpenedAt = now;
                    Console.WriteLine($"Circuit breaker OPENED for {actorId}");
                    return Task.FromResult(SupervisionDirective.Stop);
                }

                return Task.FromResult(SupervisionDirective.Restart);

            case CircuitStatus.Open:
                // Circuit is open - check if enough time has passed
                if ((now - circuit.CircuitOpenedAt) > TimeSpan.FromSeconds(30))
                {
                    // Try again (half-open state)
                    circuit.Status = CircuitStatus.HalfOpen;
                    Console.WriteLine($"Circuit breaker HALF-OPEN for {actorId} - trying restart");
                    return Task.FromResult(SupervisionDirective.Restart);
                }

                // Still in timeout
                Console.WriteLine($"Circuit breaker still OPEN for {actorId}");
                return Task.FromResult(SupervisionDirective.Stop);

            case CircuitStatus.HalfOpen:
                // Failed again in half-open - back to open
                circuit.Status = CircuitStatus.Open;
                circuit.CircuitOpenedAt = now;
                Console.WriteLine($"Circuit breaker back to OPEN for {actorId}");
                return Task.FromResult(SupervisionDirective.Stop);

            default:
                return Task.FromResult(SupervisionDirective.Restart);
        }
    }

    // Call this when a child succeeds in half-open state
    public void RecordSuccess(string actorId)
    {
        if (_circuits.TryGetValue(actorId, out var circuit))
        {
            circuit.Status = CircuitStatus.Closed;
            circuit.FailureCount = 0;
            Console.WriteLine($"Circuit breaker CLOSED for {actorId}");
        }
    }
}
```

### Pattern 6: Health Monitoring

Track long-term health metrics:

```csharp
[Actor(Name = "HealthMonitorSupervisor")]
public class HealthMonitorSupervisor : ActorBase
{
    private readonly Dictionary<string, HealthStats> _healthStats = new();

    private class HealthStats
    {
        public int TotalFailures { get; set; }
        public int ConsecutiveFailures { get; set; }
        public int TotalRestarts { get; set; }
        public DateTime? LastSuccess { get; set; }
        public DateTime? LastFailure { get; set; }
        public TimeSpan AverageUptime { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public HealthMonitorSupervisor(string actorId, IActorFactory factory) 
        : base(actorId, factory) { }

    public override Task<SupervisionDirective> OnChildFailureAsync(
        ChildFailureContext context,
        CancellationToken cancellationToken = default)
    {
        var actorId = context.Child.ActorId;

        if (!_healthStats.ContainsKey(actorId))
        {
            _healthStats[actorId] = new HealthStats();
        }

        var stats = _healthStats[actorId];
        stats.TotalFailures++;
        stats.ConsecutiveFailures++;
        stats.LastFailure = DateTime.UtcNow;

        // Calculate reliability
        var totalTime = (DateTime.UtcNow - stats.CreatedAt).TotalSeconds;
        var reliability = totalTime > 0 
            ? 1.0 - (stats.TotalFailures / totalTime) 
            : 1.0;

        // Log health metrics
        Console.WriteLine($"Actor {actorId} health:");
        Console.WriteLine($"  Total failures: {stats.TotalFailures}");
        Console.WriteLine($"  Consecutive failures: {stats.ConsecutiveFailures}");
        Console.WriteLine($"  Total restarts: {stats.TotalRestarts}");
        Console.WriteLine($"  Reliability: {reliability:P2}");
        Console.WriteLine($"  Last success: {stats.LastSuccess?.ToString() ?? "Never"}");

        // Decide based on health
        if (stats.ConsecutiveFailures >= 5)
        {
            Console.WriteLine($"  Too many consecutive failures - STOPPING");
            return Task.FromResult(SupervisionDirective.Stop);
        }

        if (reliability < 0.5 && stats.TotalFailures > 10)
        {
            Console.WriteLine($"  Reliability too low - STOPPING");
            return Task.FromResult(SupervisionDirective.Stop);
        }

        stats.TotalRestarts++;
        return Task.FromResult(SupervisionDirective.Restart);
    }

    // Call this when child succeeds
    public void RecordSuccess(string actorId)
    {
        if (_healthStats.TryGetValue(actorId, out var stats))
        {
            stats.ConsecutiveFailures = 0;
            stats.LastSuccess = DateTime.UtcNow;
        }
    }

    // Get health report
    public Dictionary<string, HealthStats> GetHealthReport()
    {
        return new Dictionary<string, HealthStats>(_healthStats);
    }
}
```

## Supervision Hierarchies

Complex systems need **multi-level supervision hierarchies** where supervisors supervise other supervisors.

### Multi-Level Hierarchy Example

```csharp
// Level 1: System Supervisor (handles infrastructure failures)
[Actor(Name = "SystemSupervisor")]
public class SystemSupervisor : ActorBase
{
    public SystemSupervisor(string actorId, IActorFactory factory) 
        : base(actorId, factory) { }

    public override async Task OnActivateAsync(CancellationToken ct = default)
    {
        // Spawn subsystem supervisors
        await SpawnChildAsync<DatabaseSupervisor>("db-supervisor");
        await SpawnChildAsync<ApiSupervisor>("api-supervisor");
        await SpawnChildAsync<CacheSupervisor>("cache-supervisor");
    }

    public override Task<SupervisionDirective> OnChildFailureAsync(
        ChildFailureContext context,
        CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"System-level failure in {context.Child.ActorId}");
        
        // Restart failed subsystems
        return Task.FromResult(SupervisionDirective.Restart);
    }
}

// Level 2: Database Supervisor (handles DB worker failures)
[Actor(Name = "DatabaseSupervisor")]
public class DatabaseSupervisor : ActorBase
{
    public DatabaseSupervisor(string actorId, IActorFactory factory) 
        : base(actorId, factory) { }

    public override async Task OnActivateAsync(CancellationToken ct = default)
    {
        // Spawn database workers
        await SpawnChildAsync<DatabaseReaderActor>("db-reader-1");
        await SpawnChildAsync<DatabaseReaderActor>("db-reader-2");
        await SpawnChildAsync<DatabaseWriterActor>("db-writer-1");
    }

    public override Task<SupervisionDirective> OnChildFailureAsync(
        ChildFailureContext context,
        CancellationToken cancellationToken = default)
    {
        return context.Exception switch
        {
            // Database connection issues - escalate to system level
            DatabaseConnectionException => SupervisionDirective.Escalate,
            
            // Query errors - restart the worker
            QueryException => SupervisionDirective.Restart,
            
            // Default
            _ => SupervisionDirective.Restart
        };
    }
}

// Level 3: Database Workers (do the actual work)
[Actor(Name = "DatabaseReader")]
public class DatabaseReaderActor : ActorBase
{
    public DatabaseReaderActor(string actorId, IActorFactory factory) 
        : base(actorId, factory) { }

    public async Task<string> ReadDataAsync(string key)
    {
        // Database read logic
        await Task.Delay(10);
        return $"Data for {key}";
    }
}
```

### Hierarchy Diagram

```
SystemSupervisor (root)
├── DatabaseSupervisor
│   ├── DatabaseReaderActor (worker-1)
│   ├── DatabaseReaderActor (worker-2)
│   └── DatabaseWriterActor (worker-1)
├── ApiSupervisor
│   ├── HttpHandlerActor (handler-1)
│   ├── HttpHandlerActor (handler-2)
│   └── WebSocketActor (ws-1)
└── CacheSupervisor
    ├── CacheReaderActor (reader-1)
    └── CacheWriterActor (writer-1)
```

### Escalation Chain

When a failure escalates, it moves up the hierarchy:

```
Worker fails with DatabaseConnectionException
    ↓
DatabaseSupervisor.OnChildFailureAsync() returns Escalate
    ↓
SystemSupervisor.OnChildFailureAsync() handles it
```

**Implementation:**

```csharp
// Worker level
[Actor(Name = "Worker")]
public class WorkerActor : ActorBase
{
    public async Task ProcessAsync()
    {
        // Let it throw - supervisor will handle
        throw new DatabaseConnectionException("Connection failed");
    }
}

// Mid-level supervisor
[Actor(Name = "PoolSupervisor")]
public class PoolSupervisor : ActorBase
{
    public override Task<SupervisionDirective> OnChildFailureAsync(
        ChildFailureContext context,
        CancellationToken cancellationToken = default)
    {
        // Can't handle connection errors - escalate
        if (context.Exception is DatabaseConnectionException)
        {
            Console.WriteLine("Pool supervisor escalating to system level");
            return Task.FromResult(SupervisionDirective.Escalate);
        }

        return Task.FromResult(SupervisionDirective.Restart);
    }
}

// Top-level supervisor
[Actor(Name = "SystemSupervisor")]
public class SystemSupervisor : ActorBase
{
    public override async Task<SupervisionDirective> OnChildFailureAsync(
        ChildFailureContext context,
        CancellationToken cancellationToken = default)
    {
        if (context.Exception is DatabaseConnectionException)
        {
            Console.WriteLine("System supervisor handling connection failure");
            
            // System-wide response: restart all database pools
            var children = GetChildren();
            foreach (var child in children.OfType<PoolSupervisor>())
            {
                await child.OnDeactivateAsync();
                await child.OnActivateAsync();
            }

            return SupervisionDirective.Resume;
        }

        return Task.FromResult(SupervisionDirective.Restart);
    }
}
```

## Best Practices

### 1. Design for Failure

**✅ Do:**
- Assume actors will fail
- Design supervision hierarchies upfront
- Test failure scenarios explicitly

**❌ Don't:**
- Add supervision as an afterthought
- Assume actors are always available
- Ignore failure cases in testing

### 2. Choose Directives Carefully

**✅ Do:**
```csharp
public override Task<SupervisionDirective> OnChildFailureAsync(
    ChildFailureContext context,
    CancellationToken cancellationToken = default)
{
    // Clear decision tree
    return context.Exception switch
    {
        TimeoutException => SupervisionDirective.Resume,
        InvalidOperationException => SupervisionDirective.Restart,
        OutOfMemoryException => SupervisionDirective.Stop,
        _ => SupervisionDirective.Escalate
    };
}
```

**❌ Don't:**
```csharp
public override Task<SupervisionDirective> OnChildFailureAsync(
    ChildFailureContext context,
    CancellationToken cancellationToken = default)
{
    // Always restarting is too simplistic
    return Task.FromResult(SupervisionDirective.Restart);
}
```

### 3. Always Log Failures

**✅ Do:**
```csharp
public override Task<SupervisionDirective> OnChildFailureAsync(
    ChildFailureContext context,
    CancellationToken cancellationToken = default)
{
    // Log before deciding
    _logger.LogError(context.Exception, 
        "Child actor {ActorId} failed", 
        context.Child.ActorId);

    return Task.FromResult(SupervisionDirective.Restart);
}
```

**❌ Don't:**
```csharp
public override Task<SupervisionDirective> OnChildFailureAsync(
    ChildFailureContext context,
    CancellationToken cancellationToken = default)
{
    // Silent failures are hard to debug
    return Task.FromResult(SupervisionDirective.Restart);
}
```

### 4. Limit Restart Attempts

**✅ Do:**
```csharp
private readonly Dictionary<string, int> _restartCounts = new();

public override Task<SupervisionDirective> OnChildFailureAsync(
    ChildFailureContext context,
    CancellationToken cancellationToken = default)
{
    _restartCounts.TryGetValue(context.Child.ActorId, out int count);

    if (count >= 5)
    {
        _logger.LogWarning("Actor {ActorId} exceeded restart limit", 
            context.Child.ActorId);
        return Task.FromResult(SupervisionDirective.Stop);
    }

    _restartCounts[context.Child.ActorId] = count + 1;
    return Task.FromResult(SupervisionDirective.Restart);
}
```

**❌ Don't:**
```csharp
public override Task<SupervisionDirective> OnChildFailureAsync(
    ChildFailureContext context,
    CancellationToken cancellationToken = default)
{
    // Infinite restart loop
    return Task.FromResult(SupervisionDirective.Restart);
}
```

### 5. Escalate Unknown Errors

**✅ Do:**
```csharp
public override Task<SupervisionDirective> OnChildFailureAsync(
    ChildFailureContext context,
    CancellationToken cancellationToken = default)
{
    return context.Exception switch
    {
        TimeoutException => SupervisionDirective.Resume,
        InvalidOperationException => SupervisionDirective.Restart,
        // Let parent handle unknown errors
        _ => SupervisionDirective.Escalate
    };
}
```

**❌ Don't:**
```csharp
public override Task<SupervisionDirective> OnChildFailureAsync(
    ChildFailureContext context,
    CancellationToken cancellationToken = default)
{
    // Blindly restarting unknown errors
    return Task.FromResult(SupervisionDirective.Restart);
}
```

### 6. Clean Up Resources

**✅ Do:**
```csharp
public override async Task<SupervisionDirective> OnChildFailureAsync(
    ChildFailureContext context,
    CancellationToken cancellationToken = default)
{
    if (ShouldStop(context.Exception))
    {
        // Clean up before stopping
        await context.Child.OnDeactivateAsync(cancellationToken);
        return SupervisionDirective.Stop;
    }

    return SupervisionDirective.Restart;
}
```

**❌ Don't:**
```csharp
public override Task<SupervisionDirective> OnChildFailureAsync(
    ChildFailureContext context,
    CancellationToken cancellationToken = default)
{
    // Stopping without cleanup may leak resources
    return Task.FromResult(SupervisionDirective.Stop);
}
```

### 7. Test Supervision Logic

```csharp
[Fact]
public async Task Supervisor_Restarts_On_InvalidOperation()
{
    // Arrange
    var factory = new ActorFactory();
    var supervisor = factory.CreateActor<TestSupervisor>("supervisor");
    var worker = await supervisor.SpawnChildAsync<WorkerActor>("worker-1");

    // Act
    var exception = new InvalidOperationException("Test error");
    var context = new ChildFailureContext(worker, exception);
    var directive = await supervisor.OnChildFailureAsync(context);

    // Assert
    Assert.Equal(SupervisionDirective.Restart, directive);
}
```

## Common Patterns

### Worker Pool Pattern

Supervisor manages a pool of identical workers:

```csharp
[Actor(Name = "WorkerPool")]
public class WorkerPoolSupervisor : ActorBase
{
    private readonly int _poolSize;
    private readonly Queue<WorkerActor> _availableWorkers = new();
    private readonly object _lock = new();

    public WorkerPoolSupervisor(string actorId, IActorFactory factory, int poolSize = 10) 
        : base(actorId, factory)
    {
        _poolSize = poolSize;
    }

    public override async Task OnActivateAsync(CancellationToken ct = default)
    {
        // Create worker pool
        for (int i = 0; i < _poolSize; i++)
        {
            var worker = await SpawnChildAsync<WorkerActor>($"worker-{i}");
            _availableWorkers.Enqueue(worker);
        }

        Console.WriteLine($"Worker pool created with {_poolSize} workers");
    }

    public async Task<TResult> ExecuteAsync<TResult>(Func<WorkerActor, Task<TResult>> work)
    {
        WorkerActor? worker = null;

        lock (_lock)
        {
            if (_availableWorkers.Count > 0)
            {
                worker = _availableWorkers.Dequeue();
            }
        }

        if (worker == null)
        {
            throw new InvalidOperationException("No workers available");
        }

        try
        {
            return await work(worker);
        }
        finally
        {
            lock (_lock)
            {
                _availableWorkers.Enqueue(worker);
            }
        }
    }

    public override Task<SupervisionDirective> OnChildFailureAsync(
        ChildFailureContext context,
        CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"Worker {context.Child.ActorId} failed - restarting");
        return Task.FromResult(SupervisionDirective.Restart);
    }
}

// Usage
var pool = factory.CreateActor<WorkerPoolSupervisor>("pool", poolSize: 10);
await pool.OnActivateAsync();

var result = await pool.ExecuteAsync(worker => worker.DoWorkAsync("task"));
```

### Manager/Worker Pattern

Manager coordinates workers and handles their failures:

```csharp
[Actor(Name = "Manager")]
public class ManagerActor : ActorBase
{
    private readonly Queue<string> _taskQueue = new();
    private readonly Dictionary<string, string> _workerAssignments = new();

    public ManagerActor(string actorId, IActorFactory factory) 
        : base(actorId, factory) { }

    public override async Task OnActivateAsync(CancellationToken ct = default)
    {
        // Spawn workers
        for (int i = 0; i < 5; i++)
        {
            await SpawnChildAsync<WorkerActor>($"worker-{i}");
        }
    }

    public async Task SubmitTaskAsync(string taskId)
    {
        _taskQueue.Enqueue(taskId);
        await AssignTasksAsync();
    }

    private async Task AssignTasksAsync()
    {
        var children = GetChildren();
        
        foreach (var child in children.Cast<WorkerActor>())
        {
            if (_taskQueue.Count == 0) break;

            // Check if worker is busy
            if (_workerAssignments.ContainsValue(child.ActorId))
                continue;

            var taskId = _taskQueue.Dequeue();
            _workerAssignments[taskId] = child.ActorId;

            try
            {
                await child.DoWorkAsync(taskId);
                _workerAssignments.Remove(taskId);
            }
            catch (Exception ex)
            {
                // Re-queue failed task
                _taskQueue.Enqueue(taskId);
                _workerAssignments.Remove(taskId);
            }
        }
    }

    public override async Task<SupervisionDirective> OnChildFailureAsync(
        ChildFailureContext context,
        CancellationToken cancellationToken = default)
    {
        // Find tasks assigned to failed worker
        var failedTasks = _workerAssignments
            .Where(kvp => kvp.Value == context.Child.ActorId)
            .Select(kvp => kvp.Key)
            .ToList();

        // Re-queue failed tasks
        foreach (var taskId in failedTasks)
        {
            _taskQueue.Enqueue(taskId);
            _workerAssignments.Remove(taskId);
        }

        Console.WriteLine($"Worker {context.Child.ActorId} failed - {failedTasks.Count} tasks re-queued");

        // Restart worker
        return SupervisionDirective.Restart;
    }
}
```

### Saga Pattern (Distributed Transactions)

Coordinate multi-step transactions with compensation:

```csharp
[Actor(Name = "SagaCoordinator")]
public class SagaCoordinator : ActorBase
{
    private readonly List<(IActor Actor, string Action)> _completedSteps = new();

    public SagaCoordinator(string actorId, IActorFactory factory) 
        : base(actorId, factory) { }

    public override async Task OnActivateAsync(CancellationToken ct = default)
    {
        await SpawnChildAsync<PaymentActor>("payment");
        await SpawnChildAsync<InventoryActor>("inventory");
        await SpawnChildAsync<ShippingActor>("shipping");
    }

    public async Task<bool> ExecuteSagaAsync(Order order)
    {
        var children = GetChildren().ToList();

        try
        {
            // Step 1: Reserve payment
            var payment = children.OfType<PaymentActor>().First();
            await payment.ReserveAsync(order.Amount);
            _completedSteps.Add((payment, "Reserve"));

            // Step 2: Reserve inventory
            var inventory = children.OfType<InventoryActor>().First();
            await inventory.ReserveAsync(order.Items);
            _completedSteps.Add((inventory, "Reserve"));

            // Step 3: Schedule shipping
            var shipping = children.OfType<ShippingActor>().First();
            await shipping.ScheduleAsync(order.Address);
            _completedSteps.Add((shipping, "Schedule"));

            Console.WriteLine("Saga completed successfully");
            _completedSteps.Clear();
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Saga failed: {ex.Message} - compensating");
            await CompensateAsync();
            return false;
        }
    }

    private async Task CompensateAsync()
    {
        // Roll back in reverse order
        _completedSteps.Reverse();

        foreach (var (actor, action) in _completedSteps)
        {
            try
            {
                if (actor is PaymentActor payment && action == "Reserve")
                    await payment.CancelAsync();
                else if (actor is InventoryActor inventory && action == "Reserve")
                    await inventory.ReleaseAsync();
                else if (actor is ShippingActor shipping && action == "Schedule")
                    await shipping.CancelAsync();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Compensation failed for {actor.ActorId}: {ex.Message}");
            }
        }

        _completedSteps.Clear();
    }

    public override async Task<SupervisionDirective> OnChildFailureAsync(
        ChildFailureContext context,
        CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"Saga step failed: {context.Child.ActorId}");
        
        // Compensate for partial completion
        await CompensateAsync();

        // Restart the failed actor for next saga
        return SupervisionDirective.Restart;
    }
}
```

## Troubleshooting

### Problem: Infinite Restart Loop

**Symptom:** Actor keeps failing and restarting repeatedly.

**Cause:** Bug in actor code or no restart limit.

**Solution:** Add restart limiting:

```csharp
private readonly Dictionary<string, int> _restartCounts = new();
private readonly Dictionary<string, DateTime> _firstFailures = new();

public override Task<SupervisionDirective> OnChildFailureAsync(
    ChildFailureContext context,
    CancellationToken cancellationToken = default)
{
    var actorId = context.Child.ActorId;
    var now = DateTime.UtcNow;

    // Track restarts
    if (!_firstFailures.ContainsKey(actorId))
    {
        _firstFailures[actorId] = now;
        _restartCounts[actorId] = 0;
    }

    // Reset counter after 5 minutes
    if ((now - _firstFailures[actorId]) > TimeSpan.FromMinutes(5))
    {
        _firstFailures[actorId] = now;
        _restartCounts[actorId] = 0;
    }

    _restartCounts[actorId]++;

    // Stop after 5 restarts in 5 minutes
    if (_restartCounts[actorId] >= 5)
    {
        Console.WriteLine($"Actor {actorId} exceeded restart limit - stopping");
        return Task.FromResult(SupervisionDirective.Stop);
    }

    return Task.FromResult(SupervisionDirective.Restart);
}
```

### Problem: Can't Spawn Children

**Symptom:** `SpawnChildAsync` throws "Cannot spawn child actors without an IActorFactory".

**Cause:** Actor was created without passing an `IActorFactory`.

**Solution:**

```csharp
// ❌ Wrong
var actor = new SupervisorActor("supervisor-1");
await actor.SpawnChildAsync<WorkerActor>("worker-1"); // Throws!

// ✅ Correct
var factory = new ActorFactory();
var actor = factory.CreateActor<SupervisorActor>("supervisor-1");
await actor.SpawnChildAsync<WorkerActor>("worker-1"); // Works!
```

### Problem: Duplicate Child ID

**Symptom:** `SpawnChildAsync` throws "A child actor with ID 'X' already exists".

**Cause:** Attempting to spawn a child with an ID that's already in use.

**Solution:**

```csharp
// Track spawned children
private readonly HashSet<string> _spawnedIds = new();

public async Task<WorkerActor> GetOrSpawnWorkerAsync(string workerId)
{
    if (_spawnedIds.Contains(workerId))
    {
        // Return existing child
        return GetChildren()
            .OfType<WorkerActor>()
            .First(w => w.ActorId == workerId);
    }

    // Spawn new child
    var worker = await SpawnChildAsync<WorkerActor>(workerId);
    _spawnedIds.Add(workerId);
    return worker;
}
```

### Problem: State Lost After Restart

**Symptom:** Actor state disappears after restart.

**Cause:** `Restart` directive creates a new instance with fresh state.

**Solution:** Use persistence to save state before deactivation:

```csharp
[Actor(Name = "PersistentWorker")]
public class PersistentWorkerActor : ActorBase, IStateful<WorkerState>
{
    private WorkerState _state = new();

    public PersistentWorkerActor(string actorId, IActorFactory factory) 
        : base(actorId, factory) { }

    public override async Task OnActivateAsync(CancellationToken ct = default)
    {
        // Load state on activation
        _state = await LoadStateAsync();
    }

    public override async Task OnDeactivateAsync(CancellationToken ct = default)
    {
        // Save state before deactivation (including before restart)
        await SaveStateAsync(_state);
    }
}
```

### Problem: Escalation Reaches Root Without Handler

**Symptom:** Exception propagates to root supervisor with no handler.

**Cause:** No supervisor in the chain handles the exception type.

**Solution:** Add a catch-all handler at the root:

```csharp
[Actor(Name = "RootSupervisor")]
public class RootSupervisor : ActorBase
{
    public override Task<SupervisionDirective> OnChildFailureAsync(
        ChildFailureContext context,
        CancellationToken cancellationToken = default)
    {
        // Root must not escalate (no parent above)
        Console.WriteLine($"CRITICAL: Unhandled failure reached root from {context.Child.ActorId}");
        Console.WriteLine($"Exception: {context.Exception}");

        // Decide what to do
        return context.Exception switch
        {
            OutOfMemoryException => SupervisionDirective.Stop, // System-wide issue
            _ => SupervisionDirective.Restart                    // Try to recover
        };
    }
}
```

### Problem: Debugging Supervision

**Enable detailed logging:**

```csharp
public override Task<SupervisionDirective> OnChildFailureAsync(
    ChildFailureContext context,
    CancellationToken cancellationToken = default)
{
    // Log everything for debugging
    Console.WriteLine($@"
========== CHILD FAILURE ==========
Actor ID: {context.Child.ActorId}
Actor Type: {context.Child.GetType().Name}
Exception Type: {context.Exception.GetType().Name}
Exception Message: {context.Exception.Message}
Stack Trace: 
{context.Exception.StackTrace}
===================================
");

    var directive = context.Exception switch
    {
        TimeoutException => SupervisionDirective.Resume,
        InvalidOperationException => SupervisionDirective.Restart,
        _ => SupervisionDirective.Escalate
    };

    Console.WriteLine($"Supervision directive: {directive}");
    return Task.FromResult(directive);
}
```

## Summary

Supervision in Quark provides:

- ✅ **Fault Tolerance** - Failures are contained and handled automatically
- ✅ **Separation of Concerns** - Business logic separate from error handling
- ✅ **Hierarchical Recovery** - Errors handled at the appropriate level
- ✅ **Flexible Strategies** - OneForOne, AllForOne, RestForOne patterns
- ✅ **Customizable Directives** - Resume, Restart, Stop, Escalate based on context

**Key Takeaways:**

1. Every actor can supervise children via `SpawnChildAsync`
2. Override `OnChildFailureAsync` to customize supervision
3. Choose directives based on exception type and impact
4. Always limit restart attempts to prevent infinite loops
5. Use hierarchies to handle failures at the right level
6. Test your supervision logic explicitly

## Next Steps

- **[Persistence](Persistence)** - Add durable state to supervised actors
- **[Clustering](Clustering)** - Supervise actors across multiple silos
- **[Timers and Reminders](Timers-and-Reminders)** - Schedule work in supervised actors
- **[Examples](Examples)** - See complete supervision examples

## Additional Resources

- **[Source Code](https://github.com/IgorWestermann/Quark/tree/main/src/Quark.Abstractions)** - ISupervisor.cs, SupervisionDirective.cs
- **[Examples](https://github.com/IgorWestermann/Quark/tree/main/examples/Quark.Examples.Supervision)** - Full working examples
- **[Actor Model](Actor-Model)** - Understanding actor lifecycle

---

**Related Documentation:**
- [Actor Model](Actor-Model) - Core concepts
- [Getting Started](Getting-Started) - First steps  
- [API Reference](API-Reference) - Complete API docs
- [Migration from Akka.NET](Migration-from-Akka-NET) - Supervision differences

**Found an issue?** [Report it on GitHub](https://github.com/IgorWestermann/Quark/issues) or [contribute to the docs](Contributing).
