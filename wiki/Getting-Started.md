# Getting Started with Quark

Welcome! This guide will take you from zero to your first working actor in minutes. Quark is a high-performance, 100% reflection-free distributed actor framework designed for Native AOT compatibility.

> **What makes Quark different?** Unlike traditional actor frameworks that use heavy runtime reflection, Quark moves all "magic" to compile-time using Roslyn source generators. This means faster startup, smaller binaries, and full Native AOT support.

## Prerequisites

Before you start, ensure you have:

- **.NET 10 SDK** ([Download](https://dotnet.microsoft.com/download))
- **IDE**: Visual Studio 2022, VS Code with C# extension, or JetBrains Rider
- **Docker Desktop** (required for running tests with Redis)
- **CPU with AVX2 support** (recommended for SIMD optimizations)
  - Intel Haswell (2013+) or AMD Excavator (2015+) or newer
  - Check on Linux: `lscpu | grep avx2`
  - Check on Windows: Use CPU-Z or similar tool

> 💡 **New to actors?** Don't worry! This guide assumes no prior knowledge. We'll explain concepts as we go.

## Quick Start (5 minutes)

The fastest way to see Quark in action:

```bash
# Clone the repository
git clone https://github.com/thnak/Quark.git
cd Quark

# Restore dependencies
dotnet restore

# Build the framework (uses parallel compilation)
dotnet build -maxcpucount

# Run the basic example
dotnet run --project examples/Quark.Examples.Basic
```

**Expected output:**
```
=== Quark Actor Framework - Basic Example ===

✓ Actor factory created
✓ Counter actor created with ID: counter-1
✓ Actor activated
✓ Counter incremented to: 1
✓ Counter incremented to: 3
✓ Message processed: Actor counter-1 received: Hello from Quark!
✓ GetOrCreate returned same instance: True
✓ Actor deactivated

=== Example completed successfully ===
```

✅ **Success!** You just ran your first Quark actor. Now let's build one from scratch.

---

## Project Setup

### Creating a New Project

Create a new console application:

```bash
mkdir MyQuarkApp
cd MyQuarkApp
dotnet new console -f net10.0
```

### Adding Quark References

⚠️ **CRITICAL**: The source generator reference is **NOT transitive**. You must explicitly add it to every project that defines actors.

Edit your `.csproj` file to include both `Quark.Core` and `Quark.Generators`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <!-- Optional: Enable AOT publishing -->
    <PublishAot>true</PublishAot>
  </PropertyGroup>

  <ItemGroup>
    <!-- Reference the core framework -->
    <ProjectReference Include="../Quark/src/Quark.Core/Quark.Core.csproj" />
    
    <!-- REQUIRED: Explicit generator reference for compile-time code generation -->
    <ProjectReference Include="../Quark/src/Quark.Generators/Quark.Generators.csproj" 
                      OutputItemType="Analyzer" 
                      ReferenceOutputAssembly="false" />
    
    <!-- Optional: Roslyn analyzers for best practices -->
    <ProjectReference Include="../Quark/src/Quark.Analyzers/Quark.Analyzers.csproj" 
                      OutputItemType="Analyzer" 
                      ReferenceOutputAssembly="false" />
  </ItemGroup>

</Project>
```

> 📚 **Why explicit generator references?** Roslyn analyzers and source generators don't propagate through project references. Even though `Quark.Core` uses the generator internally, consuming projects must reference it explicitly. See [docs/SOURCE_GENERATOR_SETUP.md](../docs/SOURCE_GENERATOR_SETUP.md) for details.

### Key Project Settings Explained

| Setting | Purpose |
|---------|---------|
| `OutputItemType="Analyzer"` | Tells MSBuild to use this as a compile-time analyzer |
| `ReferenceOutputAssembly="false"` | Generator code doesn't need to be in final assembly |
| `PublishAot="true"` | Enables Native AOT compilation (optional but recommended) |

---

## Your First Actor

Let's create a simple counter actor that demonstrates the core concepts.

### Step 1: Define the Actor Class

Create a new file `CounterActor.cs`:

```csharp
using Quark.Abstractions;
using Quark.Core.Actors;

namespace MyQuarkApp;

/// <summary>
/// A simple counter actor that demonstrates basic actor functionality.
/// Actors process operations sequentially, providing thread-safe state management.
/// </summary>
[Actor(Name = "Counter", Reentrant = false)]
public class CounterActor : ActorBase
{
    private int _counter;

    // Constructor receives the unique actor ID
    public CounterActor(string actorId) : base(actorId)
    {
        _counter = 0;
    }

    // Public methods become the actor's "interface"
    public void Increment()
    {
        _counter++;
    }

    public void IncrementBy(int amount)
    {
        _counter += amount;
    }

    public int GetValue()
    {
        return _counter;
    }

    // Async operations are fully supported
    public async Task<string> ProcessMessageAsync(string message)
    {
        // Simulate some async work (database call, API request, etc.)
        await Task.Delay(100);
        return $"Actor {ActorId} received: {message}";
    }

    // Lifecycle hook: Called when actor is activated
    public override Task OnActivateAsync(CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"→ CounterActor '{ActorId}' is activating (count: {_counter})");
        return base.OnActivateAsync(cancellationToken);
    }

    // Lifecycle hook: Called when actor is deactivated
    public override Task OnDeactivateAsync(CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"→ CounterActor '{ActorId}' is deactivating (final count: {_counter})");
        return base.OnDeactivateAsync(cancellationToken);
    }
}
```

### Understanding the Actor Attribute

The `[Actor]` attribute marks your class for source generation:

```csharp
[Actor(Name = "Counter", Reentrant = false)]
```

**Attribute Parameters:**
- **Name** (optional): Friendly name for the actor type (used in logging/diagnostics)
- **Reentrant** (default: `false`): 
  - `false` = Actor processes one operation at a time (turn-based concurrency)
  - `true` = Actor can start new operations while waiting for async calls
  - ⚠️ Use `false` unless you fully understand reentrancy implications

> **What does the generator do?** At compile-time, Quark generates factory registration code for your actor. This is what enables reflection-free instantiation in AOT scenarios.

### Step 2: Use the Actor

Update your `Program.cs`:

```csharp
using Quark.Core.Actors;
using MyQuarkApp;

Console.WriteLine("=== My First Quark Actor ===");
Console.WriteLine();

// Create an actor factory (manages actor instances)
var factory = new ActorFactory();
Console.WriteLine("✓ Actor factory created");

// Create a counter actor with unique ID "counter-1"
var counter = factory.CreateActor<CounterActor>("counter-1");
Console.WriteLine($"✓ Counter actor created with ID: {counter.ActorId}");

// Activate the actor (calls OnActivateAsync)
await counter.OnActivateAsync();

// Increment the counter
counter.Increment();
Console.WriteLine($"→ Counter value: {counter.GetValue()}");

counter.IncrementBy(5);
Console.WriteLine($"→ Counter value: {counter.GetValue()}");

// Async operations work seamlessly
var response = await counter.ProcessMessageAsync("Hello, Quark!");
Console.WriteLine($"→ Response: {response}");

// Get or create the same actor (demonstrates virtual actor pattern)
var sameCounter = factory.GetOrCreateActor<CounterActor>("counter-1");
Console.WriteLine($"→ Same instance? {ReferenceEquals(counter, sameCounter)}");
Console.WriteLine($"→ Counter value: {sameCounter.GetValue()}"); // Still 6!

// Create a different actor instance with a different ID
var counter2 = factory.CreateActor<CounterActor>("counter-2");
Console.WriteLine($"✓ Second counter created: {counter2.ActorId}");
Console.WriteLine($"→ Second counter value: {counter2.GetValue()}"); // 0 (new instance)

// Deactivate actors when done
await counter.OnDeactivateAsync();
await counter2.OnDeactivateAsync();

Console.WriteLine();
Console.WriteLine("=== Completed successfully ===");
```

### Step 3: Build and Run

```bash
# Clean build
dotnet clean
dotnet build

# Run the application
dotnet run
```

**Expected Output:**
```
=== My First Quark Actor ===

✓ Actor factory created
✓ Counter actor created with ID: counter-1
→ CounterActor 'counter-1' is activating (count: 0)
→ Counter value: 1
→ Counter value: 6
→ Response: Actor counter-1 received: Hello, Quark!
→ Same instance? True
→ Counter value: 6
✓ Second counter created: counter-2
→ Second counter value: 0
→ CounterActor 'counter-1' is deactivating (final count: 6)
→ CounterActor 'counter-2' is deactivating (final count: 0)

=== Completed successfully ===
```

---

## Understanding Quark Concepts

### Actor Identity and Virtual Actors

**Every actor has a unique ID** that determines its identity:

```csharp
var userActor = factory.CreateActor<UserActor>("user:12345");
```

**Key Insight:** In Quark's virtual actor model:
- Actor IDs are strings (use any naming scheme: "user:123", "order:456", GUIDs, etc.)
- Multiple calls with the **same ID** return the **same actor instance** (singleton per ID)
- Different IDs create **different actor instances** with separate state
- In distributed scenarios, the ID determines which server (silo) hosts the actor

This is called the **"virtual actor"** pattern (inspired by Orleans):
- You don't manually create/destroy actors - the framework manages their lifetime
- Actors are automatically activated on first use and deactivated when idle
- In a cluster, actors are transparently distributed based on their IDs

### Actor Lifecycle

Actors go through a well-defined lifecycle:

```
┌─────────────┐
│   Created   │ ← factory.CreateActor<T>(id)
└──────┬──────┘
       │
       ↓
┌─────────────┐
│  Activated  │ ← OnActivateAsync() called
└──────┬──────┘
       │
       ↓ 
┌─────────────┐
│ Processing  │ ← Handle method calls, process messages
└──────┬──────┘
       │
       ↓
┌─────────────┐
│ Deactivated │ ← OnDeactivateAsync() called
└─────────────┘
```

**Lifecycle Hooks:**
- `OnActivateAsync()`: Initialize resources (database connections, caches, etc.)
- `OnDeactivateAsync()`: Clean up resources, save state

### Thread Safety and Turn-Based Concurrency

**Actors provide automatic thread safety:**
- Each actor processes operations **one at a time** (when `Reentrant = false`)
- No need for locks, mutexes, or other synchronization primitives
- State access is inherently thread-safe within the actor

```csharp
// This code is thread-safe without any locks!
public void Increment()
{
    _counter++; // No race condition - only one operation at a time
}
```

**Reentrancy** (`Reentrant = true`):
- Allows new operations to start while waiting for `await` calls
- More concurrent but requires careful state management
- **Recommendation:** Start with `Reentrant = false` until you need the performance

---

## Exploring the Examples

Quark includes **25+ example projects** demonstrating various features:

### Basic Examples

```bash
# Simple actor creation and usage
dotnet run --project examples/Quark.Examples.Basic

# Actor supervision and fault tolerance
dotnet run --project examples/Quark.Examples.Supervision

# Reactive streaming patterns
dotnet run --project examples/Quark.Examples.Streaming
```

### Advanced Examples

```bash
# High-throughput stateless workers
dotnet run --project examples/Quark.Examples.StatelessWorkers

# Massive scale testing (1000+ actors)
dotnet run --project examples/Quark.Examples.MassiveScale

# Zero-allocation messaging patterns
dotnet run --project examples/Quark.Examples.ZeroAllocation

# Distributed actor queries
dotnet run --project examples/Quark.Examples.ActorQueries
```

### Full Application Examples

```bash
# Pizza tracking system with Blazor UI
dotnet run --project examples/Quark.Examples.PizzaTracker.Console

# Awesome Pizza Dashboard (distributed demo)
dotnet run --project examples/Quark.Demo.PizzaDash.Silo
```

> 💡 **Tip:** Browse the `examples/` directory to see real-world patterns and best practices.

---

## Building and Testing Quark

### Building the Framework

```bash
# Restore all dependencies
dotnet restore

# Build with parallel compilation (faster)
dotnet build -maxcpucount

# Build in Release mode
dotnet build -c Release -maxcpucount

# Clean build
dotnet clean && dotnet build -maxcpucount
```

### Running Tests

Quark has **370+ passing tests** covering all major features.

⚠️ **Docker Required:** Tests use Testcontainers.Redis for integration testing.

```bash
# Ensure Docker Desktop is running
docker info

# Run all tests
dotnet test

# Run tests with detailed output
dotnet test --logger "console;verbosity=detailed"

# Run specific test class
dotnet test --filter "FullyQualifiedName~ActorFactoryTests"
```

**Common Test Failures:**
- **"Cannot connect to Docker"** → Start Docker Desktop
- **"Port already in use"** → Stop other Redis instances
- **Slow test execution** → First run downloads Redis container image

### Publishing with Native AOT

One of Quark's key features is **full Native AOT support**:

```bash
# Publish for Linux (AOT)
dotnet publish -c Release -r linux-x64 --self-contained

# Publish for Windows (AOT)
dotnet publish -c Release -r win-x64 --self-contained

# Publish for macOS (AOT)
dotnet publish -c Release -r osx-arm64 --self-contained
```

**Native AOT Benefits:**
- ✅ **Fast startup:** ~50ms vs ~500ms (10x faster)
- ✅ **Small binaries:** No JIT overhead, trimmed dependencies
- ✅ **Zero reflection:** All code generated at compile-time
- ✅ **Low memory:** Smaller runtime footprint
- ✅ **Deployment:** Single self-contained binary

**Limitations:**
- ⚠️ IL3058 warnings are expected and safe (source generators handle dynamic code)
- ⚠️ All actor types must be known at compile-time (no dynamic type loading)

---

## Common Issues and Troubleshooting

### 🔴 Issue: "No factory registered for actor type YourActor"

**Error:**
```
System.InvalidOperationException: No factory registered for actor type YourActor. 
Ensure the actor is marked with [Actor] attribute for source generation.
```

**Cause:** Missing source generator reference in your project.

**Solution:** Add the explicit generator reference to your `.csproj`:

```xml
<ItemGroup>
  <ProjectReference Include="../Quark/src/Quark.Generators/Quark.Generators.csproj" 
                    OutputItemType="Analyzer" 
                    ReferenceOutputAssembly="false" />
</ItemGroup>
```

**Verify it's working:**
```bash
# Clean and rebuild
dotnet clean
dotnet build

# Check for generated files
ls obj/Debug/net10.0/generated/Quark.Generators/
```

You should see files like `YourActor.Factory.g.cs`.

### 🔴 Issue: Tests Fail with Docker Connection Errors

**Error:**
```
System.InvalidOperationException: Docker is not running
```

**Solution:**
1. Install Docker Desktop ([download](https://www.docker.com/products/docker-desktop))
2. Start Docker Desktop
3. Verify: `docker info`
4. Re-run tests: `dotnet test`

### 🟡 Issue: Build Warnings about IL3058

**Warning:**
```
IL3058: Custom attribute type is not trimmer safe because it does not have a default constructor
```

**Status:** ⚠️ **Expected and Safe**

**Explanation:** These warnings occur because source generators use reflection at *compile-time*. The generated code is reflection-free and AOT-compatible. You can safely ignore these warnings.

**Suppress if desired:**
```xml
<PropertyGroup>
  <NoWarn>$(NoWarn);IL3058</NoWarn>
</PropertyGroup>
```

### 🔴 Issue: "Missing method exception" at Runtime

**Cause:** Circular dependency between your project and `Quark.Generators`.

**Solution:** Source generators **cannot** reference the projects they generate code for. If you need shared types:
1. Create a separate shared library (e.g., `YourApp.Contracts`)
2. Reference it from both your app and `Quark.Generators`

### 🟡 Issue: "Multiple definitions of ActorAttribute"

**Warning:**
```
CS0436: The type 'ActorAttribute' conflicts with the imported type 'ActorAttribute'
```

**Cause:** Source generator creates a copy of `ActorAttribute` in each project.

**Status:** **Harmless** - does not affect functionality.

**Why?** This is intentional to avoid circular dependencies and ensure each project can compile independently.

### 🔴 Issue: Actor Not Behaving as Expected

**Debugging Checklist:**
1. ✅ Added `[Actor]` attribute to your class?
2. ✅ Inherits from `ActorBase`?
3. ✅ Added generator reference to `.csproj`?
4. ✅ Clean build: `dotnet clean && dotnet build`?
5. ✅ Using correct actor ID when calling `CreateActor<T>(id)`?
6. ✅ Called `OnActivateAsync()` before using?

**Enable diagnostic logging:**
```csharp
// In your Program.cs
using Microsoft.Extensions.Logging;

var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.AddConsole();
    builder.SetMinimumLevel(LogLevel.Debug);
});
```

---

## Next Steps

Congratulations! You've created your first Quark actor and understand the basics. Here's what to explore next:

### 📚 Core Concepts
- **[Actor Model](Actor-Model)** - Deep dive into turn-based concurrency, mailboxes, and virtual actors
- **[Source Generators](Source-Generators)** - How Quark achieves 100% reflection-free operation
- **[API Reference](API-Reference)** - Complete interface and class documentation

### 🛡️ Building Reliable Systems
- **[Supervision](Supervision)** - Parent-child hierarchies for fault tolerance
- **[Persistence](Persistence)** - State storage with Redis and PostgreSQL backends
- **[Timers and Reminders](Timers-and-Reminders)** - Scheduled operations and periodic tasks

### 🌐 Distributed Systems
- **[Clustering](Clustering)** - Run actors across multiple servers
- **[Streaming](Streaming)** - Reactive streams for pub/sub patterns

### 🔄 Migration Guides
- **[Migration from Orleans](Migration-from-Orleans)** - Coming from Orleans? Start here
- **[Migration from Akka.NET](Migration-from-Akka-NET)** - Akka.NET to Quark guide

### 💡 Advanced Topics
- **[Examples](Examples)** - 25+ example projects with real-world patterns
- **[FAQ](FAQ)** - Frequently asked questions and solutions

---

## Need Help?

- 🐛 **Bug Reports:** [GitHub Issues](https://github.com/thnak/Quark/issues)
- 💬 **Discussions:** [GitHub Discussions](https://github.com/thnak/Quark/discussions)
- 📖 **Documentation:** [Wiki Home](Home)
- 📧 **Contact:** See [Contributing](Contributing) guidelines

---

**Ready to build distributed systems?** → [Learn the Actor Model](Actor-Model)
