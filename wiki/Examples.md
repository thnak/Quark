# Examples

Practical code examples demonstrating Quark's key features and patterns.

## Table of Contents

- [Basic Examples](#basic-examples)
  - [Counter Actor](#counter-actor)
  - [Stateful Actor](#stateful-actor)
  - [Actor Lifecycle](#actor-lifecycle)
- [Supervision Examples](#supervision-examples)
  - [Simple Supervision](#simple-supervision)
  - [Custom Supervision Strategy](#custom-supervision-strategy)
  - [Supervision Hierarchy](#supervision-hierarchy)
- [Persistence Examples](#persistence-examples)
  - [Redis State Storage](#redis-state-storage)
  - [Optimistic Concurrency](#optimistic-concurrency)
- [Streaming Examples](#streaming-examples)
  - [Implicit Subscriptions](#implicit-subscriptions)
  - [Explicit Pub/Sub](#explicit-pubsub)
  - [Multiple Subscribers](#multiple-subscribers)
- [Complete Working Examples](#complete-working-examples)
  - [Basic Example](#basic-example-from-repository)
  - [Supervision Example](#supervision-example-from-repository)
  - [Streaming Example](#streaming-example-from-repository)

---

## Basic Examples

### Counter Actor

A simple actor that maintains an integer counter.

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

    public override Task OnActivateAsync(CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"Counter {ActorId} activated with count: {_count}");
        return Task.CompletedTask;
    }

    public override Task OnDeactivateAsync(CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"Counter {ActorId} deactivated with final count: {_count}");
        return Task.CompletedTask;
    }

    public void Increment()
    {
        _count++;
        Console.WriteLine($"Counter {ActorId} incremented to: {_count}");
    }

    public void Decrement()
    {
        _count--;
        Console.WriteLine($"Counter {ActorId} decremented to: {_count}");
    }

    public int GetValue()
    {
        return _count;
    }

    public void Reset()
    {
        _count = 0;
        Console.WriteLine($"Counter {ActorId} reset to: 0");
    }
}
```

**Usage:**

```csharp
var factory = new ActorFactory();
var counter = factory.CreateActor<CounterActor>("counter-1");

await counter.OnActivateAsync();

counter.Increment();  // Output: Counter counter-1 incremented to: 1
counter.Increment();  // Output: Counter counter-1 incremented to: 2
counter.Increment();  // Output: Counter counter-1 incremented to: 3

var value = counter.GetValue();
Console.WriteLine($"Final value: {value}"); // Output: Final value: 3

await counter.OnDeactivateAsync();
```

---

### Stateful Actor

An actor that persists its state to storage.

```csharp
using Quark.Abstractions;
using Quark.Abstractions.Persistence;
using Quark.Core.Actors;

// State class
public class UserState
{
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public int Points { get; set; }
    public DateTime LastLogin { get; set; }
}

// Stateful actor
[Actor(Name = "User")]
public class UserActor : StatefulActorBase<UserState>
{
    public UserActor(string actorId, IStateStorageProvider storageProvider)
        : base(actorId, storageProvider)
    {
        State = new UserState();
    }

    public override async Task OnActivateAsync(CancellationToken cancellationToken = default)
    {
        // Load existing state or use default
        await LoadStateAsync(cancellationToken);
        
        State.LastLogin = DateTime.UtcNow;
        await SaveStateAsync(cancellationToken);

        Console.WriteLine($"User {State.Name} ({ActorId}) logged in");
    }

    public override async Task OnDeactivateAsync(CancellationToken cancellationToken = default)
    {
        // Ensure state is saved before deactivation
        await SaveStateAsync(cancellationToken);
        Console.WriteLine($"User {State.Name} ({ActorId}) logged out");
    }

    public async Task UpdateProfileAsync(string name, string email)
    {
        State.Name = name;
        State.Email = email;
        await SaveStateAsync();
        
        Console.WriteLine($"Profile updated for {ActorId}");
    }

    public void AddPoints(int points)
    {
        State.Points += points;
        Console.WriteLine($"User {State.Name} earned {points} points. Total: {State.Points}");
    }

    public UserState GetProfile()
    {
        return State;
    }
}
```

**Usage:**

```csharp
using Quark.Storage.Redis;
using StackExchange.Redis;

// Setup Redis storage
var redis = await ConnectionMultiplexer.ConnectAsync("localhost:6379");
var storageProvider = new RedisStateStorageProvider(redis);

var factory = new ActorFactory();
var user = factory.CreateActor<UserActor>("user-123");

// First activation - creates new state
await user.OnActivateAsync();
await user.UpdateProfileAsync("Alice", "alice@example.com");
user.AddPoints(100);
await user.OnDeactivateAsync();

// Second activation - loads existing state
var user2 = factory.CreateActor<UserActor>("user-123");
await user2.OnActivateAsync();
var profile = user2.GetProfile();
Console.WriteLine($"Name: {profile.Name}, Points: {profile.Points}");
// Output: Name: Alice, Points: 100
```

---

### Actor Lifecycle

Demonstrates the complete actor lifecycle with initialization and cleanup.

```csharp
[Actor]
public class ResourceActor : ActorBase
{
    private HttpClient? _httpClient;
    private Timer? _healthCheckTimer;

    public ResourceActor(string actorId) : base(actorId) { }

    public override async Task OnActivateAsync(CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"{ActorId}: Initializing resources...");

        // Initialize HTTP client
        _httpClient = new HttpClient
        {
            BaseAddress = new Uri("https://api.example.com"),
            Timeout = TimeSpan.FromSeconds(30)
        };

        // Start health check timer
        _healthCheckTimer = new Timer(
            callback: _ => PerformHealthCheck(),
            state: null,
            dueTime: TimeSpan.FromSeconds(10),
            period: TimeSpan.FromSeconds(30)
        );

        // Perform initial setup
        await InitializeAsync(cancellationToken);

        Console.WriteLine($"{ActorId}: Activation complete");
    }

    public override async Task OnDeactivateAsync(CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"{ActorId}: Cleaning up resources...");

        // Stop timer
        _healthCheckTimer?.Dispose();
        _healthCheckTimer = null;

        // Dispose HTTP client
        _httpClient?.Dispose();
        _httpClient = null;

        // Perform final cleanup
        await CleanupAsync(cancellationToken);

        Console.WriteLine($"{ActorId}: Deactivation complete");
    }

    private async Task InitializeAsync(CancellationToken ct)
    {
        // Connect to external services, load configuration, etc.
        await Task.Delay(100, ct);
    }

    private async Task CleanupAsync(CancellationToken ct)
    {
        // Disconnect from services, save final state, etc.
        await Task.Delay(100, ct);
    }

    private void PerformHealthCheck()
    {
        Console.WriteLine($"{ActorId}: Health check performed");
    }

    public async Task<string> FetchDataAsync()
    {
        if (_httpClient == null)
            throw new InvalidOperationException("Actor not activated");

        var response = await _httpClient.GetStringAsync("/data");
        return response;
    }
}
```

---

## Supervision Examples

### Simple Supervision

Basic parent-child relationship with default supervision.

```csharp
[Actor]
public class WorkerActor : ActorBase
{
    public WorkerActor(string actorId) : base(actorId) { }

    public void DoWork(int value)
    {
        if (value < 0)
            throw new ArgumentException("Value must be non-negative");

        Console.WriteLine($"{ActorId}: Processing value {value}");
    }
}

[Actor]
public class SupervisorActor : ActorBase, ISupervisor
{
    public SupervisorActor(string actorId, IActorFactory? actorFactory = null)
        : base(actorId, actorFactory) { }

    public override async Task OnActivateAsync(CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"{ActorId}: Supervisor activated");

        // Spawn child actors
        var worker1 = await SpawnChildAsync<WorkerActor>("worker-1");
        var worker2 = await SpawnChildAsync<WorkerActor>("worker-2");

        Console.WriteLine($"{ActorId}: Spawned {GetChildren().Count} workers");
    }

    public override Task<SupervisionDirective> OnChildFailureAsync(
        ChildFailureContext context,
        CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"{ActorId}: Child {context.Child.ActorId} failed");
        Console.WriteLine($"  Exception: {context.Exception.Message}");

        // Default strategy: Restart the child
        return Task.FromResult(SupervisionDirective.Restart);
    }
}
```

**Usage:**

```csharp
var factory = new ActorFactory();
var supervisor = factory.CreateActor<SupervisorActor>("supervisor-1");

await supervisor.OnActivateAsync();

var children = supervisor.GetChildren();
var worker = (WorkerActor)children.First();

// This will work
worker.DoWork(10);

// This will trigger supervision (ArgumentException)
try
{
    worker.DoWork(-5);
}
catch
{
    // Supervisor handles the failure
}
```

---

### Custom Supervision Strategy

Implements different strategies based on exception type.

```csharp
[Actor]
public class ResilientSupervisorActor : ActorBase, ISupervisor
{
    public ResilientSupervisorActor(string actorId, IActorFactory? actorFactory = null)
        : base(actorId, actorFactory) { }

    public override async Task<SupervisionDirective> OnChildFailureAsync(
        ChildFailureContext context,
        CancellationToken cancellationToken = default)
    {
        var childId = context.Child.ActorId;
        var exception = context.Exception;

        Console.WriteLine($"Child {childId} failed with {exception.GetType().Name}");

        // Strategy based on exception type
        return exception switch
        {
            // Transient errors - resume without restart
            TimeoutException => HandleTransientError(context),
            HttpRequestException => HandleTransientError(context),

            // Validation errors - restart with clean state
            ArgumentException => HandleValidationError(context),
            InvalidOperationException => HandleValidationError(context),

            // Critical errors - stop child permanently
            OutOfMemoryException => HandleCriticalError(context),
            StackOverflowException => HandleCriticalError(context),

            // Unknown errors - escalate to parent
            _ => HandleUnknownError(context)
        };
    }

    private SupervisionDirective HandleTransientError(ChildFailureContext context)
    {
        Console.WriteLine($"  → Transient error, resuming {context.Child.ActorId}");
        return SupervisionDirective.Resume;
    }

    private SupervisionDirective HandleValidationError(ChildFailureContext context)
    {
        Console.WriteLine($"  → Validation error, restarting {context.Child.ActorId}");
        return SupervisionDirective.Restart;
    }

    private SupervisionDirective HandleCriticalError(ChildFailureContext context)
    {
        Console.WriteLine($"  → Critical error, stopping {context.Child.ActorId}");
        return SupervisionDirective.Stop;
    }

    private SupervisionDirective HandleUnknownError(ChildFailureContext context)
    {
        Console.WriteLine($"  → Unknown error, escalating to parent");
        return SupervisionDirective.Escalate;
    }
}
```

---

### Supervision Hierarchy

Multi-level supervision tree.

```csharp
[Actor]
public class LeafWorkerActor : ActorBase
{
    public LeafWorkerActor(string actorId) : base(actorId) { }

    public void ProcessTask(string task)
    {
        Console.WriteLine($"{ActorId}: Processing {task}");
        
        // Simulate occasional failure
        if (task == "bad-task")
            throw new InvalidOperationException("Cannot process bad task");
    }
}

[Actor]
public class TeamLeaderActor : ActorBase, ISupervisor
{
    public TeamLeaderActor(string actorId, IActorFactory? actorFactory = null)
        : base(actorId, actorFactory) { }

    public override async Task OnActivateAsync(CancellationToken cancellationToken = default)
    {
        // Spawn team of workers
        await SpawnChildAsync<LeafWorkerActor>("worker-1");
        await SpawnChildAsync<LeafWorkerActor>("worker-2");
        await SpawnChildAsync<LeafWorkerActor>("worker-3");
        
        Console.WriteLine($"Team leader {ActorId} has {GetChildren().Count} workers");
    }

    public override Task<SupervisionDirective> OnChildFailureAsync(
        ChildFailureContext context,
        CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"Team leader {ActorId}: Worker failed, restarting");
        return Task.FromResult(SupervisionDirective.Restart);
    }

    public void DistributeWork(string[] tasks)
    {
        var workers = GetChildren().Cast<LeafWorkerActor>().ToList();
        for (int i = 0; i < tasks.Length; i++)
        {
            var worker = workers[i % workers.Count];
            worker.ProcessTask(tasks[i]);
        }
    }
}

[Actor]
public class ManagerActor : ActorBase, ISupervisor
{
    public ManagerActor(string actorId, IActorFactory? actorFactory = null)
        : base(actorId, actorFactory) { }

    public override async Task OnActivateAsync(CancellationToken cancellationToken = default)
    {
        // Spawn multiple team leaders
        await SpawnChildAsync<TeamLeaderActor>("team-alpha");
        await SpawnChildAsync<TeamLeaderActor>("team-beta");
        
        Console.WriteLine($"Manager {ActorId} oversees {GetChildren().Count} teams");
    }

    public override Task<SupervisionDirective> OnChildFailureAsync(
        ChildFailureContext context,
        CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"Manager {ActorId}: Team leader failed, restarting team");
        return Task.FromResult(SupervisionDirective.Restart);
    }
}
```

**Usage:**

```csharp
var factory = new ActorFactory();
var manager = factory.CreateActor<ManagerActor>("manager-1");
await manager.OnActivateAsync();

// Access team leaders
var teamAlpha = (TeamLeaderActor)manager.GetChildren()
    .First(c => c.ActorId == "team-alpha");

// Distribute work (some tasks will fail and trigger supervision)
teamAlpha.DistributeWork(new[] { "task-1", "task-2", "bad-task", "task-3" });
```

---

## Persistence Examples

### Redis State Storage

Complete example with Redis persistence.

```csharp
using Quark.Storage.Redis;
using StackExchange.Redis;
using System.Text.Json;

// State class
public class ShoppingCartState
{
    public List<CartItem> Items { get; set; } = new();
    public decimal TotalAmount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime LastModified { get; set; }
}

public class CartItem
{
    public string ProductId { get; set; } = "";
    public string ProductName { get; set; } = "";
    public int Quantity { get; set; }
    public decimal Price { get; set; }
}

// Actor
[Actor]
public class ShoppingCartActor : StatefulActorBase<ShoppingCartState>
{
    public ShoppingCartActor(string actorId, IStateStorageProvider storageProvider)
        : base(actorId, storageProvider)
    {
        State = new ShoppingCartState { CreatedAt = DateTime.UtcNow };
    }

    public override async Task OnActivateAsync(CancellationToken cancellationToken = default)
    {
        await LoadStateAsync(cancellationToken);
        Console.WriteLine($"Cart {ActorId} loaded with {State.Items.Count} items");
    }

    public async Task AddItemAsync(string productId, string productName, int quantity, decimal price)
    {
        var existingItem = State.Items.FirstOrDefault(i => i.ProductId == productId);
        
        if (existingItem != null)
        {
            existingItem.Quantity += quantity;
        }
        else
        {
            State.Items.Add(new CartItem
            {
                ProductId = productId,
                ProductName = productName,
                Quantity = quantity,
                Price = price
            });
        }

        State.LastModified = DateTime.UtcNow;
        RecalculateTotal();
        
        await SaveStateAsync();
        Console.WriteLine($"Added {quantity}x {productName} to cart {ActorId}");
    }

    public async Task RemoveItemAsync(string productId)
    {
        var item = State.Items.FirstOrDefault(i => i.ProductId == productId);
        if (item != null)
        {
            State.Items.Remove(item);
            State.LastModified = DateTime.UtcNow;
            RecalculateTotal();
            
            await SaveStateAsync();
            Console.WriteLine($"Removed {item.ProductName} from cart {ActorId}");
        }
    }

    public async Task ClearAsync()
    {
        State.Items.Clear();
        State.TotalAmount = 0;
        State.LastModified = DateTime.UtcNow;
        
        await SaveStateAsync();
        Console.WriteLine($"Cleared cart {ActorId}");
    }

    private void RecalculateTotal()
    {
        State.TotalAmount = State.Items.Sum(i => i.Price * i.Quantity);
    }

    public ShoppingCartState GetCart() => State;
}
```

**Usage:**

```csharp
// Setup
var redis = await ConnectionMultiplexer.ConnectAsync("localhost:6379");
var storageProvider = new RedisStateStorageProvider(redis);
var factory = new ActorFactory();

// Create and use cart
var cart = factory.CreateActor<ShoppingCartActor>("cart-user123");
await cart.OnActivateAsync();

await cart.AddItemAsync("prod-1", "Widget", 2, 9.99m);
await cart.AddItemAsync("prod-2", "Gadget", 1, 19.99m);

var state = cart.GetCart();
Console.WriteLine($"Total: ${state.TotalAmount}"); // Output: Total: $39.97

await cart.OnDeactivateAsync();

// Later session - state is persisted
var cart2 = factory.CreateActor<ShoppingCartActor>("cart-user123");
await cart2.OnActivateAsync();
var state2 = cart2.GetCart();
Console.WriteLine($"Restored cart with {state2.Items.Count} items");
```

---

### Optimistic Concurrency

Handling concurrent state updates.

```csharp
using Quark.Abstractions.Persistence;

[Actor]
public class BankAccountActor : StatefulActorBase<AccountState>
{
    public BankAccountActor(string actorId, IStateStorageProvider storageProvider)
        : base(actorId, storageProvider)
    {
    }

    public async Task<bool> WithdrawAsync(decimal amount)
    {
        const int maxRetries = 3;
        int attempt = 0;

        while (attempt < maxRetries)
        {
            try
            {
                await LoadStateAsync();

                if (State.Balance < amount)
                {
                    Console.WriteLine($"Insufficient funds: {State.Balance} < {amount}");
                    return false;
                }

                State.Balance -= amount;
                State.LastTransaction = DateTime.UtcNow;

                await SaveStateAsync();
                Console.WriteLine($"Withdrawn ${amount}. New balance: ${State.Balance}");
                return true;
            }
            catch (ConcurrencyException)
            {
                attempt++;
                Console.WriteLine($"Concurrency conflict, retrying... (attempt {attempt})");
                await Task.Delay(TimeSpan.FromMilliseconds(100 * attempt));
            }
        }

        throw new InvalidOperationException("Failed to withdraw after multiple retries");
    }

    public async Task DepositAsync(decimal amount)
    {
        await LoadStateAsync();
        State.Balance += amount;
        State.LastTransaction = DateTime.UtcNow;
        await SaveStateAsync();
        
        Console.WriteLine($"Deposited ${amount}. New balance: ${State.Balance}");
    }
}

public class AccountState
{
    public decimal Balance { get; set; }
    public DateTime LastTransaction { get; set; }
}
```

---

## Streaming Examples

### Implicit Subscriptions

Actors automatically subscribed to streams via attributes.

```csharp
using Quark.Abstractions.Streaming;

// Message types
public class OrderCreatedEvent
{
    public string OrderId { get; set; } = "";
    public string CustomerId { get; set; } = "";
    public decimal Amount { get; set; }
    public DateTime Timestamp { get; set; }
}

// Actor with implicit subscription
[Actor]
[QuarkStream("orders/created")]
public class OrderProcessorActor : ActorBase, IStreamConsumer<OrderCreatedEvent>
{
    public OrderProcessorActor(string actorId) : base(actorId) { }

    public async Task OnStreamMessageAsync(
        OrderCreatedEvent message,
        StreamId streamId,
        CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"[{ActorId}] Processing order {message.OrderId}");
        Console.WriteLine($"  Customer: {message.CustomerId}");
        Console.WriteLine($"  Amount: ${message.Amount}");

        // Simulate processing
        await Task.Delay(100, cancellationToken);

        Console.WriteLine($"[{ActorId}] Order {message.OrderId} processed successfully");
    }
}
```

**Usage:**

```csharp
using Quark.Core.Streaming;

var factory = new ActorFactory();
var streamProvider = new QuarkStreamProvider(factory);

// Register implicit subscription
streamProvider.Broker.RegisterImplicitSubscription(
    "orders/created",
    typeof(OrderProcessorActor),
    typeof(OrderCreatedEvent)
);

// Publish events - actors are automatically activated
var stream = streamProvider.GetStream<OrderCreatedEvent>("orders/created", "order-123");

await stream.PublishAsync(new OrderCreatedEvent
{
    OrderId = "order-123",
    CustomerId = "customer-456",
    Amount = 99.99m,
    Timestamp = DateTime.UtcNow
});
```

---

### Explicit Pub/Sub

Dynamic subscriptions created at runtime.

```csharp
var streamProvider = new QuarkStreamProvider(factory);
var receivedMessages = new List<string>();

// Get stream
var chatStream = streamProvider.GetStream<string>("chat/lobby", "lobby-1");

// Subscribe explicitly
var subscription = await chatStream.SubscribeAsync(async message =>
{
    receivedMessages.Add(message);
    Console.WriteLine($"Received: {message}");
    await Task.CompletedTask;
});

// Publish messages
await chatStream.PublishAsync("User Alice joined");
await chatStream.PublishAsync("Alice: Hello everyone!");
await chatStream.PublishAsync("User Bob joined");

await Task.Delay(100); // Allow async processing

Console.WriteLine($"Received {receivedMessages.Count} messages");

// Unsubscribe
await subscription.UnsubscribeAsync();

// This won't be received
await chatStream.PublishAsync("User Charlie joined");
```

---

### Multiple Subscribers

Multiple actors/handlers on the same stream.

```csharp
// Logger actor
[Actor]
[QuarkStream("system/events")]
public class LoggerActor : ActorBase, IStreamConsumer<SystemEvent>
{
    public LoggerActor(string actorId) : base(actorId) { }

    public Task OnStreamMessageAsync(
        SystemEvent message,
        StreamId streamId,
        CancellationToken cancellationToken)
    {
        Console.WriteLine($"[LOG] {message.Timestamp}: {message.Message}");
        return Task.CompletedTask;
    }
}

// Metrics actor
[Actor]
[QuarkStream("system/events")]
public class MetricsActor : ActorBase, IStreamConsumer<SystemEvent>
{
    private int _eventCount = 0;

    public MetricsActor(string actorId) : base(actorId) { }

    public Task OnStreamMessageAsync(
        SystemEvent message,
        StreamId streamId,
        CancellationToken cancellationToken)
    {
        _eventCount++;
        Console.WriteLine($"[METRICS] Total events: {_eventCount}");
        return Task.CompletedTask;
    }
}

// Alert actor
[Actor]
[QuarkStream("system/events")]
public class AlertActor : ActorBase, IStreamConsumer<SystemEvent>
{
    public AlertActor(string actorId) : base(actorId) { }

    public Task OnStreamMessageAsync(
        SystemEvent message,
        StreamId streamId,
        CancellationToken cancellationToken)
    {
        if (message.Severity == "Error" || message.Severity == "Critical")
        {
            Console.WriteLine($"[ALERT] {message.Severity}: {message.Message}");
            // Send email, page on-call, etc.
        }
        return Task.CompletedTask;
    }
}

public class SystemEvent
{
    public string Message { get; set; } = "";
    public string Severity { get; set; } = "Info";
    public DateTime Timestamp { get; set; }
}
```

**Usage:**

```csharp
var factory = new ActorFactory();
var streamProvider = new QuarkStreamProvider(factory);

// Register all subscribers
streamProvider.Broker.RegisterImplicitSubscription(
    "system/events", typeof(LoggerActor), typeof(SystemEvent));
streamProvider.Broker.RegisterImplicitSubscription(
    "system/events", typeof(MetricsActor), typeof(SystemEvent));
streamProvider.Broker.RegisterImplicitSubscription(
    "system/events", typeof(AlertActor), typeof(SystemEvent));

// Publish event - all three actors receive it
var stream = streamProvider.GetStream<SystemEvent>("system/events", "server-1");

await stream.PublishAsync(new SystemEvent
{
    Message = "Database connection lost",
    Severity = "Error",
    Timestamp = DateTime.UtcNow
});

// Output:
// [LOG] 2025-01-29 10:30:00: Database connection lost
// [METRICS] Total events: 1
// [ALERT] Error: Database connection lost
```

---

## Complete Working Examples

### Basic Example (from repository)

Full source: `examples/Quark.Examples.Basic/`

```csharp
using Quark.Core.Actors;
using Quark.Examples.Basic.Actors;

Console.WriteLine("=== Quark Actor Framework - Basic Example ===");
Console.WriteLine();

// Create an actor factory
var factory = new ActorFactory();
Console.WriteLine("✓ Actor factory created");

// Create a counter actor
var counter = factory.CreateActor<CounterActor>("counter-1");
Console.WriteLine($"✓ Counter actor created with ID: {counter.ActorId}");

// Activate the actor
await counter.OnActivateAsync();
Console.WriteLine("✓ Actor activated");

// Increment the counter
counter.Increment();
Console.WriteLine($"✓ Counter incremented to: {counter.GetValue()}");

counter.Increment();
counter.Increment();
Console.WriteLine($"✓ Counter incremented to: {counter.GetValue()}");

// Process a message
var message = await counter.ProcessMessageAsync("Hello from Quark!");
Console.WriteLine($"✓ Message processed: {message}");

// Get or create the same actor (should return the same instance)
var sameCounter = factory.GetOrCreateActor<CounterActor>("counter-1");
Console.WriteLine($"✓ GetOrCreate returned same instance: {ReferenceEquals(counter, sameCounter)}");

// Deactivate the actor
await counter.OnDeactivateAsync();
Console.WriteLine("✓ Actor deactivated");

Console.WriteLine();
Console.WriteLine("=== Example completed successfully ===");
```

**Run it:**

```bash
cd examples/Quark.Examples.Basic
dotnet run
```

---

### Supervision Example (from repository)

Full source: `examples/Quark.Examples.Supervision/`

Demonstrates:
- Parent-child relationships
- Spawning child actors
- Handling child failures
- Custom supervision strategies

**Key code:**

```csharp
var factory = new ActorFactory();
var supervisor = factory.CreateActor<SupervisorActor>("supervisor-1");
await supervisor.OnActivateAsync();

// Spawn child actors
var child1 = await supervisor.SpawnChildAsync<WorkerActor>("worker-1");
var child2 = await supervisor.SpawnChildAsync<WorkerActor>("worker-2");

// Simulate failures
var exception = new InvalidOperationException("Simulated worker failure");
var failureContext = new ChildFailureContext(child1, exception);
var directive = await supervisor.OnChildFailureAsync(failureContext);

Console.WriteLine($"Supervision directive: {directive}");
```

**Run it:**

```bash
cd examples/Quark.Examples.Supervision
dotnet run
```

---

### Streaming Example (from repository)

Full source: `examples/Quark.Examples.Streaming/`

Demonstrates:
- Implicit subscriptions with `[QuarkStream]`
- Explicit pub/sub patterns
- Multiple subscribers
- Stream messages and handlers

**Key concepts:**

1. **Implicit Subscriptions** - Actors auto-activated when messages arrive
2. **Explicit Pub/Sub** - Dynamic subscriptions at runtime
3. **Multiple Subscribers** - Fan-out pattern

**Run it:**

```bash
cd examples/Quark.Examples.Streaming
dotnet run
```

**Expected output:**

```
╔════════════════════════════════════════════════════════════╗
║        Quark Phase 5: Reactive Streaming Example          ║
╚════════════════════════════════════════════════════════════╝

═══ Example 1: Implicit Subscriptions ═══
Actors are automatically activated when messages arrive...
[OrderProcessor-order-123] Processing order order-123
  Customer: John Doe
  Amount: $99.99
✓ Order processed by OrderProcessorActor

═══ Example 2: Explicit Pub/Sub ═══
Explicit subscriptions that can be created and destroyed...
  → Received: Server started
  → Received: Database connected
✓ Received 3 messages through explicit subscription

═══ Example 3: Multiple Subscribers ═══
Multiple actors/handlers can subscribe to the same stream...
  [Subscriber 1] User Alice joined
  [Subscriber 2] User Alice joined
✓ Subscriber 1 received 3 messages
✓ Subscriber 2 received 3 messages
```

---

## See Also

- **[Getting Started](Getting-Started)** - Setup and first actor
- **[Actor Model](Actor-Model)** - Core concepts
- **[Supervision](Supervision)** - Fault tolerance
- **[Persistence](Persistence)** - State management
- **[Streaming](Streaming)** - Reactive patterns
- **[API Reference](API-Reference)** - Complete API documentation
- **[FAQ](FAQ)** - Common issues

---

## Running Examples

All examples are located in the `examples/` directory:

```bash
# Clone repository
git clone https://github.com/thnak/Quark.git
cd Quark

# Build all projects
dotnet build

# Run basic example
dotnet run --project examples/Quark.Examples.Basic

# Run supervision example
dotnet run --project examples/Quark.Examples.Supervision

# Run streaming example
dotnet run --project examples/Quark.Examples.Streaming
```

Each example includes detailed console output showing the framework in action.
