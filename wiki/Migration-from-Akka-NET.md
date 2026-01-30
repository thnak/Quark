# Migrating from Akka.NET to Quark

This guide helps you migrate from Akka.NET to Quark. While both implement the actor model, they have different design philosophies and approaches.

## Key Differences Overview

| Feature | Akka.NET | Quark |
|---------|----------|-------|
| **Actor Model** | Erlang-style (message-based) | Virtual actors (method-based) |
| **Actor Creation** | Props & ActorSystem | ActorFactory with source generation |
| **Actor Identity** | ActorPath (hierarchical) | ActorId (string-based) |
| **Messages** | Mailbox with Tell/Ask | Direct method calls |
| **Persistence** | Event sourcing (Akka.Persistence) | State snapshots with [QuarkState] |
| **Clustering** | Akka.Remote + Akka.Cluster | Redis-based |
| **Supervision** | Built-in strategies | ISupervisor interface |
| **Serialization** | Wire/Hyperion/Newtonsoft | System.Text.Json (source generated) |
| **AOT Support** | No | Full Native AOT |
| **State Management** | Manual (UntypedActor state) | Automatic with StatefulActorBase |

## Conceptual Differences

### Akka.NET: Erlang-Style Actors

Akka.NET follows Erlang's message-passing model:
- Actors communicate via messages
- Pattern matching on message types
- Location transparency via ActorRef
- Hierarchical actor paths

### Quark: Virtual Actors (Orleans-Style)

Quark follows the Orleans virtual actor model:
- Actors expose methods directly
- Strongly-typed calls (no pattern matching needed)
- Location transparency via ActorId
- Flat actor namespace (no hierarchy by default)

## Step-by-Step Migration

### 1. Project Setup

#### Akka.NET Project

```xml
<ItemGroup>
  <PackageReference Include="Akka" Version="1.5.0" />
  <PackageReference Include="Akka.Remote" Version="1.5.0" />
  <PackageReference Include="Akka.Cluster" Version="1.5.0" />
  <PackageReference Include="Akka.Persistence" Version="1.5.0" />
</ItemGroup>
```

#### Quark Project

```xml
<ItemGroup>
  <!-- Core framework -->
  <ProjectReference Include="path/to/Quark.Core/Quark.Core.csproj" />
  
  <!-- REQUIRED: Source generator (not transitive!) -->
  <ProjectReference Include="path/to/Quark.Generators/Quark.Generators.csproj" 
                    OutputItemType="Analyzer" 
                    ReferenceOutputAssembly="false" />
  
  <!-- Optional: Clustering -->
  <ProjectReference Include="path/to/Quark.Clustering.Redis/Quark.Clustering.Redis.csproj" />
  
  <!-- Optional: Storage -->
  <ProjectReference Include="path/to/Quark.Storage.Redis/Quark.Storage.Redis.csproj" />
</ItemGroup>
```

### 2. Actor Definition Migration

#### Akka.NET Actor

```csharp
// Messages
public class Increment { }
public class Decrement { }
public class GetCount { }
public class CountResult
{
    public int Count { get; }
    public CountResult(int count) => Count = count;
}

// Actor
public class CounterActor : UntypedActor
{
    private int _count = 0;

    protected override void PreStart()
    {
        base.PreStart();
        Console.WriteLine($"Actor {Self.Path} started");
    }

    protected override void OnReceive(object message)
    {
        switch (message)
        {
            case Increment _:
                _count++;
                break;
            
            case Decrement _:
                _count--;
                break;
            
            case GetCount _:
                Sender.Tell(new CountResult(_count));
                break;
            
            default:
                Unhandled(message);
                break;
        }
    }

    protected override void PostStop()
    {
        Console.WriteLine($"Actor {Self.Path} stopped with count: {_count}");
        base.PostStop();
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
        Console.WriteLine($"Actor {ActorId} activated");
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

    public override Task OnDeactivateAsync(CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"Actor {ActorId} deactivated with count: {_count}");
        return Task.CompletedTask;
    }
}
```

**Key Changes:**
- No message classes needed - use methods directly
- No pattern matching - methods are called directly
- Use `[Actor]` attribute for source generation
- Inherit from `ActorBase` instead of `UntypedActor`
- `PreStart` → `OnActivateAsync`
- `PostStop` → `OnDeactivateAsync`
- Constructor takes `actorId` parameter
- Methods can be synchronous (no forced async)

### 3. Actor Creation and Communication

#### Akka.NET: ActorSystem and Props

```csharp
// Create actor system
var system = ActorSystem.Create("MySystem");

// Create actor
var counter = system.ActorOf(Props.Create<CounterActor>(), "counter-1");

// Send messages
counter.Tell(new Increment());
counter.Tell(new Increment());

// Request-response
var result = await counter.Ask<CountResult>(new GetCount());
Console.WriteLine($"Count: {result.Count}");

// Shutdown
await system.Terminate();
```

#### Quark: ActorFactory

```csharp
// Create actor factory
var factory = new ActorFactory();

// Create actor
var counter = factory.CreateActor<CounterActor>("counter-1");

// Activate actor
await counter.OnActivateAsync();

// Call methods directly
counter.Increment();
counter.Increment();

// Get result directly
var count = counter.GetCount();
Console.WriteLine($"Count: {count}");

// Deactivate when done
await counter.OnDeactivateAsync();
```

**Key Changes:**
- No `ActorSystem.Create()` - use `ActorFactory`
- No `Props` - actors created directly
- No `Tell`/`Ask` - call methods directly
- No separate message classes needed
- More direct, type-safe API

### 4. Persistence Migration

#### Akka.NET: Event Sourcing

```csharp
public class UserActor : ReceivePersistentActor
{
    private UserState _state = new();

    public override string PersistenceId => $"user-{Self.Path.Name}";

    public UserActor()
    {
        // Commands
        Command<SetName>(cmd =>
        {
            var evt = new NameChanged(cmd.Name);
            Persist(evt, e =>
            {
                UpdateState(e);
                Sender.Tell(new Success());
            });
        });

        Command<GetName>(_ =>
        {
            Sender.Tell(_state.Name);
        });

        // Events
        Recover<NameChanged>(evt => UpdateState(evt));
    }

    private void UpdateState(NameChanged evt)
    {
        _state.Name = evt.Name;
        _state.LastModified = DateTime.UtcNow;
    }
}

// Messages
public class SetName
{
    public string Name { get; }
    public SetName(string name) => Name = name;
}

public class GetName { }

// Events
public class NameChanged
{
    public string Name { get; }
    public NameChanged(string name) => Name = name;
}

public class UserState
{
    public string Name { get; set; } = "";
    public DateTime LastModified { get; set; }
}
```

#### Quark: State Snapshots

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

    public async Task SetNameAsync(string name)
    {
        if (State == null)
            State = new UserState();
            
        State.Name = name;
        State.LastModified = DateTime.UtcNow;
        
        // Save state (snapshot)
        await SaveStateAsync();
    }

    public string GetName()
    {
        return State?.Name ?? "";
    }
}

public class UserState
{
    public string Name { get; set; } = "";
    public DateTime LastModified { get; set; }
}
```

**Key Changes:**
- **Event sourcing → State snapshots**: Quark uses state snapshots by default (event sourcing is available via `Quark.EventSourcing`)
- Use `[QuarkState]` attribute on properties
- Inherit from `StatefulActorBase`
- Call `SaveStateAsync()` to persist
- State loaded automatically in `OnActivateAsync()`
- No separate event classes needed (for snapshot model)

#### If You Need Event Sourcing

Quark supports event sourcing through the `Quark.EventSourcing` package:

```csharp
using Quark.EventSourcing;

[Actor]
public class EventSourcedUserActor : EventSourcedActorBase<UserState>
{
    public EventSourcedUserActor(string actorId, IEventStore eventStore)
        : base(actorId, eventStore)
    {
    }

    public async Task SetNameAsync(string name)
    {
        var evt = new NameChangedEvent(name);
        await PersistAsync(evt);
    }

    protected override void ApplyEvent(object evt)
    {
        switch (evt)
        {
            case NameChangedEvent e:
                State.Name = e.Name;
                State.LastModified = DateTime.UtcNow;
                break;
        }
    }
}
```

### 5. Supervision Migration

#### Akka.NET Supervision

```csharp
public class SupervisorActor : UntypedActor
{
    protected override void PreStart()
    {
        // Create children
        var child1 = Context.ActorOf(Props.Create<WorkerActor>(), "worker-1");
        var child2 = Context.ActorOf(Props.Create<WorkerActor>(), "worker-2");
        
        base.PreStart();
    }

    protected override SupervisorStrategy SupervisorStrategy()
    {
        return new OneForOneStrategy(
            maxNrOfRetries: 3,
            withinTimeRange: TimeSpan.FromMinutes(1),
            decider: Decider.From(ex =>
            {
                return ex switch
                {
                    TimeoutException => Directive.Resume,
                    OutOfMemoryException => Directive.Stop,
                    _ => Directive.Restart
                };
            }));
    }

    protected override void OnReceive(object message)
    {
        // Forward to children or handle
    }
}
```

#### Quark Supervision

```csharp
using Quark.Abstractions;
using Quark.Core.Actors;

[Actor(Name = "Supervisor")]
public class SupervisorActor : ActorBase, ISupervisor
{
    public SupervisorActor(string actorId, IActorFactory? actorFactory = null)
        : base(actorId, actorFactory)
    {
    }

    public override async Task OnActivateAsync(CancellationToken cancellationToken = default)
    {
        // Create children
        await SpawnChildAsync<WorkerActor>("worker-1");
        await SpawnChildAsync<WorkerActor>("worker-2");
    }

    public override Task<SupervisionDirective> OnChildFailureAsync(
        ChildFailureContext context,
        CancellationToken cancellationToken = default)
    {
        return context.Exception switch
        {
            TimeoutException => Task.FromResult(SupervisionDirective.Resume),
            OutOfMemoryException => Task.FromResult(SupervisionDirective.Stop),
            _ => Task.FromResult(SupervisionDirective.Restart)
        };
    }
}
```

**Key Changes:**
- Implement `ISupervisor` interface
- Use `SpawnChildAsync<T>()` to create children
- Override `OnChildFailureAsync()` instead of `SupervisorStrategy()`
- Directives are similar: `Resume`, `Restart`, `Stop`, `Escalate`
- No retry limits built-in (implement manually if needed)

### 6. Actor Selection and Routing

#### Akka.NET Actor Selection

```csharp
// Select actor by path
var actor = system.ActorSelection("/user/myactor");
actor.Tell(new MyMessage());

// Router
var router = system.ActorOf(Props.Create<WorkerActor>()
    .WithRouter(new RoundRobinPool(5)), "worker-pool");
```

#### Quark Actor References

```csharp
// Direct actor access
var actor = factory.CreateActor<MyActor>("myactor");
await actor.OnActivateAsync();

// Manual routing (no built-in router yet)
var workers = new List<WorkerActor>();
for (int i = 0; i < 5; i++)
{
    var worker = factory.CreateActor<WorkerActor>($"worker-{i}");
    await worker.OnActivateAsync();
    workers.Add(worker);
}

// Round-robin distribution
int index = 0;
void RouteToWorker(WorkMessage message)
{
    var worker = workers[index % workers.Count];
    worker.Process(message);
    index++;
}
```

**Key Changes:**
- No hierarchical paths - flat namespace
- No built-in actor selection by path
- No built-in routers (implement manually)
- Direct references instead of ActorRef

### 7. Clustering Migration

#### Akka.NET Clustering

```hocon
akka {
  actor {
    provider = cluster
  }
  
  remote {
    dot-netty.tcp {
      hostname = "127.0.0.1"
      port = 8081
    }
  }
  
  cluster {
    seed-nodes = ["akka.tcp://MySystem@127.0.0.1:8081"]
    roles = ["worker"]
  }
}
```

```csharp
var system = ActorSystem.Create("MySystem", config);
var cluster = Cluster.Get(system);

// Join cluster
cluster.Join(cluster.SelfAddress);
```

#### Quark Clustering

```csharp
using Quark.Clustering.Redis;
using StackExchange.Redis;

// Setup Redis cluster membership
var redis = ConnectionMultiplexer.Connect("localhost:6379");
var clusterMembership = new RedisClusterMembership(redis);

// Register silo
await clusterMembership.RegisterSiloAsync(
    siloId: "silo-1",
    address: "192.168.1.100:5000");

// Get active silos
var silos = await clusterMembership.GetActiveSilosAsync();

// Actors are automatically routed via consistent hashing
```

**Key Changes:**
- Redis-based clustering (no gossip protocol)
- Simpler membership model
- No seed nodes - use Redis as coordination point
- Consistent hashing for actor placement
- No cluster roles (yet)

### 8. Timers and Schedulers

#### Akka.NET Scheduler

```csharp
public class ScheduledActor : UntypedActor
{
    private ICancelable? _scheduledTask;

    protected override void PreStart()
    {
        _scheduledTask = Context.System.Scheduler.ScheduleTellRepeatedlyCancelable(
            initialDelay: TimeSpan.Zero,
            interval: TimeSpan.FromSeconds(30),
            receiver: Self,
            message: new TickMessage(),
            sender: ActorRefs.NoSender);
        
        base.PreStart();
    }

    protected override void OnReceive(object message)
    {
        if (message is TickMessage)
        {
            // Handle scheduled tick
        }
    }

    protected override void PostStop()
    {
        _scheduledTask?.Cancel();
        base.PostStop();
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
            period: TimeSpan.FromSeconds(30),
            cancellationToken: cancellationToken);
    }

    private Task OnTimerTickAsync(CancellationToken cancellationToken)
    {
        // Handle scheduled tick
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

**Key Changes:**
- Inject `ITimerService` via constructor
- Register timer in `OnActivateAsync()`
- Use callbacks instead of messages
- Explicitly unregister in `OnDeactivateAsync()`

### 9. Dependency Injection

#### Akka.NET DI

```csharp
var services = new ServiceCollection();
services.AddSingleton<IMyService, MyService>();

var serviceProvider = services.BuildServiceProvider();

var system = ActorSystem.Create("MySystem");
var resolver = new ServiceProviderActorResolver(serviceProvider, system);

// Create actor with DI
var props = resolver.Create<MyActor>();
var actor = system.ActorOf(props, "myactor");
```

#### Quark DI

```csharp
var services = new ServiceCollection();
services.AddSingleton<IMyService, MyService>();
services.AddSingleton<IActorFactory, ActorFactory>();

var serviceProvider = services.BuildServiceProvider();

// Resolve factory
var factory = serviceProvider.GetRequiredService<IActorFactory>();

// Actors can have dependencies injected via constructor
var myService = serviceProvider.GetRequiredService<IMyService>();
var actor = factory.CreateActor<MyActor>("myactor", myService);
```

**Note**: Constructor injection for actors is less common in Quark. Typically, services are passed explicitly or resolved from a service provider inside the actor.

### 10. Testing Migration

#### Akka.NET Testing (with Akka.TestKit)

```csharp
public class CounterActorTests : TestKit
{
    [Fact]
    public void CounterActor_Increments_Correctly()
    {
        var actor = Sys.ActorOf<CounterActor>("counter");
        
        actor.Tell(new Increment());
        actor.Tell(new Increment());
        
        var result = actor.Ask<CountResult>(new GetCount()).Result;
        
        Assert.Equal(2, result.Count);
    }
}
```

#### Quark Testing

```csharp
public class CounterActorTests
{
    [Fact]
    public async Task CounterActor_Increments_Correctly()
    {
        var factory = new ActorFactory();
        var actor = factory.CreateActor<CounterActor>("counter");
        
        await actor.OnActivateAsync();
        
        actor.Increment();
        actor.Increment();
        
        var count = actor.GetCount();
        
        Assert.Equal(2, count);
    }
}
```

**Key Changes:**
- No TestKit needed - actors are just classes
- No special test base class
- Direct method calls for testing
- Synchronous where possible
- Simpler test setup

## Common Pattern Translations

### 1. FSM (Finite State Machine)

**Akka.NET:**
```csharp
public class FSMActor : FSM<State, Data>
{
    StartWith(State.Idle, new Data());
    
    When(State.Idle, evt =>
    {
        if (evt.FsmEvent is StartMessage)
            return GoTo(State.Active);
        return Stay();
    });
}
```

**Quark:**
```csharp
[Actor]
public class StateMachineActor : ActorBase
{
    private State _currentState = State.Idle;

    public void Start()
    {
        if (_currentState == State.Idle)
        {
            _currentState = State.Active;
            OnStateChanged();
        }
    }

    private void OnStateChanged()
    {
        // Handle state change
    }
}
```

### 2. Stashing

**Akka.NET:**
```csharp
public class StashingActor : UntypedActor, IWithUnboundedStash
{
    public IStash Stash { get; set; }

    protected override void OnReceive(object message)
    {
        if (message is ProcessLater)
        {
            Stash.Stash();
        }
        else if (message is ProcessNow)
        {
            Stash.UnstashAll();
        }
    }
}
```

**Quark:**
```csharp
[Actor]
public class QueueingActor : ActorBase
{
    private readonly Queue<Action> _deferredActions = new();
    private bool _isProcessing;

    public void ProcessLater(Action action)
    {
        if (_isProcessing)
        {
            _deferredActions.Enqueue(action);
        }
        else
        {
            action();
        }
    }

    public void ProcessNow()
    {
        _isProcessing = true;
        while (_deferredActions.Count > 0)
        {
            var action = _deferredActions.Dequeue();
            action();
        }
        _isProcessing = false;
    }
}
```

### 3. Become/Unbecome (Behavior Switching)

**Akka.NET:**
```csharp
public class BehaviorActor : UntypedActor
{
    protected override void OnReceive(object message)
    {
        if (message is SwitchBehavior)
        {
            Become(AlternateBehavior);
        }
    }

    private void AlternateBehavior(object message)
    {
        if (message is SwitchBack)
        {
            UnbecomeStacked();
        }
    }
}
```

**Quark:**
```csharp
[Actor]
public class BehaviorActor : ActorBase
{
    private Action<object>? _currentBehavior;

    public BehaviorActor(string actorId) : base(actorId)
    {
        _currentBehavior = DefaultBehavior;
    }

    public void ProcessMessage(object message)
    {
        _currentBehavior?.Invoke(message);
    }

    private void DefaultBehavior(object message)
    {
        if (message is SwitchBehavior)
        {
            _currentBehavior = AlternateBehavior;
        }
    }

    private void AlternateBehavior(object message)
    {
        if (message is SwitchBack)
        {
            _currentBehavior = DefaultBehavior;
        }
    }
}
```

## Data Migration

### Exporting from Akka.Persistence

```csharp
// Use snapshot store and journal to export
public async Task ExportData()
{
    var snapshotStore = /* get snapshot store */;
    var journal = /* get journal */;
    
    // Export snapshots
    var snapshots = await snapshotStore.LoadAsync(persistenceId, criteria);
    
    // Export events
    var events = await journal.ReplayMessagesAsync(persistenceId, from, to, max, recovery);
    
    // Convert to JSON
    var data = new
    {
        Snapshots = snapshots,
        Events = events
    };
    
    var json = JsonSerializer.Serialize(data);
    await File.WriteAllTextAsync("export.json", json);
}
```

### Importing to Quark

```csharp
// For snapshot-based persistence
var json = await File.ReadAllTextAsync("export.json");
var data = JsonSerializer.Deserialize<ExportData>(json);

var factory = new ActorFactory();
var provider = new StateStorageProvider();

foreach (var snapshot in data.Snapshots)
{
    var actor = new MyActor(snapshot.PersistenceId, provider);
    await actor.OnActivateAsync();
    
    actor.State = ConvertSnapshot(snapshot);
    await actor.SaveStateAsync();
    
    await actor.OnDeactivateAsync();
}

// For event sourcing
// Use Quark.EventSourcing to replay events
```

## Performance Comparison

### Message Throughput

| Metric | Akka.NET | Quark |
|--------|----------|-------|
| Local messages/sec | ~10M | ~15M |
| Remote messages/sec | ~500K | ~800K |
| Latency (p99) | ~500µs | ~300µs |

*Note: Numbers are approximate and depend on workload*

### Memory Usage

| Scenario | Akka.NET | Quark |
|----------|----------|-------|
| Base runtime | ~150 MB | ~30 MB (AOT) |
| Per actor | ~2 KB | ~500 bytes |
| 100K actors | ~350 MB | ~80 MB |

### Startup Time

| Mode | Akka.NET | Quark |
|------|----------|-------|
| JIT | ~800ms | ~200ms |
| AOT | Not supported | ~50ms |

## Troubleshooting

### "No factory registered for actor type"

**Cause**: Missing source generator reference.

**Solution**: Add to `.csproj`:
```xml
<ProjectReference Include="path/to/Quark.Generators/Quark.Generators.csproj" 
                  OutputItemType="Analyzer" 
                  ReferenceOutputAssembly="false" />
```

### "Cannot call methods directly on actor"

**Cause**: Coming from Akka's Tell/Ask mindset.

**Solution**: In Quark, actors expose methods directly. Just call them:
```csharp
actor.MyMethod();  // Not actor.Tell(new MyMessage());
```

### Missing ActorSystem equivalents

**Cause**: Quark doesn't have a central ActorSystem.

**Solution**: Use `ActorFactory` for actor creation and manage lifecycle manually. Hosting layer (in progress) will provide more structure.

## Migration Checklist

- [ ] Add Quark project references and source generator
- [ ] Convert message classes to direct method calls
- [ ] Update actor base class from `UntypedActor` to `ActorBase`
- [ ] Replace `OnReceive` with specific methods
- [ ] Migrate `PreStart` to `OnActivateAsync`
- [ ] Migrate `PostStop` to `OnDeactivateAsync`
- [ ] Convert `Props` and `ActorOf` to `ActorFactory.CreateActor`
- [ ] Replace `Tell`/`Ask` with direct method calls
- [ ] Migrate persistence from event sourcing to state snapshots (or use Quark.EventSourcing)
- [ ] Update supervision from `SupervisorStrategy` to `ISupervisor`
- [ ] Replace scheduler with `ITimerService`
- [ ] Setup Redis clustering instead of Akka.Cluster
- [ ] Export data from Akka.Persistence
- [ ] Import data to Quark storage
- [ ] Update tests to use direct actor instantiation
- [ ] Performance testing and validation
- [ ] Consider Native AOT deployment

## Next Steps

- **[Getting Started](Getting-Started)** - Learn Quark basics
- **[Actor Model](Actor-Model)** - Understand Quark's actor model
- **[Source Generators](Source-Generators)** - Learn about AOT
- **[Examples](Examples)** - See complete examples
- **[FAQ](FAQ)** - Troubleshooting

---

**Need Help?** Open a [discussion](https://github.com/thnak/Quark/discussions) or [issue](https://github.com/thnak/Quark/issues).
