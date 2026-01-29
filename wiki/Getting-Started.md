# Getting Started with Quark

This guide will help you set up Quark and create your first actor.

## Prerequisites

- **.NET 10 SDK** or later ([Download](https://dotnet.microsoft.com/download))
- **IDE**: Visual Studio 2022, VS Code, or Rider
- **Docker** (optional, for Redis clustering and tests)

## Installation

### Option 1: Clone the Repository

```bash
git clone https://github.com/thnak/Quark.git
cd Quark
dotnet restore
dotnet build
```

### Option 2: Reference in Your Project

Add Quark as a project reference or NuGet package (when published):

```xml
<ItemGroup>
  <ProjectReference Include="path/to/Quark.Core/Quark.Core.csproj" />
  
  <!-- CRITICAL: Source generator must be explicitly referenced -->
  <ProjectReference Include="path/to/Quark.Generators/Quark.Generators.csproj" 
                    OutputItemType="Analyzer" 
                    ReferenceOutputAssembly="false" />
</ItemGroup>
```

⚠️ **Important**: The source generator reference is **not transitive**. You must explicitly add it to every project that defines actors. See [Source Generators](Source-Generators) for details.

## Your First Actor

Let's create a simple counter actor that demonstrates the basic concepts.

### Step 1: Define the Actor

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
        Console.WriteLine($"Counter {ActorId} activated");
        return Task.CompletedTask;
    }

    public override Task OnDeactivateAsync(CancellationToken cancellationToken = default)
    {
        Console.WriteLine($"Counter {ActorId} deactivated with count: {_count}");
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

### Step 2: Use the Actor

```csharp
using Quark.Core.Actors;

// Create an actor factory
var factory = new ActorFactory();

// Create a counter actor instance
var counter = factory.CreateActor<CounterActor>("counter-1");

// Activate the actor
await counter.OnActivateAsync();

// Use the actor
counter.Increment();
counter.Increment();
counter.Increment();

Console.WriteLine($"Count: {counter.GetCount()}"); // Output: Count: 3

// Deactivate when done
await counter.OnDeactivateAsync();
```

### Step 3: Build and Run

```bash
dotnet build
dotnet run
```

**Expected Output:**
```
Counter counter-1 activated
Count: 3
Counter counter-1 deactivated with count: 3
```

## Understanding the Code

### The `[Actor]` Attribute

The `[Actor]` attribute marks a class for source generation. At compile-time, Quark generates factory methods that enable AOT-compatible actor creation.

```csharp
[Actor(Name = "Counter", Reentrant = false)]
```

- **Name**: Friendly name for the actor type
- **Reentrant**: Whether the actor can process messages while waiting for async operations

### Actor Lifecycle

Actors have a defined lifecycle:

1. **Creation**: `factory.CreateActor<T>(actorId)`
2. **Activation**: `OnActivateAsync()` - Called when the actor starts
3. **Processing**: Handle method calls and messages
4. **Deactivation**: `OnDeactivateAsync()` - Called when the actor stops

### Actor Identity

Every actor has a unique ID (`ActorId`). In Quark's virtual actor model:
- The ID determines actor placement in a distributed cluster
- Multiple calls with the same ID always route to the same actor instance
- IDs are strings, allowing flexible naming schemes (e.g., "user:123", "order:456")

## Next Steps

Now that you've created your first actor, explore more features:

- **[Actor Model](Actor-Model)** - Deep dive into actors, mailboxes, and turn-based concurrency
- **[Supervision](Supervision)** - Create parent-child hierarchies for fault tolerance
- **[Persistence](Persistence)** - Add state that survives restarts
- **[Examples](Examples)** - See more complex examples

## Running the Example Projects

Quark includes several example projects:

### Basic Example
```bash
dotnet run --project examples/Quark.Examples.Basic
```

### Supervision Example
```bash
dotnet run --project examples/Quark.Examples.Supervision
```

### Streaming Example
```bash
dotnet run --project examples/Quark.Examples.Streaming
```

## Common Issues

### "No factory registered for actor type"

**Cause**: Missing source generator reference.

**Solution**: Add the explicit generator reference to your `.csproj`:
```xml
<ProjectReference Include="path/to/Quark.Generators/Quark.Generators.csproj" 
                  OutputItemType="Analyzer" 
                  ReferenceOutputAssembly="false" />
```

See [FAQ](FAQ) for more troubleshooting tips.

## Publishing with Native AOT

One of Quark's key features is Native AOT support. To publish with AOT:

```bash
dotnet publish -c Release -r linux-x64 --self-contained
```

The resulting binary:
- ✅ Contains no IL - fully native code
- ✅ Starts in ~50ms (vs ~500ms with JIT)
- ✅ Uses zero reflection at runtime
- ✅ Has smaller memory footprint

See [Source Generators](Source-Generators) to learn how Quark achieves this.

---

**Next**: [Actor Model](Actor-Model) - Learn about the core concepts →
