# Migration Guides

This section provides guidance for migrating to Quark from other actor frameworks or between Quark versions.

## Available Migration Guides

### From Other Frameworks

- **[Migrating from Orleans](Migration-from-Orleans)** - For developers moving from Microsoft Orleans
- **[Migrating from Akka.NET](Migration-from-Akka-NET)** - For developers moving from Akka.NET

### Between Quark Versions

- **[Version Migration Guide](Migration-Between-Versions)** - Upgrading between Quark versions

## Overview

Quark currently does not provide automated migration tools. All migrations must be performed manually by following the guides and updating your code accordingly.

### What to Expect

When migrating to Quark, you'll need to:

1. **Update project references** - Add Quark packages and source generators
2. **Refactor actor definitions** - Update to Quark's actor model and attributes
3. **Adapt persistence code** - Migrate to Quark's state management
4. **Update clustering configuration** - Configure Redis-based clustering
5. **Revise deployment** - Take advantage of Native AOT compilation

### Key Differences

#### Source Generation vs. Reflection

Unlike Orleans and Akka.NET, Quark uses **compile-time source generation** instead of runtime reflection:

- ✅ **Native AOT compatible** - Can be compiled to native code
- ✅ **Faster startup** - No runtime code generation overhead
- ✅ **Better performance** - No reflection penalty at runtime
- ⚠️ **Different attribute system** - Uses `[Actor]` instead of `[Grain]` or actor interfaces
- ⚠️ **Explicit generator references** - Must add source generator to each project

#### Actor Model Variations

While Quark is Orleans-inspired, there are some differences:

| Feature | Orleans | Akka.NET | Quark |
|---------|---------|----------|-------|
| **Actor Creation** | Interface-based grains | Props & ActorSystem | `ActorFactory.CreateActor<T>()` |
| **Activation** | Automatic | Explicit | `OnActivateAsync()` |
| **Persistence** | State classes | Event sourcing | `[QuarkState]` properties |
| **Clustering** | Membership providers | Akka.Remote | Redis-based |
| **Serialization** | Multiple options | Wire/Hyperion | System.Text.Json |
| **AOT Support** | Limited | No | Full support |

#### Simplified API Surface

Quark intentionally has a more focused API:

- **Fewer abstractions** - Easier to learn and use
- **Direct actor access** - No grain proxies or actor selections
- **Compile-time safety** - Many errors caught at compile time
- **Modern .NET** - Built for .NET 10+ with latest features

## Migration Strategy

### Recommended Approach

1. **Start with a pilot** - Migrate a small, isolated component first
2. **Run in parallel** - Keep both systems running during migration
3. **Migrate incrementally** - Move services one at a time
4. **Test thoroughly** - Validate behavior matches the original system
5. **Monitor closely** - Watch for performance or correctness issues

### Coexistence Patterns

You can run Quark alongside your existing framework during migration:

**Option 1: Side-by-Side Services**
- Deploy Quark services separately
- Use HTTP/gRPC for inter-service communication
- Gradually migrate functionality

**Option 2: Hybrid Process**
- Run both frameworks in the same process (if compatible)
- Share data via message queues or databases
- Migrate actors one by one

**Option 3: Strangler Pattern**
- Route new features to Quark
- Leave legacy features in original framework
- Slowly retire old functionality

## Common Migration Challenges

### Challenge 1: No Code Generator Equivalents

**Problem**: Orleans code generation and Akka.NET's dynamic actor creation don't have direct equivalents in Quark.

**Solution**: Quark uses source generators at compile-time. Add explicit generator references:

```xml
<ProjectReference Include="path/to/Quark.Generators/Quark.Generators.csproj" 
                  OutputItemType="Analyzer" 
                  ReferenceOutputAssembly="false" />
```

### Challenge 2: Different State Management

**Problem**: Orleans uses state classes, Akka.NET uses event sourcing.

**Solution**: Quark uses properties with `[QuarkState]` attribute:

```csharp
[Actor]
public class MyActor : StatefulActorBase
{
    [QuarkState]
    public MyState? State { get; set; }
}
```

Migrate state by exporting from old system and importing into Quark storage.

### Challenge 3: Serialization Differences

**Problem**: Different frameworks use different serializers.

**Solution**: Quark uses System.Text.Json with source generation. Create a `JsonSerializerContext`:

```csharp
[JsonSerializable(typeof(MyState))]
public partial class MyJsonContext : JsonSerializerContext { }
```

You may need data transformation layer during migration.

### Challenge 4: Clustering Differences

**Problem**: Different clustering mechanisms (membership tables, gossip protocols, etc.).

**Solution**: Quark uses Redis for clustering. You'll need:
- Redis instance for cluster membership
- Migration of cluster configuration
- Coordination during cutover to avoid split-brain

### Challenge 5: Actor Lifecycle Differences

**Problem**: Different activation/deactivation patterns.

**Solution**: Implement Quark's lifecycle methods:

```csharp
public override Task OnActivateAsync(CancellationToken cancellationToken = default)
{
    // Initialization logic
    return Task.CompletedTask;
}

public override Task OnDeactivateAsync(CancellationToken cancellationToken = default)
{
    // Cleanup logic
    return Task.CompletedTask;
}
```

## Testing Your Migration

### Unit Testing

Quark actors can be tested directly:

```csharp
[Fact]
public async Task MyActor_ProcessesMessage_Correctly()
{
    var factory = new ActorFactory();
    var actor = factory.CreateActor<MyActor>("test-1");
    
    await actor.OnActivateAsync();
    
    var result = await actor.ProcessAsync("test message");
    
    Assert.Equal(expectedResult, result);
}
```

### Integration Testing

Use Testcontainers for Redis-based tests:

```csharp
[Fact]
public async Task ClusteredActors_CommunicateCorrectly()
{
    await using var redis = new RedisBuilder().Build();
    await redis.StartAsync();
    
    // Test with real Redis
    var connectionString = redis.GetConnectionString();
    // ... test code
}
```

### Load Testing

Validate performance characteristics:

```csharp
// Measure actor throughput
var stopwatch = Stopwatch.StartNew();
for (int i = 0; i < 10_000; i++)
{
    await actor.ProcessAsync($"message-{i}");
}
stopwatch.Stop();
Console.WriteLine($"Processed 10,000 messages in {stopwatch.ElapsedMilliseconds}ms");
```

## Performance Considerations

### Startup Time

Quark with Native AOT starts significantly faster:

- **Orleans**: ~500ms (JIT compilation)
- **Akka.NET**: ~400ms (JIT compilation)
- **Quark (AOT)**: ~50ms (no compilation needed)

### Memory Footprint

Native AOT reduces memory usage:

- **No JIT overhead** - No IL or code generation at runtime
- **Smaller working set** - Only includes code that's used
- **Better cache locality** - Native code is more compact

### Throughput

Quark's lock-free mailbox and minimal allocations provide high throughput:

- **Message processing**: Similar to Orleans, faster than Akka.NET
- **State operations**: Depends on storage backend
- **Network**: gRPC with connection pooling

## Getting Help

If you encounter issues during migration:

1. **Check the FAQ** - [FAQ](FAQ) has common solutions
2. **Review examples** - [Examples](Examples) show best practices
3. **Ask questions** - Open a [discussion](https://github.com/thnak/Quark/discussions)
4. **Report bugs** - Create an [issue](https://github.com/thnak/Quark/issues) if you find problems

## Next Steps

Choose the migration guide that matches your situation:

- **[Migrating from Orleans](Migration-from-Orleans)** →
- **[Migrating from Akka.NET](Migration-from-Akka-NET)** →
- **[Version Migration Guide](Migration-Between-Versions)** →

---

**Related Topics:**
- [Getting Started](Getting-Started) - Basic Quark setup
- [Actor Model](Actor-Model) - Understanding Quark actors
- [Source Generators](Source-Generators) - How AOT works in Quark
- [FAQ](FAQ) - Troubleshooting and common questions
