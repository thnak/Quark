# API Reference

Complete reference for Quark's core interfaces, classes, and attributes.

## Table of Contents

- [Core Interfaces](#core-interfaces)
  - [IActor](#iactor)
  - [ISupervisor](#isupervisor)
  - [IActorFactory](#iactorfactory)
  - [IActorContext](#iactorcontext)
  - [IMailbox](#imailbox)
- [Actor Base Classes](#actor-base-classes)
  - [ActorBase](#actorbase)
  - [StatefulActorBase](#statefulactorbase)
- [Persistence](#persistence)
  - [IStateStorage](#istatestorage)
  - [IStateStorageProvider](#istatestorageprovider)
- [Streaming](#streaming)
  - [IQuarkStreamProvider](#iquarkstreamprovider)
  - [IStreamHandle](#istreamhandle)
  - [IStreamConsumer](#istreamconsumer)
- [Clustering](#clustering)
  - [IClusterMembership](#iclustermembership)
  - [IActorDirectory](#iactordirectory)
  - [IActorTransport](#iactortransport)
- [Timers and Reminders](#timers-and-reminders)
  - [IActorTimer](#iactortimer)
  - [IRemindable](#iremindable)
- [Attributes](#attributes)
  - [[Actor]](#actor-attribute)
  - [[QuarkState]](#quarkstate-attribute)
  - [[QuarkStream]](#quarkstream-attribute)
- [Enums and Data Types](#enums-and-data-types)

---

## Core Interfaces

### IActor

Base interface for all actors in the Quark framework.

**Namespace:** `Quark.Abstractions`

```csharp
public interface IActor
{
    /// <summary>
    /// Gets the unique identifier for this actor instance.
    /// </summary>
    string ActorId { get; }

    /// <summary>
    /// Called when the actor is activated.
    /// </summary>
    Task OnActivateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Called when the actor is deactivated.
    /// </summary>
    Task OnDeactivateAsync(CancellationToken cancellationToken = default);
}
```

**Properties:**

- **ActorId** - Unique identifier for this actor instance. Must be unique within the actor type.

**Methods:**

- **OnActivateAsync** - Called when the actor is first activated. Use for initialization, loading state, subscribing to streams, etc.
- **OnDeactivateAsync** - Called before the actor is removed from memory. Use for cleanup, saving state, unsubscribing, etc.

**Example:**

```csharp
[Actor]
public class MyActor : ActorBase
{
    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        Console.WriteLine($"Actor {ActorId} is activating");
        await LoadStateAsync(cancellationToken);
    }

    public override async Task OnDeactivateAsync(CancellationToken cancellationToken)
    {
        Console.WriteLine($"Actor {ActorId} is deactivating");
        await SaveStateAsync(cancellationToken);
    }
}
```

---

### ISupervisor

Interface for actors that can supervise child actors.

**Namespace:** `Quark.Abstractions`

**Inherits:** `IActor`

```csharp
public interface ISupervisor : IActor
{
    /// <summary>
    /// Called when a child actor fails.
    /// </summary>
    Task<SupervisionDirective> OnChildFailureAsync(
        ChildFailureContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Spawns a new child actor under this supervisor.
    /// </summary>
    Task<TChild> SpawnChildAsync<TChild>(
        string actorId,
        CancellationToken cancellationToken = default) where TChild : IActor;

    /// <summary>
    /// Gets all child actors currently supervised by this actor.
    /// </summary>
    IReadOnlyCollection<IActor> GetChildren();
}
```

**Methods:**

- **OnChildFailureAsync** - Called when a child actor throws an exception. Returns a `SupervisionDirective` indicating how to handle the failure.
- **SpawnChildAsync** - Creates and activates a new child actor under this supervisor.
- **GetChildren** - Returns all currently active child actors.

**Example:**

```csharp
[Actor]
public class SupervisorActor : ActorBase, ISupervisor
{
    public SupervisorActor(string actorId, IActorFactory? actorFactory = null) 
        : base(actorId, actorFactory) { }

    public override async Task<SupervisionDirective> OnChildFailureAsync(
        ChildFailureContext context,
        CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"Child {context.Child.ActorId} failed: {context.Exception.Message}");

        return context.Exception switch
        {
            TimeoutException => SupervisionDirective.Resume,
            ArgumentException => SupervisionDirective.Restart,
            OutOfMemoryException => SupervisionDirective.Stop,
            _ => SupervisionDirective.Escalate
        };
    }
}
```

---

### IActorFactory

Factory interface for creating actor instances.

**Namespace:** `Quark.Abstractions`

```csharp
public interface IActorFactory
{
    /// <summary>
    /// Creates a new actor instance with the specified ID.
    /// </summary>
    TActor CreateActor<TActor>(string actorId) where TActor : IActor;

    /// <summary>
    /// Gets an existing actor or creates a new one if it doesn't exist.
    /// </summary>
    TActor GetOrCreateActor<TActor>(string actorId) where TActor : IActor;
}
```

**Methods:**

- **CreateActor** - Creates a new actor instance. Throws if an actor with the same ID and type already exists.
- **GetOrCreateActor** - Returns existing actor if found, otherwise creates a new one. Thread-safe.

**Example:**

```csharp
var factory = new ActorFactory();

// Create a new actor
var actor1 = factory.CreateActor<UserActor>("user-123");

// Get existing or create new
var actor2 = factory.GetOrCreateActor<UserActor>("user-123");

// actor1 and actor2 reference the same instance
Assert.True(ReferenceEquals(actor1, actor2));
```

---

### IActorContext

Provides contextual information and services to an actor.

**Namespace:** `Quark.Abstractions`

```csharp
public interface IActorContext
{
    /// <summary>
    /// Gets the actor ID.
    /// </summary>
    string ActorId { get; }

    /// <summary>
    /// Gets the actor type name.
    /// </summary>
    string ActorType { get; }

    /// <summary>
    /// Gets the mailbox for this actor.
    /// </summary>
    IMailbox Mailbox { get; }

    /// <summary>
    /// Gets the actor factory.
    /// </summary>
    IActorFactory? Factory { get; }
}
```

**Properties:**

- **ActorId** - The actor's unique identifier
- **ActorType** - The actor's type name
- **Mailbox** - The actor's message queue
- **Factory** - Factory for creating child actors

---

### IMailbox

Interface for actor message queues.

**Namespace:** `Quark.Abstractions`

```csharp
public interface IMailbox
{
    /// <summary>
    /// Gets the number of messages in the mailbox.
    /// </summary>
    int Count { get; }

    /// <summary>
    /// Enqueues a message for processing.
    /// </summary>
    Task EnqueueAsync(IActorMessage message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Dequeues the next message from the mailbox.
    /// </summary>
    Task<IActorMessage> DequeueAsync(CancellationToken cancellationToken = default);
}
```

**Methods:**

- **EnqueueAsync** - Adds a message to the mailbox queue
- **DequeueAsync** - Retrieves and removes the next message (blocks if empty)

---

## Actor Base Classes

### ActorBase

Base implementation of `IActor` providing common functionality.

**Namespace:** `Quark.Core.Actors`

**Implements:** `IActor`, `ISupervisor`

```csharp
public abstract class ActorBase : IActor, ISupervisor
{
    protected ActorBase(string actorId, IActorFactory? actorFactory = null);

    public string ActorId { get; }
    
    public virtual Task OnActivateAsync(CancellationToken cancellationToken = default);
    
    public virtual Task OnDeactivateAsync(CancellationToken cancellationToken = default);

    public virtual Task<SupervisionDirective> OnChildFailureAsync(
        ChildFailureContext context,
        CancellationToken cancellationToken = default);

    public Task<TChild> SpawnChildAsync<TChild>(
        string actorId,
        CancellationToken cancellationToken = default) where TChild : IActor;

    public IReadOnlyCollection<IActor> GetChildren();
}
```

**Constructor:**

- **ActorBase(string actorId, IActorFactory? actorFactory = null)** - Creates a new actor with the specified ID

**Properties:**

- **ActorId** - The actor's unique identifier

**Methods:**

- **OnActivateAsync** - Override to add initialization logic
- **OnDeactivateAsync** - Override to add cleanup logic
- **OnChildFailureAsync** - Override to implement custom supervision strategy
- **SpawnChildAsync** - Creates a child actor under this supervisor
- **GetChildren** - Returns all child actors

**Example:**

```csharp
[Actor(Name = "Counter")]
public class CounterActor : ActorBase
{
    private int _count;

    public CounterActor(string actorId) : base(actorId)
    {
        _count = 0;
    }

    public void Increment() => _count++;
    
    public int GetCount() => _count;

    public override Task OnActivateAsync(CancellationToken cancellationToken)
    {
        Console.WriteLine($"Counter {ActorId} activated");
        return Task.CompletedTask;
    }
}
```

---

### StatefulActorBase

Base class for actors with persistent state.

**Namespace:** `Quark.Core.Actors`

**Inherits:** `ActorBase`

```csharp
public abstract class StatefulActorBase<TState> : ActorBase
    where TState : class, new()
{
    protected StatefulActorBase(
        string actorId,
        IStateStorageProvider storageProvider,
        IActorFactory? actorFactory = null);

    protected TState State { get; set; }

    protected Task LoadStateAsync(CancellationToken cancellationToken = default);
    
    protected Task SaveStateAsync(CancellationToken cancellationToken = default);
    
    protected Task DeleteStateAsync(CancellationToken cancellationToken = default);
}
```

**Properties:**

- **State** - The actor's persistent state object

**Methods:**

- **LoadStateAsync** - Loads state from storage
- **SaveStateAsync** - Saves current state to storage
- **DeleteStateAsync** - Removes state from storage

**Example:**

```csharp
public class UserState
{
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public int Points { get; set; }
}

[Actor]
public class UserActor : StatefulActorBase<UserState>
{
    public UserActor(string actorId, IStateStorageProvider storageProvider)
        : base(actorId, storageProvider)
    {
    }

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        await LoadStateAsync(cancellationToken);
        Console.WriteLine($"User {State.Name} activated");
    }

    public async Task UpdateEmailAsync(string email)
    {
        State.Email = email;
        await SaveStateAsync();
    }

    public void AddPoints(int points)
    {
        State.Points += points;
    }
}
```

---

## Persistence

### IStateStorage

Interface for state storage operations.

**Namespace:** `Quark.Abstractions.Persistence`

```csharp
public interface IStateStorage
{
    /// <summary>
    /// Loads state from storage.
    /// </summary>
    Task<StateWithVersion<T>?> LoadAsync<T>(
        string key,
        CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Saves state to storage.
    /// </summary>
    Task SaveAsync<T>(
        string key,
        T state,
        long expectedVersion,
        CancellationToken cancellationToken = default) where T : class;

    /// <summary>
    /// Deletes state from storage.
    /// </summary>
    Task DeleteAsync(
        string key,
        CancellationToken cancellationToken = default);
}
```

**Methods:**

- **LoadAsync** - Retrieves state with version information
- **SaveAsync** - Stores state with optimistic concurrency check
- **DeleteAsync** - Removes state from storage

**Throws:**

- **ConcurrencyException** - When expectedVersion doesn't match stored version

---

### IStateStorageProvider

Factory for creating state storage instances.

**Namespace:** `Quark.Abstractions.Persistence`

```csharp
public interface IStateStorageProvider
{
    /// <summary>
    /// Creates a state storage instance for the specified actor type.
    /// </summary>
    IStateStorage GetStorage(string actorType);
}
```

**Example:**

```csharp
using Quark.Storage.Redis;

var redis = await ConnectionMultiplexer.ConnectAsync("localhost:6379");
var storageProvider = new RedisStateStorageProvider(redis);

var storage = storageProvider.GetStorage("UserActor");

// Save state
await storage.SaveAsync("user-123", new UserState 
{ 
    Name = "Alice" 
}, expectedVersion: 0);

// Load state
var stateWithVersion = await storage.LoadAsync<UserState>("user-123");
Console.WriteLine($"Loaded: {stateWithVersion.State.Name}, v{stateWithVersion.Version}");
```

---

## Streaming

### IQuarkStreamProvider

Main interface for streaming operations.

**Namespace:** `Quark.Abstractions.Streaming`

```csharp
public interface IQuarkStreamProvider
{
    /// <summary>
    /// Gets a stream handle for publishing/subscribing.
    /// </summary>
    IStreamHandle<T> GetStream<T>(string streamName, string streamKey);
}
```

**Methods:**

- **GetStream** - Returns a handle to a specific stream

**Parameters:**

- **streamName** - Logical stream name (e.g., "orders/processed")
- **streamKey** - Partition key (e.g., order ID)

---

### IStreamHandle

Handle for a specific stream.

**Namespace:** `Quark.Abstractions.Streaming`

```csharp
public interface IStreamHandle<T>
{
    /// <summary>
    /// Publishes a message to the stream.
    /// </summary>
    Task PublishAsync(T message, CancellationToken cancellationToken = default);

    /// <summary>
    /// Subscribes to messages on this stream.
    /// </summary>
    Task<IStreamSubscriptionHandle> SubscribeAsync(
        Func<T, Task> handler,
        CancellationToken cancellationToken = default);
}
```

**Methods:**

- **PublishAsync** - Sends a message to all subscribers
- **SubscribeAsync** - Registers a handler for messages on this stream

**Example:**

```csharp
var streamProvider = new QuarkStreamProvider(actorFactory);

// Get a stream
var stream = streamProvider.GetStream<OrderMessage>("orders/processed", "order-123");

// Subscribe
var subscription = await stream.SubscribeAsync(async message =>
{
    Console.WriteLine($"Received: {message.OrderId}");
    await ProcessOrderAsync(message);
});

// Publish
await stream.PublishAsync(new OrderMessage 
{ 
    OrderId = "order-123",
    Amount = 99.99m 
});

// Unsubscribe
await subscription.UnsubscribeAsync();
```

---

### IStreamConsumer

Interface for actors that consume stream messages.

**Namespace:** `Quark.Abstractions.Streaming`

```csharp
public interface IStreamConsumer<T>
{
    /// <summary>
    /// Called when a message arrives on a subscribed stream.
    /// </summary>
    Task OnStreamMessageAsync(
        T message,
        StreamId streamId,
        CancellationToken cancellationToken = default);
}
```

**Methods:**

- **OnStreamMessageAsync** - Handles incoming stream messages

**Example:**

```csharp
[Actor]
[QuarkStream("notifications/user")]
public class NotificationActor : ActorBase, IStreamConsumer<string>
{
    public NotificationActor(string actorId) : base(actorId) { }

    public async Task OnStreamMessageAsync(
        string message,
        StreamId streamId,
        CancellationToken cancellationToken)
    {
        Console.WriteLine($"Notification: {message}");
        await SendEmailAsync(message, cancellationToken);
    }

    private Task SendEmailAsync(string message, CancellationToken ct)
    {
        // Send email
        return Task.CompletedTask;
    }
}
```

---

## Clustering

### IClusterMembership

Manages cluster membership and health monitoring.

**Namespace:** `Quark.Abstractions.Clustering`

```csharp
public interface IClusterMembership
{
    string CurrentSiloId { get; }

    Task RegisterSiloAsync(SiloInfo siloInfo, CancellationToken cancellationToken = default);
    
    Task UnregisterSiloAsync(CancellationToken cancellationToken = default);
    
    Task<IReadOnlyCollection<SiloInfo>> GetActiveSilosAsync(
        CancellationToken cancellationToken = default);
    
    Task<SiloInfo?> GetSiloAsync(string siloId, CancellationToken cancellationToken = default);
    
    Task UpdateHeartbeatAsync(CancellationToken cancellationToken = default);
    
    Task StartAsync(CancellationToken cancellationToken = default);
    
    Task StopAsync(CancellationToken cancellationToken = default);

    event EventHandler<SiloInfo>? SiloJoined;
    event EventHandler<SiloInfo>? SiloLeft;
}
```

See [Clustering](Clustering) for detailed documentation.

---

### IActorDirectory

Tracks actor locations in a distributed cluster.

**Namespace:** `Quark.Abstractions.Clustering`

```csharp
public interface IActorDirectory
{
    Task RegisterActorAsync(ActorLocation location, CancellationToken cancellationToken = default);
    
    Task UnregisterActorAsync(string actorId, string actorType, 
        CancellationToken cancellationToken = default);
    
    Task<ActorLocation?> LookupActorAsync(string actorId, string actorType,
        CancellationToken cancellationToken = default);
    
    Task<IReadOnlyCollection<ActorLocation>> GetActorsBySiloAsync(string siloId,
        CancellationToken cancellationToken = default);
}
```

**ActorLocation:**

```csharp
public sealed class ActorLocation
{
    public string ActorId { get; }
    public string ActorType { get; }
    public string SiloId { get; }
    public DateTimeOffset LastUpdated { get; }
}
```

---

### IActorTransport

Handles cross-silo actor invocations.

**Namespace:** `Quark.Abstractions.Transport`

```csharp
public interface IActorTransport
{
    /// <summary>
    /// Invokes a method on a remote actor.
    /// </summary>
    Task<byte[]?> InvokeActorAsync(
        string targetSiloId,
        string actorType,
        string actorId,
        string methodName,
        byte[]? arguments,
        CancellationToken cancellationToken = default);
}
```

---

## Timers and Reminders

### IActorTimer

Interface for in-memory timers.

**Namespace:** `Quark.Abstractions.Timers`

```csharp
public interface IActorTimer : IDisposable
{
    string Name { get; }
    bool IsRunning { get; }

    void Start();
    void Stop();
}
```

See [Timers and Reminders](Timers-and-Reminders) for detailed documentation.

---

### IRemindable

Interface for actors that can receive reminders.

**Namespace:** `Quark.Abstractions.Reminders`

```csharp
public interface IRemindable
{
    /// <summary>
    /// Called when a reminder fires.
    /// </summary>
    Task ReceiveReminderAsync(
        string reminderName,
        byte[]? data,
        CancellationToken cancellationToken = default);
}
```

---

## Attributes

### [Actor] Attribute

Marks a class for actor code generation.

**Namespace:** `Quark.Abstractions`

```csharp
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public class ActorAttribute : Attribute
{
    /// <summary>
    /// Gets or sets the actor name.
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Gets or sets whether the actor is reentrant.
    /// </summary>
    public bool Reentrant { get; set; } = false;
}
```

**Properties:**

- **Name** - Friendly name for the actor type (optional)
- **Reentrant** - Whether the actor can process messages while waiting for async operations (default: false)

**Example:**

```csharp
[Actor(Name = "UserManager", Reentrant = false)]
public class UserActor : ActorBase
{
    // Actor implementation
}
```

---

### [QuarkState] Attribute

Marks properties for state persistence code generation.

**Namespace:** `Quark.Abstractions.Persistence`

```csharp
[AttributeUsage(AttributeTargets.Property, AllowMultiple = false)]
public class QuarkStateAttribute : Attribute
{
    public string? StorageName { get; set; }
}
```

**Properties:**

- **StorageName** - Optional custom storage name

**Example:**

```csharp
public partial class UserActor : ActorBase
{
    [QuarkState]
    public partial string Name { get; set; }

    [QuarkState]
    public partial string Email { get; set; }

    [QuarkState(StorageName = "user-points")]
    public partial int Points { get; set; }
}
```

**Note:** Source generator creates the backing fields and Load/Save/Delete methods.

---

### [QuarkStream] Attribute

Marks actors for implicit stream subscription.

**Namespace:** `Quark.Abstractions.Streaming`

```csharp
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public class QuarkStreamAttribute : Attribute
{
    public QuarkStreamAttribute(string streamName);

    public string StreamName { get; }
}
```

**Properties:**

- **StreamName** - Name of the stream to subscribe to

**Example:**

```csharp
[Actor]
[QuarkStream("orders/processed")]
[QuarkStream("orders/cancelled")]
public class OrderProcessorActor : ActorBase, IStreamConsumer<OrderMessage>
{
    public async Task OnStreamMessageAsync(
        OrderMessage message,
        StreamId streamId,
        CancellationToken cancellationToken)
    {
        if (streamId.Name == "orders/processed")
            await ProcessOrderAsync(message, cancellationToken);
        else if (streamId.Name == "orders/cancelled")
            await CancelOrderAsync(message, cancellationToken);
    }
}
```

---

## Enums and Data Types

### SupervisionDirective

Determines how to handle child actor failures.

**Namespace:** `Quark.Abstractions`

```csharp
public enum SupervisionDirective
{
    /// <summary>
    /// Resume the child actor (ignore the failure).
    /// </summary>
    Resume,

    /// <summary>
    /// Restart the child actor (deactivate and re-activate).
    /// </summary>
    Restart,

    /// <summary>
    /// Stop the child actor permanently.
    /// </summary>
    Stop,

    /// <summary>
    /// Escalate the failure to the parent's supervisor.
    /// </summary>
    Escalate
}
```

---

### ChildFailureContext

Contains information about a child actor failure.

**Namespace:** `Quark.Abstractions`

```csharp
public sealed class ChildFailureContext
{
    public ChildFailureContext(IActor child, Exception exception);

    /// <summary>
    /// The child actor that failed.
    /// </summary>
    public IActor Child { get; }

    /// <summary>
    /// The exception that caused the failure.
    /// </summary>
    public Exception Exception { get; }
}
```

---

### StreamId

Identifies a specific stream.

**Namespace:** `Quark.Abstractions.Streaming`

```csharp
public sealed class StreamId
{
    public StreamId(string name, string key);

    /// <summary>
    /// The stream name (e.g., "orders/processed").
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// The partition key (e.g., order ID).
    /// </summary>
    public string Key { get; }
}
```

---

### SiloInfo

Contains information about a cluster node.

**Namespace:** `Quark.Abstractions.Clustering`

```csharp
public sealed class SiloInfo
{
    public string SiloId { get; set; } = "";
    public string Address { get; set; } = "";
    public int Port { get; set; }
    public DateTimeOffset JoinedAt { get; set; }
    public DateTimeOffset LastHeartbeat { get; set; }
}
```

---

## Quick Reference

### Creating an Actor

```csharp
[Actor(Name = "MyActor")]
public class MyActor : ActorBase
{
    public MyActor(string actorId) : base(actorId) { }
}

var factory = new ActorFactory();
var actor = factory.CreateActor<MyActor>("actor-1");
await actor.OnActivateAsync();
```

### Stateful Actor

```csharp
[Actor]
public class MyActor : StatefulActorBase<MyState>
{
    public MyActor(string actorId, IStateStorageProvider storage)
        : base(actorId, storage) { }

    public override async Task OnActivateAsync(CancellationToken ct)
    {
        await LoadStateAsync(ct);
    }
}
```

### Supervision

```csharp
[Actor]
public class MySupervisor : ActorBase, ISupervisor
{
    public override Task<SupervisionDirective> OnChildFailureAsync(
        ChildFailureContext context, CancellationToken ct)
    {
        return Task.FromResult(SupervisionDirective.Restart);
    }
}
```

### Streaming

```csharp
[Actor]
[QuarkStream("events/system")]
public class EventActor : ActorBase, IStreamConsumer<Event>
{
    public Task OnStreamMessageAsync(Event message, StreamId id, CancellationToken ct)
    {
        // Process message
        return Task.CompletedTask;
    }
}
```

---

## See Also

- **[Getting Started](Getting-Started)** - Quick start guide
- **[Actor Model](Actor-Model)** - Core concepts
- **[Persistence](Persistence)** - State management
- **[Streaming](Streaming)** - Reactive streams
- **[Clustering](Clustering)** - Distributed actors
- **[Timers and Reminders](Timers-and-Reminders)** - Temporal services
- **[Examples](Examples)** - Code samples
- **[FAQ](FAQ)** - Troubleshooting
