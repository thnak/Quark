# Quark Framework - Copilot Agent Instructions

## Project Overview

Quark is a high-performance, ultra-lightweight distributed actor framework for .NET 10+, built specifically for the Native AOT era. It achieves **100% reflection-free** operation through compile-time source generation using Roslyn Incremental Source Generators.

**Key Characteristics:**
- **Language**: C# (.NET 10.0)
- **Target Framework**: net10.0
- **Architecture**: Distributed virtual actor model (Orleans-inspired)
- **Build System**: MSBuild (dotnet CLI)
- **Testing Framework**: xUnit
- **AOT Ready**: Full Native AOT compatibility, zero runtime reflection
- **Source Generators**: Extensive use of Roslyn source generators for compile-time code generation

## Repository Structure

```
Quark/
├── .github/                        # GitHub configuration and workflows
├── src/                            # Source code organized by component
│   ├── Quark.Abstractions/         # Pure interfaces and contracts
│   ├── Quark.Core/                 # Meta-package (minimal code, mostly references)
│   ├── Quark.Core.Actors/          # Actor runtime implementation
│   ├── Quark.Core.Persistence/     # State storage implementations
│   ├── Quark.Core.Reminders/       # Persistent reminder system
│   ├── Quark.Core.Streaming/       # Reactive streaming
│   ├── Quark.Core.Timers/          # In-memory timers
│   ├── Quark.Generators/           # Actor and state source generators
│   ├── Quark.Generators.Logging/   # Logging source generator
│   ├── Quark.Analyzers/            # Roslyn analyzers
│   ├── Quark.Clustering.Redis/     # Redis-based cluster membership
│   ├── Quark.Transport.Grpc/       # gRPC transport layer
│   ├── Quark.Storage.Redis/        # Redis state storage
│   ├── Quark.Storage.Postgres/     # Postgres state storage
│   ├── Quark.Hosting/              # Silo hosting (silo = actor system host/node)
│   ├── Quark.Client/               # Client gateway
│   ├── Quark.Extensions.DependencyInjection/ # DI extensions
│   ├── Quark.Networking.Abstractions/ # Network abstractions
│   └── Quark.EventSourcing/        # Event sourcing support
├── tests/
│   └── Quark.Tests/                # xUnit test project (182 tests)
├── examples/
│   ├── Quark.Examples.Basic/       # Basic usage example
│   ├── Quark.Examples.Supervision/ # Supervision hierarchy example
│   └── Quark.Examples.Streaming/   # Reactive streaming example
├── docs/                           # Documentation
├── Directory.Build.props           # Shared MSBuild properties
└── Quark.slnx                     # Solution file (XML-based .slnx format introduced in VS 2022)
```

## Building and Testing

### Prerequisites
- .NET 10 SDK (version 10.0.102 or later)
- Docker (required for tests using Testcontainers.Redis)

### Essential Commands

```bash
# Restore dependencies (run first after cloning)
dotnet restore

# Build all projects (with parallel build enabled via MSBuild flag)
dotnet build -maxcpucount

# Build in Release mode
dotnet build -c Release -maxcpucount

# Run all tests (requires Docker for Redis tests)
dotnet test

# Run tests with verbose output
dotnet test -v normal

# Run tests without building
dotnet test --no-build

# Run a specific example
dotnet run --project examples/Quark.Examples.Basic/Quark.Examples.Basic.csproj
dotnet run --project examples/Quark.Examples.Supervision/Quark.Examples.Supervision.csproj
dotnet run --project examples/Quark.Examples.Streaming/Quark.Examples.Streaming.csproj

# Publish with Native AOT (for examples marked as publishable)
dotnet publish -c Release -r linux-x64 --self-contained examples/Quark.Examples.Basic/Quark.Examples.Basic.csproj
```

### Build Configuration Notes

1. **Parallel Build**: The solution has `BuildInParallel=true` in `Directory.Build.props`, so always use `-maxcpucount` for faster builds.

2. **AOT Warnings**: You may see IL3058 warnings about `Microsoft.Extensions.DependencyInjection.Abstractions` not being built with AOT support. These are expected and can be ignored - they don't prevent AOT compilation.

3. **Test Requirements**: Tests use `Testcontainers.Redis` which requires Docker to be running. If Docker is not available, Redis-related tests will fail.

## Critical: Source Generator Setup

**IMPORTANT**: When creating any project that uses Quark actors, you MUST explicitly reference the source generator. Source generator references are NOT transitive.

### Required Project Reference Pattern

Any project that defines actors (classes with `[Actor]` attribute) must include:

```xml
<ItemGroup>
  <!-- REQUIRED: Reference Quark.Core for the framework -->
  <ProjectReference Include="path/to/Quark.Core/Quark.Core.csproj" />
  
  <!-- REQUIRED: Explicit source generator reference (not transitive) -->
  <ProjectReference Include="path/to/Quark.Generators/Quark.Generators.csproj" 
                    OutputItemType="Analyzer" 
                    ReferenceOutputAssembly="false" />
</ItemGroup>
```

**Why This Matters:**
- Without the explicit generator reference, actor factory methods won't be generated
- You'll get runtime errors: `System.InvalidOperationException: No factory registered for actor type`
- See `docs/SOURCE_GENERATOR_SETUP.md` for detailed explanation

**Examples to Reference:**
- `examples/Quark.Examples.Basic/Quark.Examples.Basic.csproj`
- `tests/Quark.Tests/Quark.Tests.csproj`

## Code Conventions and Style

### File Organization
- One public type per file
- File names match the type name (e.g., `IActor.cs`, `ActorBase.cs`)
- Interfaces start with `I` prefix
- Use namespaces matching folder structure

### Naming Conventions
- **Interfaces**: `IActor`, `IActorFactory`, `ISupervisor`
- **Abstract Classes**: `ActorBase`, `StatefulActorBase`
- **Concrete Classes**: `ActorFactory`, `ChannelMailbox`, `StreamBroker`
- **Methods**: PascalCase (`OnActivateAsync`, `SpawnChildAsync`)
- **Private Fields**: `_camelCase` with underscore prefix
- **Properties**: PascalCase
- **Parameters**: camelCase

### Code Style
- **Null Safety**: Nullable reference types enabled (`<Nullable>enable</Nullable>`)
- **Implicit Usings**: Enabled project-wide
- **Comments**: Use XML documentation comments for public APIs (triple-slash `///`)
- **Async**: Always use `Async` suffix for async methods, include `CancellationToken` parameter (default to `default`)

### Example Code Pattern

```csharp
namespace Quark.Abstractions;

/// <summary>
/// Base interface for all actors in the Quark framework.
/// Actors are lightweight, stateful objects that process messages sequentially.
/// </summary>
public interface IActor
{
    /// <summary>
    /// Gets the unique identifier for this actor instance.
    /// </summary>
    string ActorId { get; }

    /// <summary>
    /// Called when the actor is activated.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    Task OnActivateAsync(CancellationToken cancellationToken = default);
}
```

## Testing Practices

### Test Framework: xUnit

**Test Organization:**
- One test class per component/feature
- Test class names: `{ComponentName}Tests` (e.g., `SupervisionTests`, `StreamBrokerTests`)
- Test method names: `{Method}_{Scenario}_{ExpectedResult}` (e.g., `SpawnChildAsync_WithFactory_CreatesChild`)

### Test Structure Pattern

```csharp
[Fact]
public async Task MethodName_Scenario_ExpectedResult()
{
    // Arrange
    var factory = new ActorFactory();
    var actor = factory.CreateActor<TestActor>("test-1");

    // Act
    var result = await actor.DoSomethingAsync();

    // Assert
    Assert.NotNull(result);
    Assert.Equal(expected, result);
}
```

### Running Tests
- All 182 tests should pass
- Tests use `Testcontainers.Redis` for integration testing
- Tests create and dispose Docker containers automatically
- Use `dotnet test -v normal` to see test execution details

## AOT and Reflection Considerations

### Core Principle: Zero Reflection at Runtime

Quark achieves 100% reflection-free operation. ALL dynamic behavior is handled at compile-time via source generators:

1. **Actor Factory Generation**: `Quark.Generators/ActorSourceGenerator.cs`
   - Scans for `[Actor]` attributes
   - Generates factory methods at compile-time
   - Creates module initializer for auto-registration

2. **State Persistence**: `Quark.Generators/StateSourceGenerator.cs`
   - Generates Load/Save/Delete methods for `[QuarkState]` properties
   - Creates partial properties with backing fields

3. **JSON Serialization**: System.Text.Json's built-in source generator
   - Uses `JsonSerializerContext` (e.g., `QuarkJsonSerializerContext` in Quark.Clustering.Redis)
   - No reflection in serialization

4. **Logging**: `Quark.Generators.Logging/LoggerMessageSourceGenerator.cs`
   - Uses LoggerMessage.Define pattern
   - Zero-allocation logging

### AOT Compatibility Checklist

When adding new features:
- ✅ Do NOT use `Activator.CreateInstance`, `Type.GetMethod`, or any reflection APIs
- ✅ Do NOT use runtime IL emission
- ✅ Use source generators for dynamic behavior
- ✅ Mark types for JSON serialization with `[JsonSerializable(typeof(T))]`
- ✅ Test AOT compilation with `dotnet publish -c Release -r linux-x64 -p:PublishAot=true`

### Verifying Zero Reflection

```bash
# Publish with AOT (should succeed with minimal warnings)
dotnet publish -c Release -r linux-x64 -p:PublishAot=true examples/Quark.Examples.Basic

# Expected: Only IL3058 warnings about DI abstractions (safe to ignore)
# NOT Expected: IL2026, IL2087, IL3050 (these indicate reflection usage)
```

## Common Patterns and Idioms

### Actor Definition Pattern

```csharp
[Actor(Name = "Counter", Reentrant = false)]
public class CounterActor : ActorBase
{
    private int _counter;

    public CounterActor(string actorId) : base(actorId)
    {
        _counter = 0;
    }

    public override Task OnActivateAsync(CancellationToken cancellationToken = default)
    {
        // Initialization logic
        return Task.CompletedTask;
    }

    public void Increment() => _counter++;
    public int GetValue() => _counter;
}
```

### Supervision Pattern

```csharp
[Actor]
public class SupervisorActor : ActorBase, ISupervisor
{
    public SupervisorActor(string actorId, IActorFactory? actorFactory = null) 
        : base(actorId, actorFactory) { }

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

### Stream Subscription Pattern

```csharp
[Actor(Name = "OrderProcessor")]
[QuarkStream("orders/processed")]
public class OrderProcessorActor : ActorBase, IStreamConsumer<OrderMessage>
{
    public OrderProcessorActor(string actorId) : base(actorId) { }

    public async Task OnStreamMessageAsync(
        OrderMessage message, 
        StreamId streamId, 
        CancellationToken cancellationToken = default)
    {
        // Process message
        await Task.CompletedTask;
    }
}
```

## Common Issues and Workarounds

### Issue 1: Actor Not Found at Runtime

**Symptom**: `System.InvalidOperationException: No factory registered for actor type`

**Cause**: Missing explicit source generator reference

**Solution**: Add the generator reference to your `.csproj`:
```xml
<ProjectReference Include="path/to/Quark.Generators/Quark.Generators.csproj" 
                  OutputItemType="Analyzer" 
                  ReferenceOutputAssembly="false" />
```

### Issue 2: Source Generator Not Producing Files

**Symptom**: No generated files in `obj/Debug/net10.0/generated/`

**Solution**:
1. Clean the project: `dotnet clean`
2. Rebuild: `dotnet build`
3. Verify generator reference has `OutputItemType="Analyzer"`
4. Check for build errors in the generator project itself

### Issue 3: Multiple Attribute Definition Warnings

**Symptom**: CS0436 warnings about `ActorAttribute` being defined in multiple places

**Cause**: The source generator creates its own copy of attributes for each consuming project

**Solution**: These warnings are harmless and expected. You can suppress them or ignore them. They don't affect functionality.

### Issue 4: Docker Not Available for Tests

**Symptom**: Redis-related tests fail with Docker connection errors

**Solution**: 
1. Ensure Docker is running: `docker ps`
2. If Docker is unavailable, skip Redis tests or run only specific test classes
3. Most core functionality tests don't require Docker

### Issue 5: AOT IL3058 Warnings

**Symptom**: Warning about `Microsoft.Extensions.DependencyInjection.Abstractions` not being AOT compatible

**Cause**: Some Microsoft libraries haven't been fully updated for AOT

**Solution**: These warnings are expected and safe to ignore. They don't prevent AOT compilation or cause runtime issues.

## Project-Specific Considerations

### 1. Multi-Project Solution
- The solution contains 23 projects organized by feature
- Solution uses `.slnx` format (XML-based format introduced in Visual Studio 2022)
- Core abstractions are in `Quark.Abstractions` (interfaces only, no implementations)
- Implementations are split across feature-specific projects
- `Quark.Core` is a meta-package that references core components

### 2. Generators Are Critical
- **Quark.Generators**: Contains `ActorSourceGenerator` and `StateSourceGenerator`
- **Quark.Generators.Logging**: Contains `LoggerMessageSourceGenerator`
- Targets `netstandard2.0` (required for source generators)
- Must NOT reference the projects they generate code for (circular dependency)

### 3. Test Project Dependencies
- Tests reference nearly all projects to test integration
- Uses Moq for mocking
- Uses Testcontainers for integration tests
- All tests are in a single `Quark.Tests` project (182 tests)

### 4. Examples Demonstrate Patterns
- `Quark.Examples.Basic`: Basic actor lifecycle
- `Quark.Examples.Supervision`: Parent-child supervision
- `Quark.Examples.Streaming`: Reactive streams
- Examples are publishable with Native AOT (`<IsPublishable>true</IsPublishable>`)

## Documentation References

**Essential Reading:**
- `README.md`: Project overview and quick start
- `docs/SOURCE_GENERATOR_SETUP.md`: Critical source generator setup guide
- `docs/ZERO_REFLECTION_ACHIEVEMENT.md`: How reflection was eliminated
- `docs/PROGRESS.md`: Development phases and current status
- `docs/PHASE5_STREAMING.md`: Streaming implementation details

**Key Technical Docs:**
- Actor model: See `src/Quark.Abstractions/IActor.cs` and `src/Quark.Core.Actors/ActorBase.cs`
- Supervision: See `src/Quark.Abstractions/ISupervisor.cs`
- Streaming: See `docs/PHASE5_STREAMING.md`

## Making Changes

### Adding a New Feature
1. **Add abstractions first**: Create interfaces in `Quark.Abstractions`
2. **Implement in appropriate project**: Create implementation in relevant `Quark.Core.*` project or other appropriate `Quark.*` project
3. **Update tests**: Add tests to `Quark.Tests`
4. **Update examples**: If user-facing, add example to `examples/`
5. **Document**: Update `README.md` and relevant docs in `docs/`

### Modifying Source Generators
1. **Location**: `src/Quark.Generators/` or `src/Quark.Generators.Logging/`
2. **Target**: Must target `netstandard2.0`
3. **Test**: Test generators by building consuming projects and checking generated files in `obj/`
4. **Debugging**: Generated files appear in `obj/Debug/net10.0/generated/`

### Adding Dependencies
- Use NuGet packages that are AOT-compatible
- Check for AOT warnings after adding dependencies
- Prefer packages with Native AOT support (consistent capitalization with Microsoft's official terminology)
- Document any known AOT incompatibilities

## Performance Considerations

### Build Performance
- Use `-maxcpucount` flag for parallel builds (solution is configured for this)
- Clean build time: ~15-20 seconds on modern hardware
- Incremental builds: ~2-5 seconds

### Test Performance
- Full test suite: ~7-12 seconds (includes Docker container spin-up)
- Use `--no-build` to skip rebuild when running tests repeatedly
- Redis tests add overhead due to container lifecycle

### Runtime Performance
- Actor creation: Direct instantiation (no reflection overhead)
- JSON serialization: 2-3x faster than reflection-based (source generated)
- Logging: Zero allocation (LoggerMessage pattern)
- AOT startup: ~50ms vs ~500ms with JIT

## Troubleshooting Build/Test Issues

### Build Fails
1. Check .NET SDK version: `dotnet --version` (should be 10.0.102+)
2. Clean and restore: `dotnet clean && dotnet restore && dotnet build`
3. Check for circular references in project files
4. Ensure generator projects build first (no dependencies on projects they generate for)

### Tests Fail
1. Ensure Docker is running: `docker ps`
2. Check for port conflicts (Redis uses default ports)
3. Run with verbose output: `dotnet test -v normal`
4. Check specific test output for details

### Source Generator Issues
1. Check generated files: Look in `obj/Debug/net10.0/generated/`
2. Rebuild consuming projects: `dotnet clean && dotnet build`
3. Verify `[Actor]` attributes are correctly applied
4. Ensure generator project builds successfully

## Best Practices for AI Agents

1. **Always use explicit paths**: Use absolute paths when referencing project files
2. **Check build before testing**: Run `dotnet build` before `dotnet test`
3. **Use appropriate test commands**: Use `dotnet test --no-build` for repeated test runs
4. **Parallel operations**: Build operations can be parallelized with `-maxcpucount`
5. **Verify AOT compatibility**: When adding code, test with `PublishAot=true`
6. **Document workarounds**: If you encounter and fix an issue, document it
7. **Follow conventions**: Match existing code style and organization patterns
8. **Test thoroughly**: Run all 182 tests before considering work complete

## Summary Checklist for Working with Quark

- [ ] .NET 10 SDK installed and verified
- [ ] Docker running (for Redis tests)
- [ ] Understand source generator setup requirements
- [ ] Know where to find abstractions vs implementations
- [ ] Familiar with test patterns (Arrange-Act-Assert)
- [ ] Aware of AOT constraints (no reflection at runtime)
- [ ] Know how to build, test, and run examples
- [ ] Understand the project structure and organization
- [ ] Ready to make minimal, surgical changes following conventions

---

**Last Updated**: 2026-01-29  
**Quark Version**: 0.1.0-alpha  
**Test Status**: 182/182 passing ✅  
**AOT Ready**: Yes ✅
