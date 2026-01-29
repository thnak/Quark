# Supervision in Quark

Supervision is Quark's fault tolerance mechanism, inspired by Akka and Erlang/OTP. Supervisors create and monitor child actors, deciding what to do when they fail.

## Core Concepts

### What is Supervision?

Supervision is a pattern where:
1. **Parent actors** create and monitor **child actors**
2. When a child fails, the **parent decides** what to do
3. Failures are **contained** and don't crash the entire system
4. **Error handling is separated** from business logic

### The Supervision Hierarchy

```
          RootSupervisor
               │
       ┌───────┼───────┐
       ↓       ↓       ↓
   Worker1 Worker2  ChildSupervisor
                         │
                    ┌────┼────┐
                    ↓    ↓    ↓
                  Sub1 Sub2 Sub3
```

Each supervisor manages its children's lifecycle and failures.

## Creating Supervised Actors

### Basic Supervisor

```csharp
[Actor]
public class SupervisorActor : ActorBase, ISupervisor
{
    public SupervisorActor(
        string actorId, 
        IActorFactory? actorFactory = null) 
        : base(actorId, actorFactory)
    {
    }

    // Called when a child actor fails
    public override Task<SupervisionDirective> OnChildFailureAsync(
        ChildFailureContext context,
        CancellationToken cancellationToken = default)
    {
        // Decide what to do based on the exception
        return context.Exception switch
        {
            TimeoutException => Task.FromResult(SupervisionDirective.Resume),
            InvalidOperationException => Task.FromResult(SupervisionDirective.Restart),
            OutOfMemoryException => Task.FromResult(SupervisionDirective.Stop),
            _ => Task.FromResult(SupervisionDirective.Escalate)
        };
    }
}
```

### Spawning Children

```csharp
var factory = new ActorFactory();
var supervisor = factory.CreateActor<SupervisorActor>("supervisor-1");

// Spawn child actors
var worker1 = await supervisor.SpawnChildAsync<WorkerActor>("worker-1");
var worker2 = await supervisor.SpawnChildAsync<WorkerActor>("worker-2");
var worker3 = await supervisor.SpawnChildAsync<WorkerActor>("worker-3");

// Get all children
var children = supervisor.GetChildren();
Console.WriteLine($"Supervisor has {children.Count} children");
```

## Supervision Directives

When a child fails, the supervisor chooses one of four directives:

### 1. Resume

**Continue processing** with the current state.

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
- Transient errors (timeouts, rate limits)
- Errors that don't corrupt state
- Errors that can be safely ignored

**Effect:**
- ✅ Actor keeps its state
- ✅ Actor continues processing
- ✅ No downtime

### 2. Restart

**Stop and restart** the child actor with fresh state.

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
- State corruption
- Recoverable errors
- "Turn it off and on again" scenarios

**Effect:**
- ❌ Actor loses its state
- ✅ Actor gets fresh start
- ⚠️ Brief downtime during restart

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
- Unrecoverable errors
- Resource exhaustion
- Actor no longer needed

**Effect:**
- ❌ Actor is removed
- ❌ Cannot be restarted
- ⚠️ Parent must handle actor removal

### 4. Escalate

**Pass the error up** to the parent's supervisor.

```csharp
public override Task<SupervisionDirective> OnChildFailureAsync(
    ChildFailureContext context,
    CancellationToken cancellationToken = default)
{
    if (context.Exception is DatabaseException)
    {
        // Can't handle at this level - escalate
        return Task.FromResult(SupervisionDirective.Escalate);
    }
    // ...
}
```

**When to use:**
- Errors the supervisor can't handle
- System-wide issues (database down, network failure)
- Errors requiring higher-level coordination

**Effect:**
- ⬆️ Error passed to parent's supervisor
- 🔁 Parent decides what to do
- ⚠️ Can cascade up the hierarchy

## Child Failure Context

The `ChildFailureContext` provides details about the failure:

```csharp
public class ChildFailureContext
{
    public IActor ChildActor { get; }        // The failed actor
    public Exception Exception { get; }      // What went wrong
    public string ActorId { get; }          // Child's ID
    public DateTime FailureTime { get; }     // When it failed
}
```

### Using the Context

```csharp
public override Task<SupervisionDirective> OnChildFailureAsync(
    ChildFailureContext context,
    CancellationToken cancellationToken = default)
{
    // Log the failure
    Console.WriteLine($"Actor {context.ActorId} failed at {context.FailureTime}");
    Console.WriteLine($"Exception: {context.Exception.Message}");

    // Decide based on actor type
    if (context.ChildActor is CriticalWorkerActor)
    {
        return Task.FromResult(SupervisionDirective.Restart);
    }

    // Decide based on exception type
    return context.Exception switch
    {
        ArgumentException => Task.FromResult(SupervisionDirective.Resume),
        InvalidOperationException => Task.FromResult(SupervisionDirective.Restart),
        _ => Task.FromResult(SupervisionDirective.Escalate)
    };
}
```

## Supervision Strategies

### One-For-One (Default)

Each child is supervised independently. One child's failure doesn't affect siblings.

```csharp
[Actor]
public class IndependentSupervisor : ActorBase, ISupervisor
{
    // Each worker handles its own orders independently
    public async Task InitializeAsync()
    {
        await SpawnChildAsync<OrderWorkerActor>("worker-1");
        await SpawnChildAsync<OrderWorkerActor>("worker-2");
        await SpawnChildAsync<OrderWorkerActor>("worker-3");
    }

    public override Task<SupervisionDirective> OnChildFailureAsync(
        ChildFailureContext context,
        CancellationToken cancellationToken = default)
    {
        // Only restart the failed worker
        return Task.FromResult(SupervisionDirective.Restart);
    }
}
```

**When to use:**
- Workers are independent
- Failures are isolated
- High availability needed

### All-For-One

When one child fails, restart all children (implement manually).

```csharp
[Actor]
public class CoordinatedSupervisor : ActorBase, ISupervisor
{
    public override async Task<SupervisionDirective> OnChildFailureAsync(
        ChildFailureContext context,
        CancellationToken cancellationToken = default)
    {
        // Get all children
        var children = GetChildren();

        // Restart all children when one fails
        foreach (var child in children)
        {
            await child.OnDeactivateAsync(cancellationToken);
            await child.OnActivateAsync(cancellationToken);
        }

        return SupervisionDirective.Resume;
    }
}
```

**When to use:**
- Children are interdependent
- Coordinated state required
- Consistency is critical

### Rest-For-One

Restart the failed child and all children spawned after it (implement manually).

```csharp
[Actor]
public class SequentialSupervisor : ActorBase, ISupervisor
{
    private readonly List<(string Id, IActor Actor)> _childrenInOrder = new();

    public override async Task<IActor> SpawnChildAsync<T>(
        string childId,
        CancellationToken cancellationToken = default)
    {
        var child = await base.SpawnChildAsync<T>(childId, cancellationToken);
        _childrenInOrder.Add((childId, child));
        return child;
    }

    public override async Task<SupervisionDirective> OnChildFailureAsync(
        ChildFailureContext context,
        CancellationToken cancellationToken = default)
    {
        // Find the failed child's position
        int failedIndex = _childrenInOrder.FindIndex(c => c.Id == context.ActorId);

        // Restart this child and all subsequent children
        for (int i = failedIndex; i < _childrenInOrder.Count; i++)
        {
            var child = _childrenInOrder[i].Actor;
            await child.OnDeactivateAsync(cancellationToken);
            await child.OnActivateAsync(cancellationToken);
        }

        return SupervisionDirective.Resume;
    }
}
```

**When to use:**
- Pipeline processing
- Sequential dependencies
- Order matters

## Advanced Patterns

### Exponential Backoff

Implement progressive delays between restart attempts:

```csharp
[Actor]
public class SmartSupervisor : ActorBase, ISupervisor
{
    private readonly Dictionary<string, int> _restartCounts = new();
    private readonly Dictionary<string, DateTime> _lastRestarts = new();

    public override async Task<SupervisionDirective> OnChildFailureAsync(
        ChildFailureContext context,
        CancellationToken cancellationToken = default)
    {
        var actorId = context.ActorId;

        // Track restart count
        _restartCounts.TryGetValue(actorId, out int count);
        _restartCounts[actorId] = count + 1;

        // Too many restarts - stop it
        if (count >= 5)
        {
            Console.WriteLine($"Actor {actorId} failed {count} times - stopping");
            return SupervisionDirective.Stop;
        }

        // Calculate backoff delay
        var delay = TimeSpan.FromSeconds(Math.Pow(2, count)); // 1s, 2s, 4s, 8s, 16s

        // Check if we should wait
        if (_lastRestarts.TryGetValue(actorId, out var lastRestart))
        {
            var timeSinceRestart = DateTime.UtcNow - lastRestart;
            if (timeSinceRestart < delay)
            {
                var remainingWait = delay - timeSinceRestart;
                await Task.Delay(remainingWait, cancellationToken);
            }
        }

        _lastRestarts[actorId] = DateTime.UtcNow;
        return SupervisionDirective.Restart;
    }
}
```

### Circuit Breaker Pattern

Stop trying after repeated failures:

```csharp
[Actor]
public class CircuitBreakerSupervisor : ActorBase, ISupervisor
{
    private readonly Dictionary<string, CircuitState> _circuits = new();

    private class CircuitState
    {
        public int FailureCount { get; set; }
        public DateTime LastFailure { get; set; }
        public bool IsOpen { get; set; }
    }

    public override Task<SupervisionDirective> OnChildFailureAsync(
        ChildFailureContext context,
        CancellationToken cancellationToken = default)
    {
        var actorId = context.ActorId;

        if (!_circuits.ContainsKey(actorId))
        {
            _circuits[actorId] = new CircuitState();
        }

        var circuit = _circuits[actorId];

        // Circuit is open - don't restart
        if (circuit.IsOpen)
        {
            return Task.FromResult(SupervisionDirective.Stop);
        }

        // Increment failure count
        circuit.FailureCount++;
        circuit.LastFailure = DateTime.UtcNow;

        // Open circuit after 3 failures in 1 minute
        if (circuit.FailureCount >= 3 
            && (DateTime.UtcNow - circuit.LastFailure) < TimeSpan.FromMinutes(1))
        {
            circuit.IsOpen = true;
            Console.WriteLine($"Circuit breaker opened for {actorId}");
            return Task.FromResult(SupervisionDirective.Stop);
        }

        return Task.FromResult(SupervisionDirective.Restart);
    }
}
```

### Health Monitoring

Track child health over time:

```csharp
[Actor]
public class HealthMonitoringSupervisor : ActorBase, ISupervisor
{
    private readonly Dictionary<string, HealthStats> _healthStats = new();

    private class HealthStats
    {
        public int TotalFailures { get; set; }
        public int ConsecutiveFailures { get; set; }
        public DateTime? LastSuccess { get; set; }
        public DateTime? LastFailure { get; set; }
    }

    public override Task<SupervisionDirective> OnChildFailureAsync(
        ChildFailureContext context,
        CancellationToken cancellationToken = default)
    {
        var actorId = context.ActorId;

        if (!_healthStats.ContainsKey(actorId))
        {
            _healthStats[actorId] = new HealthStats();
        }

        var stats = _healthStats[actorId];
        stats.TotalFailures++;
        stats.ConsecutiveFailures++;
        stats.LastFailure = DateTime.UtcNow;

        // Log health metrics
        Console.WriteLine($"Actor {actorId} health:");
        Console.WriteLine($"  Total failures: {stats.TotalFailures}");
        Console.WriteLine($"  Consecutive failures: {stats.ConsecutiveFailures}");
        Console.WriteLine($"  Last success: {stats.LastSuccess?.ToString() ?? "Never"}");

        // Decide based on health
        if (stats.ConsecutiveFailures >= 3)
        {
            return Task.FromResult(SupervisionDirective.Stop);
        }

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
}
```

## Best Practices

### 1. Choose the Right Directive

```csharp
// ✅ Good: Clear decision tree
public override Task<SupervisionDirective> OnChildFailureAsync(
    ChildFailureContext context,
    CancellationToken cancellationToken = default)
{
    return context.Exception switch
    {
        TimeoutException => Task.FromResult(SupervisionDirective.Resume),
        InvalidOperationException => Task.FromResult(SupervisionDirective.Restart),
        OutOfMemoryException => Task.FromResult(SupervisionDirective.Stop),
        _ => Task.FromResult(SupervisionDirective.Escalate)
    };
}

// ❌ Avoid: Always restarting
public override Task<SupervisionDirective> OnChildFailureAsync(
    ChildFailureContext context,
    CancellationToken cancellationToken = default)
{
    return Task.FromResult(SupervisionDirective.Restart); // Too simplistic
}
```

### 2. Log Failures

Always log child failures for debugging:

```csharp
public override Task<SupervisionDirective> OnChildFailureAsync(
    ChildFailureContext context,
    CancellationToken cancellationToken = default)
{
    // Log the failure
    _logger.LogError(context.Exception, 
        "Child actor {ActorId} failed", 
        context.ActorId);

    // Then decide what to do
    return Task.FromResult(SupervisionDirective.Restart);
}
```

### 3. Limit Restart Attempts

Prevent infinite restart loops:

```csharp
private readonly Dictionary<string, int> _restartCounts = new();

public override Task<SupervisionDirective> OnChildFailureAsync(
    ChildFailureContext context,
    CancellationToken cancellationToken = default)
{
    _restartCounts.TryGetValue(context.ActorId, out int count);

    if (count >= 5)
    {
        return Task.FromResult(SupervisionDirective.Stop);
    }

    _restartCounts[context.ActorId] = count + 1;
    return Task.FromResult(SupervisionDirective.Restart);
}
```

### 4. Escalate Unknown Errors

Don't try to handle errors you don't understand:

```csharp
public override Task<SupervisionDirective> OnChildFailureAsync(
    ChildFailureContext context,
    CancellationToken cancellationToken = default)
{
    return context.Exception switch
    {
        TimeoutException => Task.FromResult(SupervisionDirective.Resume),
        InvalidOperationException => Task.FromResult(SupervisionDirective.Restart),
        // Let parent handle unknown errors
        _ => Task.FromResult(SupervisionDirective.Escalate)
    };
}
```

### 5. Clean Up on Stop

When stopping a child, clean up its resources:

```csharp
public override async Task<SupervisionDirective> OnChildFailureAsync(
    ChildFailureContext context,
    CancellationToken cancellationToken = default)
{
    if (ShouldStop(context.Exception))
    {
        // Clean up before stopping
        await context.ChildActor.OnDeactivateAsync(cancellationToken);
        return SupervisionDirective.Stop;
    }

    return SupervisionDirective.Restart;
}
```

## Complete Example

Here's a full example demonstrating supervision:

```csharp
// Worker that might fail
[Actor]
public class WorkerActor : ActorBase
{
    private readonly Random _random = new();

    public WorkerActor(string actorId, IActorFactory? actorFactory = null) 
        : base(actorId, actorFactory) { }

    public async Task ProcessTaskAsync()
    {
        Console.WriteLine($"[{ActorId}] Processing task...");

        // Simulate random failures
        var outcome = _random.Next(100);

        if (outcome < 10)
        {
            throw new TimeoutException("Request timed out");
        }
        else if (outcome < 15)
        {
            throw new InvalidOperationException("State corrupted");
        }
        else if (outcome < 17)
        {
            throw new OutOfMemoryException("Out of memory");
        }

        // Success
        await Task.Delay(100);
        Console.WriteLine($"[{ActorId}] Task completed successfully");
    }
}

// Supervisor that handles failures
[Actor]
public class WorkerSupervisor : ActorBase, ISupervisor
{
    private readonly Dictionary<string, int> _restartCounts = new();

    public WorkerSupervisor(string actorId, IActorFactory? actorFactory = null) 
        : base(actorId, actorFactory) { }

    public override async Task OnActivateAsync(CancellationToken cancellationToken = default)
    {
        // Spawn 3 workers
        for (int i = 1; i <= 3; i++)
        {
            await SpawnChildAsync<WorkerActor>($"worker-{i}");
        }

        Console.WriteLine($"Supervisor spawned {GetChildren().Count} workers");
    }

    public override Task<SupervisionDirective> OnChildFailureAsync(
        ChildFailureContext context,
        CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"Worker {context.ActorId} failed: {context.Exception.Message}");

        // Track restarts
        _restartCounts.TryGetValue(context.ActorId, out int count);
        _restartCounts[context.ActorId] = count + 1;

        // Stop after 3 restarts
        if (count >= 3)
        {
            Console.WriteLine($"Worker {context.ActorId} failed too many times - stopping");
            return Task.FromResult(SupervisionDirective.Stop);
        }

        // Decide based on exception type
        var directive = context.Exception switch
        {
            TimeoutException => SupervisionDirective.Resume,
            InvalidOperationException => SupervisionDirective.Restart,
            OutOfMemoryException => SupervisionDirective.Stop,
            _ => SupervisionDirective.Escalate
        };

        Console.WriteLine($"Directive: {directive}");
        return Task.FromResult(directive);
    }
}

// Usage
var factory = new ActorFactory();
var supervisor = factory.CreateActor<WorkerSupervisor>("supervisor-1");
await supervisor.OnActivateAsync();

// Process tasks with workers
var workers = supervisor.GetChildren();
foreach (var worker in workers.Cast<WorkerActor>())
{
    for (int i = 0; i < 10; i++)
    {
        try
        {
            await worker.ProcessTaskAsync();
        }
        catch (Exception ex)
        {
            // Supervisor handles the failure
            Console.WriteLine($"Task failed, supervisor will handle it");
        }
    }
}
```

## Next Steps

- **[Persistence](Persistence)** - Add durable state to supervised actors
- **[Clustering](Clustering)** - Supervise actors across multiple machines
- **[Examples](Examples)** - See the complete supervision example

---

**Related**: [Actor Model](Actor-Model) | [Getting Started](Getting-Started) | [API Reference](API-Reference)
