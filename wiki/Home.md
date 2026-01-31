# Welcome to Quark Framework

**Quark** is a next-generation, high-performance distributed actor framework for .NET 10+ that brings the virtual actor model into the Native AOT era. With **100% reflection-free** operation through compile-time source generation, Quark delivers Orleans-inspired distributed actors with blazing-fast performance and modern .NET capabilities.

> 🎯 **What Makes Quark Different?** Unlike traditional actor frameworks that rely on runtime reflection and IL emission, Quark moves all the "magic" to compile time. Every actor, every proxy, every serializer—generated before your application even starts. The result? Full Native AOT compatibility, faster startup, smaller binaries, and predictable performance.

---

## 🚀 Quick Navigation

### 🎓 Learning Quark
| Guide | Description |
|-------|-------------|
| **[Getting Started](Getting-Started)** | Install Quark, create your first actor in minutes |
| **[Actor Model](Actor-Model)** | Core concepts, lifecycle, and message processing |
| **[Examples](Examples)** | Real-world code samples and common patterns |
| **[API Reference](API-Reference)** | Complete API documentation |

### 🏗️ Building Distributed Systems
| Feature | Guide |
|---------|-------|
| **[Supervision](Supervision)** | Fault tolerance with parent-child hierarchies |
| **[Persistence](Persistence)** | State management with Redis, Postgres, SQL Server, MongoDB, Cassandra, DynamoDB |
| **[Clustering](Clustering)** | Distributed actors with Redis membership and gRPC transport |
| **[Streaming](Streaming)** | Reactive streams with pub/sub and backpressure |
| **[Timers & Reminders](Timers-and-Reminders)** | Scheduling and temporal services |

### 🔧 Advanced Topics
| Topic | Guide |
|-------|-------|
| **[Source Generators](Source-Generators)** | Understanding AOT compilation and code generation |
| **[Migration Guides](Migration-Guides)** | From Akka.NET, Orleans, or between Quark versions |
| **[FAQ](FAQ)** | Troubleshooting and common questions |
| **[Contributing](Contributing)** | Join the Quark community |

---

## ✨ Core Features

### 🚫 **Zero Reflection - 100% Compile-Time Generation**
Every line of framework code is generated at compile time using Roslyn Incremental Source Generators. No runtime reflection, no `Activator.CreateInstance()`, no IL emission—just pure, AOT-friendly code.

### ⚡ **Blazing Performance**
- **SIMD-Accelerated Hashing**: CRC32 hardware intrinsics (10-20x faster than MD5)
- **Lock-Free Messaging**: Zero contention in actor mailboxes
- **Local Call Optimization**: 10-100x lower latency for same-silo calls (eliminates network + serialization overhead)
- **Zero-Allocation Messaging**: Object pooling for TaskCompletionSource and envelopes
- **Incremental Message IDs**: 51x faster than GUID generation
- **Persistent gRPC Streams**: Long-lived connections for minimal latency

### 🎯 **Type-Safe Client Proxies**
```csharp
// Define an actor interface
public interface ICounterActor : IQuarkActor
{
    Task IncrementAsync(int amount);
    Task<int> GetCountAsync();
}

// Get a strongly-typed proxy - NO manual serialization!
var counter = client.GetActor<ICounterActor>("counter-1");
await counter.IncrementAsync(5);
var count = await counter.GetCountAsync(); // Fully type-safe!
```

Quark automatically generates:
- ✅ Protobuf message contracts for parameters and return values
- ✅ Client-side proxy implementations
- ✅ Factory registration for `IClusterClient.GetActor<T>()`
- ✅ Full compile-time type checking and IntelliSense

### 💪 **Stateless Workers**
High-throughput compute actors for stateless operations:
```csharp
[Actor(Name = "ImageProcessor", Stateless = true)]
[StatelessWorker(MinInstances = 2, MaxInstances = 100)]
public class ImageProcessorActor : StatelessActorBase
{
    public async Task<byte[]> ResizeImageAsync(byte[] data, int w, int h)
        => await ProcessImageAsync(data, w, h);
}
```
- ✅ Multiple instances per actor ID for automatic load balancing
- ✅ No state persistence overhead (see examples/Quark.Examples.StatelessWorkers for benchmarks)
- ✅ Minimal activation/deactivation cost

### 🌊 **Reactive Streams**
Publish-subscribe messaging with windowing, backpressure, and stream operators:
```csharp
[QuarkStream(Name = "orders", Namespace = "shop")]
public class OrderActor : ActorBase, IStreamHandler<Order>
{
    public Task OnNextAsync(Order order) { /* process order */ }
}
```

### 🌐 **Production-Ready Clustering**
- **Redis Membership**: Consistent hashing for actor placement
- **gRPC Transport**: Bi-directional streaming with automatic retry
- **Connection Pooling**: Shared connections with health monitoring
- **Multi-Datacenter**: Cassandra replication for global deployments

### 💾 **Multi-Database Persistence**
Choose the right storage backend for your needs:
- **Redis** - Fast in-memory state and reminders
- **Postgres** - Relational data with JSONB state storage
- **SQL Server** - Enterprise integration with retry policies
- **MongoDB** - Document-based flexible schemas
- **Cassandra** - Wide-column, multi-datacenter replication
- **DynamoDB** - Serverless, pay-per-request AWS integration

### 🔍 **Roslyn Analyzers**
Catch errors at compile time:
- **QUARK010**: Detect multiple implementations of `IQuarkActor` interfaces
- **QUARK011**: Warn about deep inheritance chains (>3 levels)

### 🛡️ **Akka-Style Supervision**
Fault tolerance with flexible supervision strategies:
```csharp
public override Task<SupervisionDirective> OnChildFailureAsync(
    ChildFailureContext context, CancellationToken ct)
{
    return context.Exception switch
    {
        TimeoutException => Task.FromResult(SupervisionDirective.Resume),
        OutOfMemoryException => Task.FromResult(SupervisionDirective.Stop),
        _ => Task.FromResult(SupervisionDirective.Restart)
    };
}
```

---

## 📐 Architecture Overview

Quark's modular architecture separates concerns for maximum flexibility:

```
┌──────────────────────────────────────────────────────────────────┐
│                       Your Application                            │
│              (Business Logic + Actor Implementations)             │
└──────────────────────────────────────────────────────────────────┘
                                 ↓
┌──────────────────────────────────────────────────────────────────┐
│                   Quark.Hosting + Quark.Client                    │
│              (Silo Management + Cluster Client Gateway)           │
└──────────────────────────────────────────────────────────────────┘
                                 ↓
┌────────────┬──────────────┬──────────────┬─────────────┬─────────┐
│ Quark.Core │   Streaming  │  Clustering  │ Persistence │  Jobs   │
│  (Actors)  │  (Pub/Sub)   │   (Redis)    │ (Multi-DB)  │ (Queue) │
└────────────┴──────────────┴──────────────┴─────────────┴─────────┘
                                 ↓
┌────────────────────┬────────────────────┬────────────────────────┐
│  Quark.Networking  │ Quark.Abstractions │  Quark.Placement       │
│  (gRPC Transport)  │   (Interfaces)     │  (NUMA, GPU, Locality) │
└────────────────────┴────────────────────┴────────────────────────┘
                                 ↓
┌──────────────────────────────────────────────────────────────────┐
│     Quark.Generators (Roslyn Source Generators + Analyzers)       │
│  (ActorFactory, Proxies, State, Streams, Logging - All AOT-Safe) │
└──────────────────────────────────────────────────────────────────┘
```

### Key Layers
1. **Abstractions**: Core interfaces (`IQuarkActor`, `IStateStorage`, `IClusterClient`)
2. **Core**: Actor runtime, mailboxes, lifecycle, supervision
3. **Clustering**: Redis membership, consistent hashing, silo discovery
4. **Networking**: gRPC transport with persistent streams and connection pooling
5. **Persistence**: State and reminder storage across 6 databases
6. **Streaming**: Reactive streams with implicit/explicit subscriptions
7. **Hosting**: Silo host (`IQuarkSilo`) and cluster client gateway
8. **Generators**: Compile-time code generation for actors, proxies, state, streams, logging
9. **Placement**: Advanced placement strategies (NUMA, GPU acceleration, locality)
10. **Jobs**: Distributed job queue with Redis backend
11. **Event Sourcing**: Journaling support for audit logs and state replay

---

## 🎯 Use Cases

Quark excels in scenarios requiring:

### 🏢 **Enterprise & Microservices**
- Distributed business logic with strong consistency
- Saga pattern for distributed transactions
- Actor-based microservices architecture
- Service mesh integration

### 🎮 **Gaming & Real-Time**
- Player session management (millions of concurrent actors)
- Game world state with virtual actors
- Matchmaking and lobby systems
- Real-time leaderboards

### 🏭 **IoT & Edge Computing**
- Device twin management
- Edge-to-cloud actor distribution
- Lightweight Native AOT deployments
- MQTT integration for device messaging

### 💰 **Financial Services**
- Account management with strong consistency
- Transaction processing with event sourcing
- Portfolio management actors
- Risk calculation engines

### 📊 **Data Processing**
- Stream processing with backpressure
- Event-driven architectures
- ETL pipelines with stateless workers
- Real-time analytics

---

## 📦 Project Structure

Quark contains **48 projects** across core, extensions, storage providers, and examples:

```
Quark/
├── src/
│   ├── Quark.Abstractions/              # Core interfaces
│   ├── Quark.Core.*/                    # Actor runtime, persistence, streaming, timers
│   ├── Quark.Generators/                # Source generators
│   ├── Quark.Analyzers/                 # Roslyn analyzers
│   ├── Quark.Hosting/                   # Silo host
│   ├── Quark.Client/                    # Cluster client
│   ├── Quark.Clustering.Redis/          # Redis membership
│   ├── Quark.Storage.*/                 # Redis, Postgres, SQL Server, MongoDB, Cassandra, DynamoDB
│   ├── Quark.EventSourcing.*/           # Journaling support
│   ├── Quark.Placement.*/               # NUMA, GPU, Locality placement
│   ├── Quark.Jobs.*/                    # Distributed job queue
│   ├── Quark.Messaging.*/               # Inbox/Outbox pattern
│   └── Quark.OpenTelemetry/             # Distributed tracing
├── examples/                             # 25+ example projects
│   ├── Quark.Examples.Basic/
│   ├── Quark.Examples.StatelessWorkers/
│   ├── Quark.Examples.Supervision/
│   ├── Quark.Examples.Streaming/
│   ├── Quark.Examples.Clustering/
│   └── ...
├── tests/Quark.Tests/                   # Comprehensive test suite
├── wiki/                                 # This documentation
└── docs/                                 # Technical deep dives
```

---

## 🧪 Current Status

### ✅ **Production-Ready Features (Phases 1-5 Complete)**

| Phase | Feature | Status |
|-------|---------|--------|
| **Phase 1** | Core Actor Runtime | ✅ Complete |
| | Lifecycle Management (Activate/Deactivate) | ✅ |
| | Supervision Hierarchies | ✅ |
| | Source Generation (ActorSourceGenerator) | ✅ |
| **Phase 2** | Clustering & Networking | ✅ Complete |
| | gRPC Bi-directional Streaming | ✅ |
| | Redis Cluster Membership | ✅ |
| | Consistent Hashing | ✅ |
| | Location Transparency | ✅ |
| **Phase 3** | Reliability & Supervision | ✅ Complete |
| | Call-Chain Reentrancy (Chain IDs) | ✅ |
| | Restart Strategies (OneForOne, AllForOne, RestForOne) | ✅ |
| | Exponential Backoff | ✅ |
| **Phase 4** | Persistence & Temporal Services | ✅ Complete |
| | State Storage Abstractions | ✅ |
| | Redis & Postgres State Storage | ✅ |
| | Persistent Reminders | ✅ |
| | In-Memory Timers | ✅ |
| | Distributed Scheduler | ✅ |
| | E-Tag Optimistic Concurrency | ✅ |
| **Phase 5** | Reactive Streaming | ✅ Complete |
| | Implicit Subscriptions (`[QuarkStream]`) | ✅ |
| | Explicit Pub/Sub (`IQuarkStreamProvider`) | ✅ |
| | Stream-to-Actor Mappings | ✅ |
| | Multiple Subscribers | ✅ |

### 🚀 **Advanced Features (Phases 6-10 - Implemented)**

| Feature Category | Features | Status |
|-----------------|----------|--------|
| **Performance** | SIMD Hash, Lock-Free Mailbox, Local Call Optimization | ✅ |
| **Type Safety** | Protobuf Proxies, `IQuarkActor` Interfaces | ✅ |
| **Stateless** | Stateless Workers, High-Throughput Compute | ✅ |
| **Analyzers** | QUARK010, QUARK011 (Inheritance Analysis) | ✅ |
| **Storage** | SQL Server, MongoDB, Cassandra, DynamoDB | ✅ |
| **Placement** | NUMA Optimization, GPU Acceleration Plugins | ✅ |
| **Jobs** | Distributed Job Queue (Redis) | ✅ |
| **Messaging** | Inbox/Outbox Pattern (Postgres/Redis) | ✅ |
| **Event Sourcing** | Journaling (Postgres/Redis) | ✅ |
| **Observability** | OpenTelemetry Integration | ✅ |

### 📊 **Quality Metrics**
- ✅ **370+ tests passing** (comprehensive test coverage)
- ✅ **CodeQL security scanning** (continuous vulnerability monitoring)
- ✅ **Zero reflection** (100% AOT-compatible)
- ✅ **Production-grade** (multiple storage backends)
- ✅ **48 projects** compiled in parallel
- ✅ **25+ examples** demonstrating features

### 🛠️ **Active Development**
- 🚧 Durable Tasks (Workflow orchestration)
- 🚧 Additional placement strategies
- 🚧 Performance benchmarks and optimization
- 🚧 Documentation expansion

---

## 🎓 Getting Started

Ready to build distributed systems with Quark? Here's a taste:

```csharp
using Quark.Core;
using Quark.Abstractions;

// 1. Define your actor interface (generates Protobuf contracts + proxy)
public interface IGreeterActor : IQuarkActor
{
    Task<string> SayHelloAsync(string name);
}

// 2. Implement the actor
[Actor(Name = "Greeter")]
public class GreeterActor : ActorBase, IGreeterActor
{
    public GreeterActor(string actorId) : base(actorId) { }
    
    public Task<string> SayHelloAsync(string name)
        => Task.FromResult($"Hello, {name}!");
}

// 3. Use it locally or remotely
var factory = new ActorFactory();
var greeter = factory.CreateActor<GreeterActor>("greeter-1");
await greeter.OnActivateAsync();
var message = await greeter.SayHelloAsync("World");
Console.WriteLine(message); // "Hello, World!"

// Or use type-safe remote proxy
var client = serviceProvider.GetRequiredService<IClusterClient>();
var remoteGreeter = client.GetActor<IGreeterActor>("greeter-1");
var remoteMessage = await remoteGreeter.SayHelloAsync("Distributed World");
```

**Next Steps:**
1. 📖 **[Getting Started Guide](Getting-Started)** - Full installation and setup
2. 💡 **[Examples](Examples)** - Complete code samples
3. 🏗️ **[Actor Model](Actor-Model)** - Deep dive into actors

---

## 🤝 Community & Support

### Get Help
- **[FAQ](FAQ)** - Common questions and troubleshooting
- **[GitHub Discussions](https://github.com/thnak/Quark/discussions)** - Ask questions, share ideas
- **[GitHub Issues](https://github.com/thnak/Quark/issues)** - Report bugs or request features

### Contribute
- **[Contributing Guide](Contributing)** - How to contribute code
- **[Migration Guides](Migration-Guides)** - Help improve migration from Akka.NET/Orleans
- **Source Code** - [github.com/thnak/Quark](https://github.com/thnak/Quark)

### Stay Updated
- ⭐ **Star the repo** to follow development
- 👀 **Watch releases** for new versions
- 📢 **Spread the word** - Tell others about Quark!

---

## 📄 License

Quark is open source software licensed under the [MIT License](https://github.com/thnak/Quark/blob/main/LICENSE).

---

## 🏁 Ready to Build?

Choose your path:

| I want to... | Go to... |
|--------------|----------|
| **Create my first actor in 5 minutes** | **[Getting Started](Getting-Started)** |
| **Understand the actor model** | **[Actor Model](Actor-Model)** |
| **See real code examples** | **[Examples](Examples)** |
| **Build distributed systems** | **[Clustering](Clustering)** |
| **Migrate from Akka.NET** | **[Migration from Akka.NET](Migration-from-Akka-NET)** |
| **Migrate from Orleans** | **[Migration from Orleans](Migration-from-Orleans)** |
| **Explore the API** | **[API Reference](API-Reference)** |

---

**Quark Framework** - High-performance distributed actors for the Native AOT era. Build fast, build reliable, build with Quark. 🚀
