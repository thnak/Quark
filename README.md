# Quark

Quark is a high-performance, ultra-lightweight distributed actor framework for .NET, built specifically for the Native AOT era. Unlike traditional frameworks that rely on heavy runtime reflection and IL emission, Quark moves the "magic" to compile-time using Roslyn Incremental Source Generators.

**Key Highlight:** Quark is **100% reflection-free** - all code generation happens at compile-time, making it fully Native AOT compatible.

## Features

### Core Framework
- ✨ **Native AOT Ready**: Full support for .NET Native AOT compilation
- 🚫 **Zero Reflection**: 100% reflection-free - all code generated at compile-time
- 🏗️ **Orleans-inspired**: Familiar virtual actor model with modern AOT support
- 🎯 **.NET 10 Target**: Built for the latest .NET platform
- ⚡ **Parallel Build**: Multi-project structure optimized for parallel compilation

### Performance
- 🚀 **High Performance**: Lock-free messaging, persistent gRPC streams, zero-allocation messaging
  - Object pooling for TaskCompletionSource and messages
  - Incremental message IDs (51x faster than GUID)
  - 44.5% memory reduction in hot paths
  - Local call optimization: Automatic detection and optimization of same-silo calls
  - SIMD-accelerated hash computations (AVX2 support)

### Source Generation
- 🔧 **Compile-time Code Generation**: Full AOT compatibility
  - Actor factories with automatic registration
  - Type-safe client proxies with Protobuf contracts
  - Context-based proxy registration (like JsonSerializerContext)
  - State persistence (Load/Save/Delete for [QuarkState] properties)
  - Stream dispatchers for reactive messaging
  - JSON serialization (JsonSerializerContext)
  - High-performance logging (LoggerMessage pattern)

### Actor Model
- 🎭 **Virtual Actors**: Location-transparent distributed actors
- 💪 **Stateless Workers**: High-throughput compute actors
  - Multiple instances per actor ID
  - No state persistence overhead
  - Automatic load balancing (~2000 ops/sec)
- 👨‍👩‍👧‍👦 **Supervision Hierarchies**: Parent-child relationships with failure handling
  - Supervision directives (Resume, Restart, Stop, Escalate)
  - Multiple restart strategies (OneForOne, AllForOne, RestForOne)
  - Exponential backoff support

### Distributed Features
- 🌐 **Clustering**: Redis-based cluster membership with consistent hashing
- 🔌 **Connection Optimization**: Intelligent pooling and sharing
  - Shared Redis connections across components
  - gRPC channel pooling with automatic lifecycle management
  - Health monitoring and automatic recovery
  - Zero-copy for co-hosted scenarios
- 🔄 **Reactive Streaming**: Publish-subscribe messaging
  - Implicit subscriptions with [QuarkStream] attribute
  - Explicit pub/sub with IQuarkStreamProvider
  - Multiple subscribers support
  - Backpressure control
- ⏰ **Temporal Services**: Timers and reminders
  - Lightweight in-memory timers
  - Persistent reminders that survive reboots
  - Distributed scheduler with consistent hashing

### Persistence & Storage
- 💾 **Multi-Database Support**: Pluggable state storage
  - Redis, PostgreSQL, SQL Server
  - MongoDB, Cassandra, DynamoDB
  - In-memory storage for development/testing
  - E-Tag/optimistic concurrency control
- 📜 **Event Sourcing**: Native journaling support
  - Redis and PostgreSQL implementations
  - Audit logs and state replay

### Advanced Features
- 🎯 **Actor Queries**: LINQ-style actor discovery and querying
- 🔀 **Sagas**: Long-running distributed transactions
- 📨 **Messaging Patterns**: Inbox/Outbox pattern support
  - Redis and PostgreSQL implementations
  - At-least-once delivery guarantees
- ⚡ **Durable Jobs**: Redis-backed job queues
  - Persistent job state
  - Retry policies and dead-letter queues
- 🗺️ **Advanced Placement**: Hardware-aware actor placement
  - Memory-based placement (NUMA-aware)
  - Locality-based placement (network topology)
  - GPU placement (CUDA support)
  - Platform-specific optimizations (Linux/Windows)

### Developer Experience
- 🔍 **Roslyn Analyzers**: Compile-time diagnostics
  - Detect multiple IQuarkActor interface implementations
  - Warn about deep inheritance chains
  - Parameter serializability checks
  - Reentrancy detection
  - Method signature validation
- 📊 **Observability**: Production monitoring
  - OpenTelemetry integration
  - Performance profiling (Linux/Windows)
  - Profiling dashboard
  - Load testing tools
  - Health monitoring
- 📚 **Comprehensive Documentation**: Wiki, guides, and examples

## Project Structure

The Quark framework is organized into focused, composable packages:

### Core Framework (`src/`)
- **Abstractions**: `Quark.Abstractions`, `Quark.Networking.Abstractions`
- **Core Runtime**: `Quark.Core`, `Quark.Core.Actors`, `Quark.Core.Persistence`
- **Temporal Services**: `Quark.Core.Timers`, `Quark.Core.Reminders`
- **Reactive Streaming**: `Quark.Core.Streaming`
- **Source Generators**: `Quark.Generators`, `Quark.Generators.Logging`
- **Analyzers**: `Quark.Analyzers`, `Quark.Analyzers.CodeFixes`

### Distributed Systems (`src/`)
- **Networking**: `Quark.Transport.Grpc`, `Quark.Clustering.Redis`
- **Client**: `Quark.Client`
- **Hosting**: `Quark.Hosting`, `Quark.Extensions.DependencyInjection`

### Persistence (`src/`)
- **Storage Providers**: Redis, PostgreSQL, SQL Server, MongoDB, Cassandra, DynamoDB
- **Event Sourcing**: `Quark.EventSourcing` (Redis, PostgreSQL)
- **Messaging**: `Quark.Messaging` (Inbox/Outbox pattern - Redis, PostgreSQL)
- **Jobs**: `Quark.Jobs`, `Quark.Jobs.Redis`

### Advanced Features (`src/`)
- **Sagas**: `Quark.Sagas` - Long-running distributed transactions
- **Queries**: `Quark.Queries` - Actor discovery and querying
- **Placement**: Memory, Locality, NUMA (Linux/Windows), GPU (CUDA)
- **Observability**: `Quark.OpenTelemetry`, Profiling (Linux/Windows, Dashboard, Load Testing)

### Examples (`examples/`)
- **Getting Started**: Basic, Supervision, Streaming, StatelessWorkers
- **Advanced**: Sagas, ActorQueries, Placement, Backpressure, DeadLetterQueue
- **Performance**: Performance, MassiveScale, ZeroAllocation, Profiling
- **Production Demos**: PizzaTracker (API + Console), PizzaDash (Full-stack demo)
- **Integration**: ContextRegistration, ReactiveActors, Serverless

### Tests
- `tests/Quark.Tests/` - Comprehensive test suite with xUnit

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

### Stateless Workers

Quark supports **stateless workers** - lightweight compute actors optimized for high-throughput processing without state persistence overhead. Multiple instances can be created with the same actor ID for automatic load balancing.

```csharp
[Actor(Name = "ImageProcessor", Stateless = true)]
[StatelessWorker(MinInstances = 2, MaxInstances = 100)]
public class ImageProcessorActor : StatelessActorBase
{
    public ImageProcessorActor(string actorId) : base(actorId) { }

    public async Task<byte[]> ResizeImageAsync(byte[] imageData, int width, int height)
    {
        // Stateless computation - no state persistence
        await Task.Delay(50); // Simulate processing
        return ProcessImage(imageData, width, height);
    }
}

// Create multiple instances with the same ID
var worker1 = factory.CreateActor<ImageProcessorActor>("image-processor");
var worker2 = factory.CreateActor<ImageProcessorActor>("image-processor");
var worker3 = factory.CreateActor<ImageProcessorActor>("image-processor");

// Process concurrently across multiple workers
var task1 = worker1.ResizeImageAsync(imageData1, 800, 600);
var task2 = worker2.ResizeImageAsync(imageData2, 1024, 768);
var task3 = worker3.ResizeImageAsync(imageData3, 1920, 1080);

var results = await Task.WhenAll(task1, task2, task3);
```

**Key Benefits:**
- ✅ No state persistence overhead
- ✅ Multiple instances per actor ID
- ✅ High-throughput concurrent processing (~2000 ops/sec in benchmarks)
- ✅ Minimal activation/deactivation cost
- ✅ Automatic load distribution

**Use Cases:**
- Image processing and transformation
- Data validation and enrichment
- API aggregation and proxying
- Stateless computations and transformations

See the `examples/Quark.Examples.StatelessWorkers` project for complete examples.

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

## Documentation

### Essential Guides (`docs/`)
- **[Virtual Actor Principles](docs/VIRTUAL_ACTOR_PRINCIPLES.md)** - Understanding proxies and avoiding reference leaks
- **[Source Generator Setup](docs/SOURCE_GENERATOR_SETUP.md)** - Critical setup guide for using Quark actors
- **[Zero Reflection Achievement](docs/ZERO_REFLECTION_ACHIEVEMENT.md)** - How we achieved 100% reflection-free operation
- **[Quick Reference](docs/QUICK_REFERENCE.md)** - Quick API reference

### Advanced Features (`docs/`)
- **[Streaming](docs/STREAMING.md)** - Reactive streaming and pub/sub patterns
- **[Backpressure](docs/BACKPRESSURE.md)** - Flow control and reactive streaming
- **[Type-Safe Proxies](docs/TYPE_SAFE_PROXIES.md)** - Client proxy generation for remote actors
- **[Local Call Optimization](docs/LOCAL_CALL_OPTIMIZATION.md)** - Automatic same-silo call optimization
- **[Connection Optimization](docs/CONNECTION_OPTIMIZATION.md)** - Connection pooling and sharing
- **[Advanced Placement](docs/ADVANCED_PLACEMENT_EXAMPLE.md)** - Hardware-aware actor placement

### Operations & Production (`docs/`)
- **[Database Integrations](docs/DATABASE_INTEGRATIONS_GUIDE.md)** - Multi-database storage support
- **[Cluster Health Monitoring](docs/CLUSTER_HEALTH_MONITORING_GUIDE.md)** - Production monitoring
- **[Dead Letter Queue Usage](docs/DLQ_USAGE_GUIDE.md)** - Handling failed messages

### Wiki
The `wiki/` directory contains comprehensive guides:
- **Getting Started** - Installation and first steps
- **Actor Model** - Core concepts and patterns
- **Supervision** - Failure handling and hierarchies
- **Persistence** - State management
- **Clustering** - Distributed deployment
- **Timers and Reminders** - Temporal services
- **Streaming** - Reactive messaging
- **Source Generators** - Understanding code generation
- **Migration Guides** - From Orleans and Akka.NET
- **API Reference** - Complete API documentation

## License

MIT License - see LICENSE file for details

## Contributing

Contributions are welcome! Please feel free to submit issues and pull requests.

See the `wiki/Contributing.md` for guidelines.

## Implementation Status

Quark is feature-complete and production-ready with the following implemented capabilities:

### ✅ Core Actor System
- Virtual actor model with location transparency
- Lifecycle management (OnActivateAsync/OnDeactivateAsync)
- Supervision hierarchies with failure handling strategies
- Stateless workers for high-throughput compute
- Turn-based mailbox with reentrancy control

### ✅ Distributed Systems
- gRPC-based transport with bi-directional streaming
- Redis-based cluster membership
- Consistent hashing for actor placement
- Local call optimization (same-silo)
- Connection pooling and sharing

### ✅ State Management
- Multi-database storage (Redis, PostgreSQL, SQL Server, MongoDB, Cassandra, DynamoDB)
- Optimistic concurrency (E-Tag support)
- Event sourcing with Redis and PostgreSQL
- Inbox/Outbox messaging pattern
- In-memory storage for development

### ✅ Temporal Services
- Lightweight in-memory timers
- Persistent reminders with distributed scheduling
- Consistent hashing for reminder distribution

### ✅ Reactive Streaming
- Implicit subscriptions with [QuarkStream] attribute
- Explicit pub/sub with IQuarkStreamProvider
- Multiple subscribers support
- Backpressure control
- Stream-to-actor dispatching

### ✅ Advanced Features
- Sagas for distributed transactions
- Actor queries (LINQ-style discovery)
- Durable jobs with Redis backend
- Dead-letter queues for failed messages
- Hardware-aware placement (Memory, NUMA, Locality, GPU/CUDA)

### ✅ Developer Experience
- 100% Native AOT compatible (zero reflection)
- Compile-time code generation for all dynamic behavior
- Type-safe client proxies with Protobuf
- Roslyn analyzers for best practices
- Comprehensive test coverage
- Production monitoring (OpenTelemetry, profiling)

### 📊 Current Statistics
- **52 projects** across core framework, storage providers, and placement strategies
- **24+ examples** demonstrating various patterns and use cases
- **Comprehensive test suite** with xUnit
- **Full wiki documentation** with guides and API reference

