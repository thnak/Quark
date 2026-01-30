# Migrating from Orleans to Quark

This guide helps you migrate from Microsoft Orleans to Quark. While both frameworks implement the virtual actor model, there are significant differences in how they achieve it.

## Key Differences Overview

| Feature | Orleans | Quark |
|---------|---------|-------|
| **Code Generation** | Runtime (Roslyn IL) | Compile-time (Source Generators) |
| **AOT Support** | Partial | Full Native AOT |
| **Actor Interface** | Interface-based (IGrain) | Class-based with attributes |
| **Activation** | Implicit via GrainFactory | Explicit via ActorFactory |
| **State** | IPersistentState<T> | [QuarkState] properties |
| **Clustering** | SQL/Azure/Consul/etc. | Redis |
| **Serialization** | Multiple providers | System.Text.Json |
| **Timers** | RegisterTimer() | ITimerService |
| **Reminders** | RegisterReminder() | IReminderService |
| **Streams** | IAsyncStream<T> | IStreamConsumer<T> |

## Step-by-Step Migration

### 1. Project Setup

#### Orleans Project Structure

```xml
<ItemGroup>
  <PackageReference Include="Microsoft.Orleans.Server" Version="7.0.0" />
  <PackageReference Include="Microsoft.Orleans.Client" Version="7.0.0" />
  <PackageReference Include="Microsoft.Orleans.CodeGenerator.MSBuild" Version="7.0.0" />
</ItemGroup>
```

#### Quark Project Structure

```xml
<ItemGroup>
  <!-- Core framework -->
  <ProjectReference Include="path/to/Quark.Core/Quark.Core.csproj" />
  
  <!-- REQUIRED: Source generator reference (not transitive!) -->
  <ProjectReference Include="path/to/Quark.Generators/Quark.Generators.csproj" 
                    OutputItemType="Analyzer" 
                    ReferenceOutputAssembly="false" />
  
  <!-- Optional: Clustering -->
  <ProjectReference Include="path/to/Quark.Clustering.Redis/Quark.Clustering.Redis.csproj" />
  
  <!-- Optional: Storage -->
  <ProjectReference Include="path/to/Quark.Storage.Redis/Quark.Storage.Redis.csproj" />
</ItemGroup>
```

⚠️ **Critical**: The source generator reference must be explicit in every project that defines actors.

### 2. Actor Definition Migration

#### Orleans Grain

```csharp
// Interface
public interface ICounterGrain : IGrainWithStringKey
{
    Task Increment();
    Task Decrement();
    Task<int> GetCount();
}

// Implementation
public class CounterGrain : Grain, ICounterGrain
{
    private int _count;

    public override Task OnActivateAsync(CancellationToken cancellationToken)
    {
        _count = 0;
        return base.OnActivateAsync(cancellationToken);
    }

    public Task Increment()
    {
        _count++;
        return Task.CompletedTask;
    }

    public Task Decrement()
    {
        _count--;
        return Task.CompletedTask;
    }

    public Task<int> GetCount()
    {
        return Task.FromResult(_count);
    }
}
```

#### Quark Actor

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
        // Initialization if needed
        return Task.CompletedTask;
    }

    public void Increment()
    {
        _count++;
    }

    public void Decrement()
    {
        _count--;
    }

    public int GetCount()
    {
        return _count;
    }
}
```

**Key Changes:**
- No separate interface (unless you want one for testing)
- Use `[Actor]` attribute instead of inheriting from `Grain`
- Constructor takes `actorId` instead of implicit key from grain context
- Methods can be synchronous (no forced Task return)
- Inherit from `ActorBase` instead of `Grain`

### 3. Actor Activation Migration

#### Orleans: Grain Factory

```csharp
// Get grain reference
var counter = grainFactory.GetGrain<ICounterGrain>("counter-1");

// Call methods (activation is implicit)
await counter.Increment();
var count = await counter.GetCount();
```

#### Quark: Actor Factory

```csharp
// Create actor factory
var factory = new ActorFactory();

// Create and activate actor
var counter = factory.CreateActor<CounterActor>("counter-1");
await counter.OnActivateAsync();

// Call methods (synchronous if method is synchronous)
counter.Increment();
var count = counter.GetCount();
```

**Key Changes:**
- Explicit actor creation with `CreateActor<T>()`
- Explicit activation with `OnActivateAsync()`
- Direct method calls (no proxy layer)
- ActorId is a constructor parameter

### 4. State Persistence Migration

#### Orleans: IPersistentState

```csharp
public interface IUserGrain : IGrainWithStringKey
{
    Task<string> GetName();
    Task SetName(string name);
}

public class UserGrain : Grain, IUserGrain
{
    private readonly IPersistentState<UserState> _state;

    public UserGrain(
        [PersistentState("user", "UserStore")] 
        IPersistentState<UserState> state)
    {
        _state = state;
    }

    public Task<string> GetName()
    {
        return Task.FromResult(_state.State.Name);
    }

    public async Task SetName(string name)
    {
        _state.State.Name = name;
        await _state.WriteStateAsync();
    }
}

public class UserState
{
    public string Name { get; set; } = "";
    public DateTime LastModified { get; set; }
}
```

#### Quark: [QuarkState] Properties

```csharp
using Quark.Core.Actors;
using Quark.Core.Persistence;

[Actor(Name = "User")]
public class UserActor : StatefulActorBase
{
    [QuarkState]
    public UserState? State { get; set; }

    public UserActor(string actorId, IStateStorageProvider? storageProvider = null)
        : base(actorId, storageProvider)
    {
    }

    public override async Task OnActivateAsync(CancellationToken cancellationToken = default)
    {
        // Load state automatically
        await base.OnActivateAsync(cancellationToken);
        
        if (State == null)
        {
            State = new UserState();
        }
    }

    public string GetName()
    {
        return State?.Name ?? "";
    }

    public async Task SetNameAsync(string name)
    {
        if (State == null)
            State = new UserState();
            
        State.Name = name;
        State.LastModified = DateTime.UtcNow;
        
        await SaveStateAsync();
    }
}

public class UserState
{
    public string Name { get; set; } = "";
    public DateTime LastModified { get; set; }
}
```

**Key Changes:**
- Use `[QuarkState]` attribute instead of constructor injection
- Inherit from `StatefulActorBase` instead of `Grain`
- Call `SaveStateAsync()` explicitly to persist
- State is loaded automatically in `OnActivateAsync()`
- Storage provider passed via constructor (or DI)

### 5. Storage Configuration Migration

#### Orleans Storage Configuration

```csharp
siloBuilder
    .UseAzureBlobStorage(options =>
    {
        options.ConfigureBlobServiceClient("connection-string");
    })
    .AddAzureTableGrainStorage("UserStore", options =>
    {
        options.ConfigureTableServiceClient("connection-string");
    });
```

#### Quark Storage Configuration

```csharp
using Quark.Storage.Redis;
using Quark.Core.Persistence;
using StackExchange.Redis;

// Setup Redis storage
var redis = ConnectionMultiplexer.Connect("localhost:6379");
var storage = new RedisStateStorage<UserState>(redis);

// Register with provider
var provider = new StateStorageProvider();
provider.RegisterStorage("default", storage);

// Pass to actors
var actor = new UserActor("user-1", provider);
```

**Key Changes:**
- Currently Redis and PostgreSQL storage available
- Direct storage configuration (no builder pattern yet)
- Pass provider to actor constructor or use DI

### 6. Timer and Reminder Migration

#### Orleans Timers

```csharp
public class ReminderGrain : Grain
{
    private IDisposable? _timer;

    public override Task OnActivateAsync(CancellationToken cancellationToken)
    {
        _timer = RegisterTimer(
            callback: OnTimerTick,
            state: null,
            dueTime: TimeSpan.Zero,
            period: TimeSpan.FromMinutes(1));
        
        return base.OnActivateAsync(cancellationToken);
    }

    private Task OnTimerTick(object state)
    {
        // Timer logic
        return Task.CompletedTask;
    }
}
```

#### Quark Timers

```csharp
using Quark.Core.Timers;

[Actor]
public class TimerActor : ActorBase
{
    private readonly ITimerService _timerService;
    private string? _timerId;

    public TimerActor(string actorId, ITimerService timerService)
        : base(actorId)
    {
        _timerService = timerService;
    }

    public override async Task OnActivateAsync(CancellationToken cancellationToken = default)
    {
        _timerId = await _timerService.RegisterTimerAsync(
            actorId: ActorId,
            callback: OnTimerTickAsync,
            dueTime: TimeSpan.Zero,
            period: TimeSpan.FromMinutes(1),
            cancellationToken: cancellationToken);
    }

    private Task OnTimerTickAsync(CancellationToken cancellationToken)
    {
        // Timer logic
        return Task.CompletedTask;
    }

    public override async Task OnDeactivateAsync(CancellationToken cancellationToken = default)
    {
        if (_timerId != null)
        {
            await _timerService.UnregisterTimerAsync(_timerId, cancellationToken);
        }
    }
}
```

#### Orleans Reminders

```csharp
public class ReminderGrain : Grain, IRemindable
{
    public async Task ReceiveReminder(string reminderName, TickStatus status)
    {
        // Handle reminder
    }

    public async Task StartReminder()
    {
        await this.RegisterOrUpdateReminder(
            reminderName: "daily-task",
            dueTime: TimeSpan.FromHours(1),
            period: TimeSpan.FromDays(1));
    }
}
```

#### Quark Reminders

```csharp
using Quark.Core.Reminders;

[Actor]
public class ReminderActor : ActorBase, IReminderTarget
{
    private readonly IReminderService _reminderService;

    public ReminderActor(string actorId, IReminderService reminderService)
        : base(actorId)
    {
        _reminderService = reminderService;
    }

    public async Task StartReminderAsync()
    {
        await _reminderService.RegisterReminderAsync(
            actorId: ActorId,
            reminderName: "daily-task",
            dueTime: TimeSpan.FromHours(1),
            period: TimeSpan.FromDays(1),
            target: this);
    }

    public Task ReceiveReminderAsync(string reminderName, CancellationToken cancellationToken)
    {
        // Handle reminder
        return Task.CompletedTask;
    }
}
```

**Key Changes:**
- Timer/Reminder services injected via constructor
- Explicit registration/unregistration
- Implement `IReminderTarget` for reminders
- More control over lifecycle

### 7. Streaming Migration

#### Orleans Streams

```csharp
public class ProducerGrain : Grain
{
    public async Task PublishAsync(string message)
    {
        var streamProvider = this.GetStreamProvider("StreamProvider");
        var stream = streamProvider.GetStream<string>("my-stream", Guid.Empty);
        
        await stream.OnNextAsync(message);
    }
}

public class ConsumerGrain : Grain
{
    private StreamSubscriptionHandle<string>? _subscription;

    public override async Task OnActivateAsync(CancellationToken cancellationToken)
    {
        var streamProvider = this.GetStreamProvider("StreamProvider");
        var stream = streamProvider.GetStream<string>("my-stream", Guid.Empty);
        
        _subscription = await stream.SubscribeAsync(OnMessageAsync);
        
        await base.OnActivateAsync(cancellationToken);
    }

    private Task OnMessageAsync(string message, StreamSequenceToken token)
    {
        // Process message
        return Task.CompletedTask;
    }
}
```

#### Quark Streams

```csharp
using Quark.Core.Streaming;

[Actor]
public class ProducerActor : ActorBase
{
    private readonly IStreamBroker _streamBroker;

    public ProducerActor(string actorId, IStreamBroker streamBroker)
        : base(actorId)
    {
        _streamBroker = streamBroker;
    }

    public async Task PublishAsync(string message)
    {
        var streamId = new StreamId("my-stream");
        await _streamBroker.PublishAsync(streamId, message);
    }
}

[Actor]
[QuarkStream("my-stream")]
public class ConsumerActor : ActorBase, IStreamConsumer<string>
{
    public ConsumerActor(string actorId) : base(actorId) { }

    public async Task OnStreamMessageAsync(
        string message, 
        StreamId streamId, 
        CancellationToken cancellationToken = default)
    {
        // Process message
        await Task.CompletedTask;
    }
}
```

**Key Changes:**
- Use `[QuarkStream]` attribute for implicit subscriptions
- Implement `IStreamConsumer<T>` interface
- Inject `IStreamBroker` for publishing
- Simpler API with less boilerplate

### 8. Clustering Migration

#### Orleans Clustering

```csharp
siloBuilder
    .UseLocalhostClustering()
    // or
    .UseAzureStorageClustering(options =>
    {
        options.ConfigureTableServiceClient("connection-string");
    })
    // or
    .UseConsulClustering(options =>
    {
        options.Address = new Uri("http://localhost:8500");
    });
```

#### Quark Clustering

```csharp
using Quark.Clustering.Redis;
using StackExchange.Redis;

// Setup Redis cluster membership
var redis = ConnectionMultiplexer.Connect("localhost:6379");
var clusterMembership = new RedisClusterMembership(redis);

// Register current silo
await clusterMembership.RegisterSiloAsync(
    siloId: "silo-1",
    address: "192.168.1.100:5000");

// Discover other silos
var silos = await clusterMembership.GetActiveSilosAsync();
```

**Key Changes:**
- Redis-only clustering (for now)
- Manual silo registration
- Simpler membership protocol
- Consistent hashing for actor placement

### 9. Hosting and Startup Migration

#### Orleans Silo Host

```csharp
var siloHost = new HostBuilder()
    .UseOrleans(siloBuilder =>
    {
        siloBuilder
            .UseLocalhostClustering()
            .ConfigureApplicationParts(parts =>
            {
                parts.AddApplicationPart(typeof(MyGrain).Assembly).WithReferences();
            })
            .AddAzureBlobGrainStorage("storage");
    })
    .Build();

await siloHost.StartAsync();
```

#### Quark Host (Current)

```csharp
using Quark.Core.Actors;
using Quark.Clustering.Redis;

// Setup components
var factory = new ActorFactory();
var redis = ConnectionMultiplexer.Connect("localhost:6379");
var membership = new RedisClusterMembership(redis);

// Register silo
await membership.RegisterSiloAsync("silo-1", "localhost:5000");

// Create and use actors
var actor = factory.CreateActor<MyActor>("actor-1");
await actor.OnActivateAsync();

// Keep running
await Task.Delay(-1);
```

**Note**: Quark's hosting layer (Phase 6) is in progress. The API above shows current manual setup. Future versions will have a builder-based approach similar to Orleans.

### 10. Client Migration

#### Orleans Client

```csharp
var client = new ClientBuilder()
    .UseLocalhostClustering()
    .Build();

await client.Connect();

var grain = client.GetGrain<ICounterGrain>("counter-1");
await grain.Increment();
```

#### Quark Client (Current)

```csharp
using Quark.Core.Actors;

// Direct actor access (no separate client concept yet)
var factory = new ActorFactory();
var actor = factory.CreateActor<CounterActor>("counter-1");

await actor.OnActivateAsync();
actor.Increment();
```

**Note**: Quark's client gateway (Phase 6) is in progress. Currently, actors are accessed directly.

## Common Patterns

### 1. Observer Pattern (Orleans) → Supervision (Quark)

#### Orleans Observer

```csharp
public interface IObserver : IGrainObserver
{
    void Notify(string message);
}

public interface ISubjectGrain : IGrainWithStringKey
{
    Task Subscribe(IObserver observer);
}
```

#### Quark Supervision

```csharp
[Actor]
public class ParentActor : ActorBase, ISupervisor
{
    public ParentActor(string actorId, IActorFactory? actorFactory = null)
        : base(actorId, actorFactory)
    {
    }

    public async Task CreateChildAsync()
    {
        var child = await SpawnChildAsync<ChildActor>("child-1");
        // Parent automatically supervises child
    }

    public override Task<SupervisionDirective> OnChildFailureAsync(
        ChildFailureContext context,
        CancellationToken cancellationToken = default)
    {
        // Handle child failure
        return Task.FromResult(SupervisionDirective.Restart);
    }
}
```

### 2. Re-entrancy

Both frameworks support re-entrant actors:

**Orleans:**
```csharp
[Reentrant]
public class MyGrain : Grain, IMyGrain { }
```

**Quark:**
```csharp
[Actor(Reentrant = true)]
public class MyActor : ActorBase { }
```

### 3. Actor References

**Orleans:** Grains are accessed via interfaces with GrainFactory.

**Quark:** Actors are accessed directly. If you need indirection, use your own interface:

```csharp
public interface ICounter
{
    void Increment();
    int GetCount();
}

[Actor]
public class CounterActor : ActorBase, ICounter
{
    // ... implementation
}

// Usage
ICounter counter = factory.CreateActor<CounterActor>("counter-1");
```

## Data Migration

### Exporting from Orleans

```csharp
// Create a grain that exports all user data
public class DataExportGrain : Grain
{
    private readonly IPersistentState<UserState> _state;

    public async Task<UserState> ExportState()
    {
        await _state.ReadStateAsync();
        return _state.State;
    }
}

// Export script
var users = new List<UserState>();
foreach (var userId in allUserIds)
{
    var grain = grainFactory.GetGrain<DataExportGrain>(userId);
    var state = await grain.ExportState();
    users.Add(state);
}

// Serialize to JSON
var json = JsonSerializer.Serialize(users);
await File.WriteAllTextAsync("users.json", json);
```

### Importing to Quark

```csharp
// Import script
var json = await File.ReadAllTextAsync("users.json");
var users = JsonSerializer.Deserialize<List<UserState>>(json);

var factory = new ActorFactory();
var provider = new StateStorageProvider();
// ... configure storage

foreach (var user in users)
{
    var actor = new UserActor(user.UserId, provider);
    await actor.OnActivateAsync();
    
    // Set state
    actor.State = user;
    
    // Save
    await actor.SaveStateAsync();
    await actor.OnDeactivateAsync();
}
```

## Performance Comparison

### Startup Time

| Framework | Startup Time | Notes |
|-----------|--------------|-------|
| Orleans | ~500ms | JIT compilation, assembly scanning |
| Quark (JIT) | ~200ms | Source generated, less scanning |
| Quark (AOT) | ~50ms | Native code, no compilation |

### Message Throughput

Both frameworks achieve similar throughput (millions of messages/sec), but:
- Quark has lower latency due to lock-free mailbox
- Orleans has more mature optimizations
- Quark benefits more from AOT compilation

### Memory Usage

| Framework | Base Memory | Per Actor | Notes |
|-----------|-------------|-----------|-------|
| Orleans | ~100 MB | ~1-2 KB | Includes runtime overhead |
| Quark (JIT) | ~50 MB | ~500 bytes | Less framework overhead |
| Quark (AOT) | ~30 MB | ~500 bytes | No JIT, smaller runtime |

## Testing Migration

### Unit Tests

Orleans grains need special setup; Quark actors don't:

**Orleans:**
```csharp
// Requires TestCluster or mocking
var cluster = new TestCluster();
await cluster.DeployAsync();
var grain = cluster.GrainFactory.GetGrain<IMyGrain>(Guid.NewGuid());
```

**Quark:**
```csharp
// Direct instantiation
var factory = new ActorFactory();
var actor = factory.CreateActor<MyActor>("test-1");
await actor.OnActivateAsync();
```

### Integration Tests

Both can use Testcontainers for real infrastructure:

```csharp
[Fact]
public async Task ActorPersistsState()
{
    await using var redis = new RedisBuilder().Build();
    await redis.StartAsync();
    
    var connectionString = redis.GetConnectionString();
    // ... test with real Redis
}
```

## Troubleshooting

### "No factory registered for actor type"

**Cause**: Missing source generator reference.

**Solution**: Add generator reference to `.csproj`:
```xml
<ProjectReference Include="path/to/Quark.Generators/Quark.Generators.csproj" 
                  OutputItemType="Analyzer" 
                  ReferenceOutputAssembly="false" />
```

### "Cannot resolve ActorFactory"

**Cause**: Missing DI registration or not passing factory to constructor.

**Solution**: Either use manual factory creation or setup DI properly.

### State not persisting

**Cause**: Not calling `SaveStateAsync()`.

**Solution**: Unlike Orleans' `WriteStateAsync()`, you must explicitly call `SaveStateAsync()`.

## Migration Checklist

- [ ] Add Quark project references and source generator
- [ ] Convert grain interfaces to actor classes with `[Actor]` attribute
- [ ] Update actor activation to use `ActorFactory.CreateActor<T>()`
- [ ] Migrate state from `IPersistentState<T>` to `[QuarkState]` properties
- [ ] Configure storage provider (Redis or PostgreSQL)
- [ ] Update timer/reminder registrations
- [ ] Convert stream subscriptions to `[QuarkStream]` and `IStreamConsumer<T>`
- [ ] Setup Redis clustering
- [ ] Export data from Orleans storage
- [ ] Import data to Quark storage
- [ ] Update unit and integration tests
- [ ] Performance testing and validation
- [ ] Update deployment for Native AOT (optional but recommended)

## Next Steps

- **[Getting Started](Getting-Started)** - Learn Quark basics
- **[Actor Model](Actor-Model)** - Deep dive into Quark actors
- **[Source Generators](Source-Generators)** - Understand AOT compatibility
- **[Examples](Examples)** - See complete examples
- **[FAQ](FAQ)** - Common questions and troubleshooting

---

**Need Help?** Open a [discussion](https://github.com/thnak/Quark/discussions) or [issue](https://github.com/thnak/Quark/issues).
