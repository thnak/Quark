# Quark

Quark is a high-performance, ultra-lightweight distributed actor framework for .NET, built specifically for the Native AOT era. Unlike traditional frameworks that rely on heavy runtime reflection and IL emission, Quark moves the "magic" to compile-time using Roslyn Incremental Source Generators.

**Key Highlight:** Quark is **100% reflection-free** - all code generation happens at compile-time, making it fully Native AOT compatible.

## Features

- ✨ **Native AOT Ready**: Full support for .NET Native AOT compilation
- 🚫 **Zero Reflection**: 100% reflection-free - all code generated at compile-time
- 🚀 **High Performance**: Lock-free messaging, persistent gRPC streams
- 🔧 **Source Generation**: Compile-time code generation for AOT compatibility
  - Actor factories
  - **Type-safe client proxies with Protobuf contracts** 🆕
  - JSON serialization (JsonSerializerContext)
  - High-performance logging (LoggerMessage)
- 🏗️ **Orleans-inspired**: Familiar actor model with modern AOT support
- 🌐 **Distributed**: Redis clustering with consistent hashing
- 🔌 **Connection Optimization**: Intelligent connection pooling and sharing
  - Shared Redis connections across components
  - gRPC channel pooling with automatic lifecycle management
  - Health monitoring and automatic recovery
  - Zero-copy for co-hosted scenarios
- ⚡ **Parallel Build**: Multi-project structure optimized for parallel compilation
- 🎯 **.NET 10 Target**: Built for the latest .NET platform

## Project Structure

```
Quark/
├── src/
│   ├── Quark.Core/              # Main actor framework library
│   │   ├── IActor.cs            # Actor interface
│   │   ├── ActorBase.cs         # Base actor implementation
│   │   ├── ActorFactory.cs      # Actor factory for creating instances
│   │   └── IActorFactory.cs     # Factory interface
│   │
│   └── Quark.SourceGenerator/   # Roslyn source generator
│       └── ActorSourceGenerator.cs
│
├── tests/
│   └── Quark.Tests/             # xUnit tests
│       └── ActorFactoryTests.cs
│
├── examples/
│   └── Quark.Examples.Basic/    # Basic usage example
│       └── Program.cs
│
├── Directory.Build.props         # Shared MSBuild properties
└── Quark.slnx                   # Solution file
```

## Getting Started

### Prerequisites

- .NET 10 SDK or later
- A C# IDE (Visual Studio, VS Code, or Rider)
- **CPU with AVX2 support** (Intel Haswell 2013+, AMD Excavator 2015+)
  - Required for SIMD-accelerated hash computations and hot path optimizations
  - Most modern x64 CPUs support AVX2 (check with `lscpu | grep avx2` on Linux)

### Project Setup

⚠️ **Important**: When creating a project that uses Quark, you must explicitly reference the source generator:

```xml
<ItemGroup>
  <ProjectReference Include="path/to/Quark.Core/Quark.Core.csproj" />
  <!-- REQUIRED: Source generator reference (not transitive) -->
  <ProjectReference Include="path/to/Quark.Generators/Quark.Generators.csproj" 
                    OutputItemType="Analyzer" 
                    ReferenceOutputAssembly="false" />
</ItemGroup>
```

See [SOURCE_GENERATOR_SETUP.md](docs/SOURCE_GENERATOR_SETUP.md) for detailed information.

### Building

```bash
# Restore dependencies
dotnet restore

# Build all projects (with parallel build enabled)
dotnet build -maxcpucount

# Run tests
dotnet test

# Run the example
dotnet run --project examples/Quark.Examples.Basic/Quark.Examples.Basic.csproj
```

### Publishing with AOT

```bash
# Publish with Native AOT
cd examples/Quark.Examples.Basic
dotnet publish -c Release -r linux-x64 --self-contained
```

## Quick Example

```csharp
using Quark.Core;

// Create an actor factory
var factory = new ActorFactory();

// Create a counter actor
var counter = factory.CreateActor<CounterActor>("counter-1");

// Activate the actor
await counter.OnActivateAsync();

// Use the actor
counter.Increment();
Console.WriteLine($"Counter value: {counter.GetValue()}");

// Deactivate when done
await counter.OnDeactivateAsync();

// Define your actor
[Actor(Name = "Counter", Reentrant = false)]
public class CounterActor : ActorBase
{
    private int _counter;

    public CounterActor(string actorId) : base(actorId)
    {
        _counter = 0;
    }

    public void Increment() => _counter++;
    public int GetValue() => _counter;
}
```

## Type-Safe Client Proxies

Quark supports type-safe client proxies for remote actor invocation with full AOT compatibility. Define an actor interface inheriting from `IQuarkActor`, and Quark will automatically generate Protobuf message contracts and client-side proxy implementations at compile-time.

### Defining an Actor Interface

```csharp
using Quark.Abstractions;

// Define your actor interface
public interface ICounterActor : IQuarkActor
{
    Task IncrementAsync(int amount);
    Task<int> GetCountAsync();
    Task ResetAsync();
}
```

### Using Type-Safe Proxies

```csharp
using Quark.Client;

// Connect to the cluster
var client = serviceProvider.GetRequiredService<IClusterClient>();
await client.ConnectAsync();

// Get a type-safe proxy for the actor
var counter = client.GetActor<ICounterActor>("counter-1");

// Make strongly-typed calls - no manual envelope construction!
await counter.IncrementAsync(5);
var count = await counter.GetCountAsync();
Console.WriteLine($"Counter value: {count}");
await counter.ResetAsync();
```

### What Gets Generated

For each `IQuarkActor` interface, the source generator creates:

1. **Protobuf Message Contracts** - Request/response messages for each method:
   ```csharp
   [ProtoContract]
   public struct IncrementAsyncRequest
   {
       [ProtoMember(1)] public int Amount { get; set; }
   }
   
   [ProtoContract]
   public struct GetCountAsyncResponse
   {
       [ProtoMember(1)] public int Result { get; set; }
   }
   ```

2. **Client-Side Proxy** - A proxy class that implements your interface:
   ```csharp
   internal sealed class ICounterActorProxy : ICounterActor
   {
       // Serializes parameters to Protobuf, creates QuarkEnvelope,
       // sends via IClusterClient, and deserializes the response
   }
   ```

3. **Factory Registration** - Automatic registration in `ActorProxyFactory`:
   ```csharp
   if (typeof(TActorInterface) == typeof(ICounterActor))
       return (TActorInterface)(object)new ICounterActorProxy(client, actorId);
   ```

### Benefits

- **Type Safety**: Full compile-time type checking and IntelliSense support
- **Zero Reflection**: All code generated at compile-time for Native AOT compatibility
- **Efficient Serialization**: Protobuf binary serialization for optimal network performance
- **Developer Experience**: Clean, strongly-typed API - no manual envelope construction
- **Backward Compatible**: Uses existing QuarkEnvelope transport protocol

### Actor Implementation

The server-side actor implementation is straightforward:

```csharp
[Actor(Name = "Counter")]
public class CounterActor : ActorBase, ICounterActor
{
    private int _count;

    public CounterActor(string actorId) : base(actorId) { }

    public Task IncrementAsync(int amount)
    {
        _count += amount;
        return Task.CompletedTask;
    }

    public Task<int> GetCountAsync()
    {
        return Task.FromResult(_count);
    }

    public Task ResetAsync()
    {
        _count = 0;
        return Task.CompletedTask;
    }
}
```

## Architecture

### Core Components

- **IActor**: Base interface for all actors
- **ISupervisor**: Interface for actors that can supervise child actors
- **ActorBase**: Abstract base class providing common actor functionality and supervision support
- **ActorFactory**: Factory for creating and managing actor instances
- **ActorAttribute**: Marks classes for source generation
- **SupervisionDirective**: Enum defining how to handle child actor failures (Resume, Restart, Stop, Escalate)
- **ChildFailureContext**: Context information about a child actor failure

### Supervision Hierarchy

Quark supports Akka-style supervision hierarchies within the virtual actor model:

```csharp
// Create a supervisor that can manage child actors
var factory = new ActorFactory();
var supervisor = factory.CreateActor<SupervisorActor>("supervisor-1");

// Spawn child actors under the supervisor
var child = await supervisor.SpawnChildAsync<WorkerActor>("worker-1");

// Get all children
var children = supervisor.GetChildren();

// Handle child failures with custom strategies
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
```

See the `examples/Quark.Examples.Supervision` project for a complete example.

### Source Generators

The `Quark.Generators` project contains Roslyn incremental source generators that provide compile-time code generation:

1. **ActorSourceGenerator**: 
   - Detects classes marked with `[Actor]` attribute
   - Generates AOT-friendly factory methods at compile-time
   - Eliminates runtime reflection for Native AOT compatibility

2. **ProxySourceGenerator**: 🆕
   - Detects interfaces inheriting from `IQuarkActor`
   - Generates Protobuf message contracts for method parameters and return values
   - Generates type-safe client proxy implementations
   - Registers proxies in `ActorProxyFactory` for `IClusterClient.GetActor<T>()`

3. **StateSourceGenerator**:
   - Generates state persistence code for properties marked with `[QuarkState]`
   - Provides AOT-compatible serialization/deserialization

4. **StreamSourceGenerator**:
   - Generates reactive stream dispatchers for actors with `[QuarkStream]` attributes
   - Enables publish-subscribe messaging patterns

## Configuration

### Parallel Build

The solution is configured for parallel builds via `Directory.Build.props`:

```xml
<BuildInParallel>true</BuildInParallel>
```

### AOT Support

Projects marked with `IsPublishable=true` automatically get:
- `PublishAot=true`
- Optimized for speed
- Minimal stack trace data for smaller binaries

## Testing

The project uses xUnit for testing. All tests are in the `Quark.Tests` project:

```bash
# Run all tests
dotnet test

# Run with verbose output
dotnet test -v normal
```

## License

MIT License - see LICENSE file for details

## Contributing

Contributions are welcome! Please feel free to submit issues and pull requests.

## Roadmap

- [x] **Phase 1: Core Actor Abstractions** - Lifecycle management and supervision hierarchies
  - [x] OnActivateAsync and OnDeactivateAsync lifecycle methods
  - [x] Supervision directives (Resume, Restart, Stop, Escalate)
  - [x] Parent-child actor relationships with SpawnChildAsync
  - [x] Child failure handling with OnChildFailureAsync
  - [x] GetChildren for accessing supervised actors
- [x] **Phase 2: Cluster & Networking** - gRPC transport and distributed actors
  - [x] Bi-directional gRPC streaming
  - [x] Consistent hashing for actor placement
  - [x] Redis-based cluster membership
  - [x] Location transparency with routing
- [x] **Phase 3: Reliability & Supervision** - Advanced failure handling
  - [x] Call-chain reentrancy with Chain IDs
  - [x] Restart strategies (OneForOne, AllForOne, RestForOne)
  - [x] Configurable supervision with exponential backoff
- [x] **Phase 4: Persistence & Temporal Services** - State durability and timers
  - [x] Production-grade state generator with JsonSerializerContext
  - [x] E-Tag/optimistic concurrency for state management
  - [x] Persistent reminders that survive reboots
  - [x] Distributed scheduler with consistent hashing
  - [x] Lightweight in-memory timers
  - [x] State storage abstractions (IStateStorage with optimistic concurrency)
  - [x] Reminder storage abstractions (IReminderTable with consistent hashing)
  - [x] InMemoryStateStorage implementation for development/testing
  - [x] InMemoryReminderTable implementation for development/testing
  - [ ] State Providers: Redis and Postgres storage with optimistic concurrency (deferred)
  - [ ] Reminder Storage: Redis and Postgres reminder tables (deferred)
  - [ ] Event Sourcing: Native journaling support for audit-logs and state replay (deferred)
- [x] **Phase 5: Reactive Streaming** - Decoupled messaging patterns
  - [x] Implicit subscriptions with `[QuarkStream]` attribute
  - [x] Explicit pub/sub with `IQuarkStreamProvider`
  - [x] Stream-to-actor mappings via source generator
  - [x] Analyzer for compile-time validation
  - [x] Multiple subscribers support
- [ ] **Phase 6: Silo Host & Client Gateway** - Production-ready hosting (planning)
  - [ ] IQuarkSilo host with lifecycle orchestration
  - [ ] IClusterClient lightweight gateway
  - [ ] IServiceCollection extensions for DI registration
  - [ ] Actor method signature analyzers
  - [ ] Protobuf proxy generation
- [ ] Performance benchmarks and optimization

