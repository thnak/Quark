# Quark

Quark is a high-performance, ultra-lightweight distributed actor framework for .NET, built specifically for the Native AOT era. Unlike traditional frameworks that rely on heavy runtime reflection and IL emission, Quark moves the "magic" to compile-time using Roslyn Incremental Source Generators.

## Features

- ✨ **Native AOT Ready**: Full support for .NET Native AOT compilation
- 🚀 **High Performance**: Minimal overhead actor framework
- 🔧 **Source Generation**: Compile-time code generation for AOT compatibility
- 🏗️ **Orleans-inspired**: Familiar actor model with modern AOT support
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

## Architecture

### Core Components

- **IActor**: Base interface for all actors
- **ActorBase**: Abstract base class providing common actor functionality
- **ActorFactory**: Factory for creating and managing actor instances
- **ActorAttribute**: Marks classes for source generation

### Source Generator

The `Quark.SourceGenerator` project contains a Roslyn incremental source generator that:
- Detects classes marked with `[Actor]` attribute
- Generates AOT-friendly factory methods at compile-time
- Eliminates runtime reflection for Native AOT compatibility

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

- [ ] Complete source generator integration with ActorFactory
- [ ] Add distributed actor support
- [ ] Implement grain persistence
- [ ] Add clustering support
- [ ] Performance benchmarks
- [ ] More examples and documentation

