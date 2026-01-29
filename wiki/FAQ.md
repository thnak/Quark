# FAQ - Frequently Asked Questions

Common questions, issues, and troubleshooting for Quark.

## Table of Contents

- [Getting Started](#getting-started)
- [Build and Setup Issues](#build-and-setup-issues)
- [Runtime Errors](#runtime-errors)
- [Source Generators](#source-generators)
- [Performance](#performance)
- [Native AOT](#native-aot)
- [Testing](#testing)
- [Persistence](#persistence)
- [Clustering](#clustering)

---

## Getting Started

### What is Quark?

Quark is a high-performance, ultra-lightweight distributed actor framework for .NET 10+. It's designed for the Native AOT era and achieves **100% reflection-free** operation through compile-time source generation.

### Why choose Quark over Orleans or Akka.NET?

**Quark offers:**
- ✅ **Native AOT ready** - Full AOT compilation support
- ✅ **Zero reflection** - All code generated at compile-time
- ✅ **Smaller footprint** - Minimal dependencies, faster startup
- ✅ **Modern .NET** - Built for .NET 10+ with latest features
- ✅ **Simpler** - Focused API surface, easier to learn

**Trade-offs:**
- ⚠️ Newer, less battle-tested than Orleans
- ⚠️ Smaller ecosystem and community
- ⚠️ Some features still in development

### What are the system requirements?

- **.NET 10 SDK** or later ([Download](https://dotnet.microsoft.com/download))
- **Windows, Linux, or macOS**
- **Docker** (optional, for Redis-based features and tests)
- **IDE**: Visual Studio 2022, VS Code, or Rider

### How do I get started?

1. Clone the repository or reference Quark in your project
2. Add source generator reference (see [Source Generator Setup](#source-generators))
3. Create your first actor
4. See [Getting Started](Getting-Started) guide for details

---

## Build and Setup Issues

### Build fails with "Project not found" errors

**Cause:** Missing project references or incorrect paths

**Solution:**
```bash
# Clean and restore
dotnet clean
dotnet restore
dotnet build
```

### Multiple projects fail to build

**Cause:** Build order issues or circular dependencies

**Solution:**
```bash
# Build with maximum parallelism
dotnet build -maxcpucount

# Or build solution file directly
dotnet build Quark.slnx
```

### "Could not load file or assembly" at runtime

**Cause:** Missing NuGet packages or incompatible versions

**Solution:**
```bash
# Force restore all packages
dotnet restore --force
dotnet build --no-restore
```

### Source generator project won't build

**Cause:** Generator projects must target `netstandard2.0`

**Solution:** Verify `.csproj`:
```xml
<PropertyGroup>
  <TargetFramework>netstandard2.0</TargetFramework>
  <LangVersion>latest</LangVersion>
  <EnforceExtendedAnalyzerRules>true</EnforceExtendedAnalyzerRules>
</PropertyGroup>
```

---

## Runtime Errors

### "No factory registered for actor type"

**Problem:** Most common error when starting with Quark.

```
System.InvalidOperationException: No factory registered for actor type 'MyActor'
```

**Cause:** Missing explicit source generator reference.

**Solution:** Add generator to your `.csproj`:
```xml
<ItemGroup>
  <ProjectReference Include="path/to/Quark.Core/Quark.Core.csproj" />
  
  <!-- REQUIRED: Explicit source generator reference -->
  <ProjectReference Include="path/to/Quark.Generators/Quark.Generators.csproj" 
                    OutputItemType="Analyzer" 
                    ReferenceOutputAssembly="false" />
</ItemGroup>
```

**Verification:**
```bash
# Check generated files exist
ls obj/Debug/net10.0/generated/Quark.Generators/
# Should see: ActorFactoryRegistration.g.cs
```

See [Source Generators](Source-Generators) for detailed explanation.

### "Concurrency conflict" when saving state

**Problem:** Multiple updates to the same actor state

```
Quark.Abstractions.Persistence.ConcurrencyException: 
  Expected version 5 but found version 6
```

**Cause:** Optimistic concurrency control detected conflicting update

**Solution 1:** Retry pattern
```csharp
public async Task<bool> UpdateWithRetryAsync()
{
    const int maxRetries = 3;
    for (int attempt = 0; attempt < maxRetries; attempt++)
    {
        try
        {
            await LoadStateAsync();
            State.Value = newValue;
            await SaveStateAsync();
            return true;
        }
        catch (ConcurrencyException)
        {
            if (attempt == maxRetries - 1) throw;
            await Task.Delay(100 * (attempt + 1));
        }
    }
    return false;
}
```

**Solution 2:** Sequential processing
```csharp
// Ensure actor processes messages one at a time
[Actor(Reentrant = false)]
public class MyActor : StatefulActorBase<MyState>
{
    // Actor's turn-based concurrency prevents conflicts
}
```

### Actor methods throw NullReferenceException

**Cause:** Actor not activated before use

**Solution:** Always activate actors:
```csharp
var actor = factory.CreateActor<MyActor>("actor-1");
await actor.OnActivateAsync(); // Must call before using
actor.DoWork(); // Now safe
```

### Child actor spawning fails

**Cause:** Factory not passed to parent actor

**Solution:** Pass factory in constructor:
```csharp
[Actor]
public class ParentActor : ActorBase, ISupervisor
{
    public ParentActor(string actorId, IActorFactory? actorFactory = null)
        : base(actorId, actorFactory) // Pass factory to base
    {
    }
}
```

---

## Source Generators

### Generated files not appearing

**Problem:** No files in `obj/Debug/net10.0/generated/`

**Solution 1:** Clean and rebuild
```bash
dotnet clean
dotnet build
```

**Solution 2:** Verify generator reference
```xml
<!-- Must have OutputItemType="Analyzer" -->
<ProjectReference Include="path/to/Quark.Generators/Quark.Generators.csproj" 
                  OutputItemType="Analyzer" 
                  ReferenceOutputAssembly="false" />
```

**Solution 3:** Check for build errors in generator project
```bash
cd src/Quark.Generators
dotnet build
# Check for errors
```

**Solution 4:** Enable verbose build output
```bash
dotnet build -v detailed
# Look for source generator diagnostics
```

### Multiple attribute definition warnings (CS0436)

**Problem:** Warning about `ActorAttribute` defined in multiple places

```
warning CS0436: The type 'ActorAttribute' in 
  'obj/Debug/generated/...' conflicts with the imported type 
  'ActorAttribute' in 'Quark.Abstractions'
```

**Cause:** Source generator creates its own copy of attributes for each project

**Solution:** This is **expected and harmless**. You can:

1. **Ignore the warning** (recommended)
2. Suppress it in `.csproj`:
```xml
<PropertyGroup>
  <NoWarn>$(NoWarn);CS0436</NoWarn>
</PropertyGroup>
```

### Source generator not triggered on changes

**Problem:** Modifying actor doesn't regenerate code

**Solution:** Trigger rebuild:
```bash
# Clean to force regeneration
dotnet clean
dotnet build

# Or touch the source file
touch MyActor.cs
dotnet build
```

### How to debug source generators?

**Method 1:** View generated files
```bash
# Check generated code
cat obj/Debug/net10.0/generated/Quark.Generators/ActorFactoryRegistration.g.cs
```

**Method 2:** Attach debugger (advanced)
```csharp
// Add to generator's Execute method
if (!Debugger.IsAttached)
{
    Debugger.Launch();
}
```

**Method 3:** Enable generator logging
```bash
dotnet build -p:EmitCompilerGeneratedFiles=true
# Output to: obj/Debug/net10.0/generated/
```

---

## Performance

### Actors seem slow to create

**Q:** Is actor creation expensive?

**A:** No. Quark uses direct instantiation (no reflection). Creation is ~1-2μs per actor.

**Verify:**
```csharp
var sw = Stopwatch.StartNew();
for (int i = 0; i < 1000; i++)
{
    var actor = factory.CreateActor<MyActor>($"actor-{i}");
}
sw.Stop();
Console.WriteLine($"Created 1000 actors in {sw.ElapsedMilliseconds}ms");
// Expected: 1-3ms (1000-3000 actors/ms)
```

### Message processing throughput

**Q:** How many messages/sec can an actor process?

**A:** Depends on message complexity:
- Empty messages: ~1M msg/sec per actor
- With state updates: ~100K-500K msg/sec
- With persistence: ~10K-50K msg/sec (limited by storage)

### Reducing memory usage

**Q:** How to minimize actor memory footprint?

**A:** Best practices:
1. Use deactivation for idle actors
2. Avoid large in-memory caches
3. Use weak references for optional data
4. Enable actor GC in hosting (Phase 6+)

```csharp
// Deactivate idle actors
public override async Task OnDeactivateAsync(CancellationToken ct)
{
    _largeCache?.Clear();
    await base.OnDeactivateAsync(ct);
}
```

### Optimizing state persistence

**Q:** State saves are slow

**A:** Optimization strategies:

1. **Batch updates:**
```csharp
State.Value1 = x;
State.Value2 = y;
State.Value3 = z;
await SaveStateAsync(); // Single save, not three
```

2. **Lazy saving:**
```csharp
private bool _isDirty = false;

public void UpdateValue(int value)
{
    State.Value = value;
    _isDirty = true;
    // Don't save immediately
}

public override async Task OnDeactivateAsync(CancellationToken ct)
{
    if (_isDirty)
        await SaveStateAsync(ct);
}
```

3. **Use Redis over SQL** for higher throughput:
- Redis: ~50K writes/sec
- PostgreSQL: ~5K writes/sec

---

## Native AOT

### What is Native AOT?

Native AOT (Ahead-of-Time compilation) converts .NET IL to native machine code at build time, eliminating the JIT compiler at runtime.

**Benefits:**
- ⚡ Faster startup (~50ms vs ~500ms)
- 💾 Smaller memory footprint
- 🚀 Better performance
- 📦 Single-file deployment

### How to publish with AOT?

```bash
dotnet publish -c Release -r linux-x64 --self-contained

# Or explicitly enable AOT
dotnet publish -c Release -r linux-x64 -p:PublishAot=true
```

**Supported platforms:**
- `linux-x64`, `linux-arm64`
- `win-x64`, `win-arm64`
- `osx-x64`, `osx-arm64`

### AOT warnings (IL3058)

**Problem:** Warning about DI abstractions not being AOT-ready

```
warning IL3058: 'Microsoft.Extensions.DependencyInjection.Abstractions' 
  is not trim-compatible
```

**Cause:** Some Microsoft libraries haven't been fully updated for AOT

**Solution:** **Safe to ignore**. These warnings don't prevent AOT compilation or cause runtime issues.

Suppress if desired:
```xml
<PropertyGroup>
  <NoWarn>$(NoWarn);IL3058</NoWarn>
</PropertyGroup>
```

### Other AOT warnings (IL2026, IL2087, IL3050)

**Problem:** Warnings about reflection usage

**Cause:** Your code or dependencies use reflection

**Solution:** **Must fix** - these indicate actual AOT incompatibility:
1. Find the reflection usage
2. Replace with source generation or compile-time alternatives
3. See [Zero Reflection Achievement](../docs/ZERO_REFLECTION_ACHIEVEMENT.md)

### Verifying AOT compatibility

```bash
# Publish and check for IL2xxx/IL3xxx warnings
dotnet publish -c Release -r linux-x64

# Should see ONLY IL3058 (DI abstractions) - all others must be fixed
```

### Testing AOT builds

```bash
# Publish
dotnet publish -c Release -r linux-x64 --self-contained

# Run the native binary
./bin/Release/net10.0/linux-x64/publish/MyApp

# Verify startup time (should be ~50ms)
```

---

## Testing

### Tests require Docker

**Problem:** Tests fail with Redis connection errors

```
Failed to connect to Redis: Connection refused
```

**Cause:** Tests use Testcontainers.Redis which requires Docker

**Solution 1:** Install and start Docker
```bash
# Verify Docker is running
docker ps

# Run tests
dotnet test
```

**Solution 2:** Skip Redis tests
```bash
# Run only non-Redis tests (if test filters exist)
dotnet test --filter "Category!=Redis"
```

**Solution 3:** Mock Redis (for unit tests)
```csharp
// Use in-memory mock instead of real Redis
var mockStorage = new InMemoryStateStorage();
```

### Tests pass locally but fail in CI

**Cause:** Timing issues or missing Docker in CI

**Solution:** Configure CI for Docker:

**GitHub Actions:**
```yaml
jobs:
  test:
    runs-on: ubuntu-latest
    services:
      redis:
        image: redis:latest
        ports:
          - 6379:6379
```

**Or use Testcontainers** (already set up in Quark.Tests)

### How to run only unit tests?

```bash
# Fast unit tests only (no integration)
dotnet test --filter "Category=Unit"

# Skip slow tests
dotnet test --filter "Category!=Integration"
```

### Debugging test failures

```bash
# Verbose output
dotnet test -v normal

# Run specific test
dotnet test --filter "FullyQualifiedName~ActorFactoryTests.CreateActor"

# Debug in IDE
# Set breakpoint and use "Debug Test" in VS/Rider
```

---

## Persistence

### Which storage backend should I use?

**Redis:** Best for most scenarios
- ✅ Fast (50K+ writes/sec)
- ✅ Simple setup
- ✅ AOT compatible
- ⚠️ In-memory (requires persistence config)
- ⚠️ Single-threaded (per instance)

**PostgreSQL:** For complex queries
- ✅ Durable by default
- ✅ ACID transactions
- ✅ Relational queries
- ⚠️ Slower (5K writes/sec)
- ⚠️ Requires schema management

### State not persisting across restarts

**Cause:** Redis not configured for persistence

**Solution:** Enable Redis persistence:

**redis.conf:**
```
# Append-only file
appendonly yes
appendfsync everysec

# Or RDB snapshots
save 900 1
save 300 10
```

### "State not found" errors

**Cause:** Actor state was never saved or was deleted

**Solution:** Check save logic:
```csharp
public override async Task OnActivateAsync(CancellationToken ct)
{
    try
    {
        await LoadStateAsync(ct);
    }
    catch (StateNotFoundException)
    {
        // First activation - initialize with defaults
        State = new MyState { /* defaults */ };
        await SaveStateAsync(ct);
    }
}
```

### How to migrate state between storage backends?

```csharp
// Example: Redis to PostgreSQL
var redisStorage = new RedisStateStorageProvider(redis);
var pgStorage = new PostgresStateStorageProvider(connectionString);

// Load from Redis
var redisState = redisStorage.GetStorage("MyActor");
var data = await redisState.LoadAsync<MyState>("actor-1");

// Save to PostgreSQL
var pgState = pgStorage.GetStorage("MyActor");
await pgState.SaveAsync("actor-1", data.State, expectedVersion: 0);
```

---

## Clustering

### Silos not discovering each other

**Problem:** Silos don't see each other in cluster

**Cause:** Redis connection or network issues

**Solution:** Verify connectivity:
```csharp
var redis = await ConnectionMultiplexer.ConnectAsync("redis-host:6379");
var db = redis.GetDatabase();

// Test write
await db.StringSetAsync("test", "value");

// Test read
var value = await db.StringGetAsync("test");
Console.WriteLine($"Redis test: {value}"); // Should be "value"
```

### Actor calls fail across silos

**Cause:** gRPC transport not configured

**Solution:** Ensure gRPC endpoint is reachable:
```bash
# Test gRPC endpoint
curl -v http://silo-address:5000
# Or use grpcurl
grpcurl -plaintext silo-address:5000 list
```

### Heartbeat timeouts

**Problem:** Silos constantly joining/leaving

**Cause:** Heartbeat interval too short or network latency

**Solution:** Tune heartbeat settings:
```csharp
var membership = new RedisClusterMembership(
    redis,
    siloId: "silo-1",
    heartbeatIntervalSeconds: 10,  // Increase from default 5
    expirationSeconds: 30           // Increase from default 15
);
```

### Consistent hashing distribution

**Q:** Are actors evenly distributed?

**A:** Check distribution:
```csharp
var silos = await membership.GetActiveSilosAsync();
foreach (var silo in silos)
{
    var actors = await directory.GetActorsBySiloAsync(silo.SiloId);
    Console.WriteLine($"Silo {silo.SiloId}: {actors.Count} actors");
}
```

For better distribution, use diverse actor IDs (not sequential).

---

## Additional Resources

### Documentation

- **[Getting Started](Getting-Started)** - Setup guide
- **[Source Generators](Source-Generators)** - Understanding code generation
- **[Examples](Examples)** - Code samples
- **[API Reference](API-Reference)** - Complete API documentation

### Source Code

- **GitHub**: [thnak/Quark](https://github.com/thnak/Quark)
- **Issues**: [Report bugs](https://github.com/thnak/Quark/issues)
- **Discussions**: [Ask questions](https://github.com/thnak/Quark/discussions)

### Related Topics

- **[Actor Model](Actor-Model)** - Core concepts
- **[Supervision](Supervision)** - Fault tolerance
- **[Persistence](Persistence)** - State management
- **[Streaming](Streaming)** - Reactive patterns
- **[Clustering](Clustering)** - Distributed actors

---

## Still Need Help?

If you can't find an answer here:

1. **Search existing issues**: [GitHub Issues](https://github.com/thnak/Quark/issues)
2. **Ask in discussions**: [GitHub Discussions](https://github.com/thnak/Quark/discussions)
3. **Open a new issue**: [New Issue](https://github.com/thnak/Quark/issues/new)

When reporting issues, include:
- Quark version
- .NET SDK version
- Operating system
- Minimal reproduction code
- Full error message and stack trace
- Relevant configuration

---

**Last Updated:** 2025-01-29  
**Quark Version:** 0.1.0-alpha
