# Migration Between Quark Versions

This guide helps you migrate between different versions of Quark. Since Quark is currently in alpha, breaking changes may occur as the framework evolves toward stability.

## Current Version: 0.1.0-alpha

Quark is currently in **alpha** status. We're working toward a stable 1.0 release with the following timeline:

- **0.1.x-alpha**: Core features, experimental APIs
- **0.2.x-beta**: API stabilization, production hardening
- **1.0.0**: Stable release with API guarantees

## Version Compatibility Policy

### During Alpha (0.x)

- ⚠️ **Breaking changes expected** - APIs may change
- ⚠️ **No backward compatibility guarantees**
- ✅ Migration guides provided for major changes
- ✅ Deprecation warnings when possible

### After 1.0 Release

- ✅ **Semantic versioning** - Major.Minor.Patch
- ✅ **Breaking changes only in major versions**
- ✅ **Deprecation period** - At least one minor version before removal
- ✅ **Migration tools** - Automated migrations where possible

## How to Check Your Version

### Check Quark Version

```bash
# View project references
dotnet list package --include-transitive | grep Quark

# Check Assembly version in code
var version = typeof(ActorBase).Assembly.GetName().Version;
Console.WriteLine($"Quark version: {version}");
```

### Check Source Generator Version

```bash
# In your project directory
dotnet build -v detailed | grep "Quark.Generators"
```

## Migration Guides by Version

### Future Versions (Planned)

When new versions are released, specific migration guides will be added here.

---

## Breaking Changes (By Version)

### 0.1.0-alpha (Current)

**Release Date**: January 2026  
**Status**: Initial release

#### What's Included

- ✅ Core actor runtime
- ✅ Supervision hierarchies
- ✅ State persistence (Redis, PostgreSQL)
- ✅ Timers and reminders
- ✅ Reactive streaming
- ✅ Clustering (Redis-based)
- ✅ Source generators for actors and state
- ✅ 182 passing tests

#### Known Limitations

- Hosting layer (Phase 6) in progress
- Client gateway not yet available
- Limited storage providers (Redis, PostgreSQL only)
- No migration tools (manual migration only)
- Some APIs may change before 1.0

---

## General Migration Strategies

### 1. Updating Project References

When upgrading Quark, always update all related packages together:

```bash
# Update all Quark packages
dotnet add package Quark.Core --version <new-version>
dotnet add package Quark.Clustering.Redis --version <new-version>
dotnet add package Quark.Storage.Redis --version <new-version>

# Don't forget the source generator
# (Usually handled via project reference in Quark repository)
```

### 2. Check for Breaking Changes

Before upgrading:

1. **Review release notes** - Check GitHub releases for breaking changes
2. **Read migration guide** - Follow version-specific instructions below
3. **Update dependencies** - Ensure compatible .NET SDK version
4. **Review deprecation warnings** - Fix any deprecated API usage

### 3. Incremental Migration

For large projects:

1. **Create a migration branch**
2. **Update one module at a time**
3. **Run tests after each module**
4. **Fix compilation errors**
5. **Address runtime issues**
6. **Merge when stable**

### 4. Testing Strategy

After upgrading:

```bash
# Run all tests
dotnet test

# Run with verbose output to catch warnings
dotnet test -v normal

# Check for deprecation warnings
dotnet build -warnaserror:CS0618
```

---

## Common Migration Scenarios

### Scenario 1: Actor Attribute Changes

If the `[Actor]` attribute signature changes in a future version:

**Before (hypothetical):**
```csharp
[Actor(Name = "MyActor")]
public class MyActor : ActorBase
{
    // ...
}
```

**After (hypothetical):**
```csharp
[Actor("MyActor", Reentrant = false, MaxConcurrency = 1)]
public class MyActor : ActorBase
{
    // ...
}
```

**Migration:**
- Update all `[Actor]` attributes in your codebase
- Use find-and-replace with regex if needed
- Rebuild to trigger source generation

### Scenario 2: State Storage Interface Changes

If `IStateStorage<T>` interface changes:

**Before (hypothetical):**
```csharp
public interface IStateStorage<T>
{
    Task<T?> LoadAsync(string key);
    Task SaveAsync(string key, T state);
    Task DeleteAsync(string key);
}
```

**After (hypothetical):**
```csharp
public interface IStateStorage<T>
{
    Task<T?> LoadAsync(string key, CancellationToken cancellationToken = default);
    Task SaveAsync(string key, T state, CancellationToken cancellationToken = default);
    Task DeleteAsync(string key, CancellationToken cancellationToken = default);
}
```

**Migration:**
- Update custom storage implementations
- Add `CancellationToken` parameters
- Pass tokens through to underlying operations

### Scenario 3: Configuration API Changes

If configuration moves from manual setup to builder pattern (planned for Phase 6):

**Before (current):**
```csharp
var factory = new ActorFactory();
var redis = ConnectionMultiplexer.Connect("localhost:6379");
var membership = new RedisClusterMembership(redis);
await membership.RegisterSiloAsync("silo-1", "localhost:5000");
```

**After (hypothetical future):**
```csharp
var host = new SiloHostBuilder()
    .UseRedisClusterMembership("localhost:6379")
    .ConfigureSilo(options =>
    {
        options.SiloId = "silo-1";
        options.Address = "localhost:5000";
    })
    .Build();

await host.StartAsync();
```

**Migration:**
- Replace manual setup with builder pattern
- Configuration becomes declarative
- Lifecycle management handled by host

---

## Data Migration

### Migrating Persisted State

If storage format changes between versions:

#### Step 1: Export Current State

```csharp
// Export all state from current version
public async Task ExportAllStateAsync()
{
    var allActorIds = await GetAllActorIdsAsync();
    var exportData = new List<StateExport>();

    foreach (var actorId in allActorIds)
    {
        var actor = factory.CreateActor<MyActor>(actorId, storageProvider);
        await actor.OnActivateAsync();

        exportData.Add(new StateExport
        {
            ActorId = actorId,
            State = actor.State,
            Version = "0.1.0"
        });

        await actor.OnDeactivateAsync();
    }

    var json = JsonSerializer.Serialize(exportData, new JsonSerializerOptions
    {
        WriteIndented = true
    });

    await File.WriteAllTextAsync("state-export.json", json);
}
```

#### Step 2: Transform Data (if needed)

```csharp
// Transform exported data to new format
public async Task TransformStateAsync()
{
    var json = await File.ReadAllTextAsync("state-export.json");
    var oldData = JsonSerializer.Deserialize<List<OldStateExport>>(json);

    var newData = oldData.Select(old => new NewStateExport
    {
        ActorId = old.ActorId,
        State = ConvertOldToNew(old.State),
        Version = "0.2.0"
    }).ToList();

    var newJson = JsonSerializer.Serialize(newData, new JsonSerializerOptions
    {
        WriteIndented = true
    });

    await File.WriteAllTextAsync("state-export-transformed.json", newJson);
}

private NewState ConvertOldToNew(OldState old)
{
    // Transform state structure
    return new NewState
    {
        // Map fields
    };
}
```

#### Step 3: Import to New Version

```csharp
// Import state in new version
public async Task ImportStateAsync()
{
    var json = await File.ReadAllTextAsync("state-export-transformed.json");
    var data = JsonSerializer.Deserialize<List<NewStateExport>>(json);

    var factory = new ActorFactory();
    var provider = new StateStorageProvider();
    // ... configure new storage

    foreach (var export in data)
    {
        var actor = new MyActor(export.ActorId, provider);
        await actor.OnActivateAsync();

        actor.State = export.State;
        await actor.SaveStateAsync();

        await actor.OnDeactivateAsync();
    }
}
```

### Zero-Downtime Migration

For production systems, use blue-green deployment:

1. **Deploy new version alongside old** (blue-green)
2. **Dual-write** - Write to both old and new storage
3. **Background migration** - Copy data to new format
4. **Validate** - Ensure data integrity
5. **Switch traffic** - Route to new version
6. **Monitor** - Watch for issues
7. **Retire old version** - When stable

---

## Source Generator Updates

### Regenerating Generated Code

After updating Quark:

```bash
# Clean generated files
dotnet clean

# Rebuild to regenerate
dotnet build

# Check generated files
ls obj/Debug/net10.0/generated/Quark.Generators/
```

### Handling Generator Breaking Changes

If generator output format changes:

1. **Clean all projects**
2. **Update generator reference**
3. **Rebuild in correct order**
4. **Check for new compiler errors**
5. **Update code to match new patterns**

---

## Rollback Strategy

If migration fails:

### 1. Version Control Rollback

```bash
# Rollback code changes
git checkout main
git reset --hard <previous-commit>

# Restore packages
dotnet restore
dotnet build
```

### 2. Data Rollback

```bash
# Restore from backup
# (Depends on your storage backend)

# For Redis
redis-cli --rdb /path/to/backup.rdb

# For PostgreSQL
psql mydb < backup.sql
```

### 3. Deployment Rollback

```bash
# Redeploy previous version
# (Depends on your deployment process)

# Using Docker
docker pull myregistry/myapp:previous-tag
docker run myregistry/myapp:previous-tag

# Using systemd
systemctl restart myapp-old
```

---

## Best Practices

### 1. Always Test in Non-Production First

```bash
# Test in development
dotnet test

# Test in staging
# Deploy to staging environment
# Run smoke tests
# Monitor for issues
```

### 2. Maintain Version Lock

In `.csproj`:
```xml
<!-- Lock to specific version during development -->
<PackageReference Include="Quark.Core" Version="0.1.0-alpha" />

<!-- Or use version range for flexibility -->
<PackageReference Include="Quark.Core" Version="[0.1.0-alpha,0.2.0-alpha)" />
```

### 3. Document Custom Changes

```csharp
// Document why you're doing something non-standard
[Actor]
public class MyActor : ActorBase
{
    // MIGRATION NOTE: We use custom initialization instead of OnActivateAsync
    // due to specific requirements. Review when upgrading to 1.0.
    public void CustomInit()
    {
        // ...
    }
}
```

### 4. Monitor After Upgrade

```csharp
// Add version logging
public override Task OnActivateAsync(CancellationToken cancellationToken = default)
{
    var version = typeof(ActorBase).Assembly.GetName().Version;
    _logger.LogInformation("Actor {ActorId} running on Quark {Version}", 
        ActorId, version);
    
    return base.OnActivateAsync(cancellationToken);
}
```

---

## Getting Help with Migration

### 1. Check Documentation

- **[Release Notes](https://github.com/thnak/Quark/releases)** - What changed
- **[FAQ](FAQ)** - Common issues
- **[Examples](Examples)** - Updated examples

### 2. Community Support

- **[Discussions](https://github.com/thnak/Quark/discussions)** - Ask questions
- **[Issues](https://github.com/thnak/Quark/issues)** - Report bugs

### 3. Migration Assistance

If you encounter issues:

1. **Search existing issues** - Someone may have faced the same problem
2. **Create a discussion** - For migration questions
3. **Open an issue** - For bugs in the migration process

---

## Deprecation Policy (Post-1.0)

When Quark reaches 1.0, deprecated APIs will follow this pattern:

### Phase 1: Deprecation Warning

```csharp
[Obsolete("Use NewMethod instead. This will be removed in version 2.0.")]
public void OldMethod()
{
    // Still works, but warns
}
```

### Phase 2: Grace Period

- Minimum of **one minor version** before removal
- Deprecation notices in documentation
- Migration guide provided

### Phase 3: Removal

- Removed in next **major version**
- Compile-time error if still used
- Migration guide updated

---

## Future-Proofing Your Code

### 1. Depend on Abstractions

```csharp
// Good - depends on interface
private readonly IActorFactory _factory;

// Less flexible - depends on concrete type
private readonly ActorFactory _factory;
```

### 2. Use Latest Language Features Carefully

```csharp
// Be cautious with C# preview features
// They might change or be removed
```

### 3. Follow Quark Conventions

```csharp
// Follow patterns from official examples
// They're updated with each release
```

### 4. Keep Dependencies Updated

```bash
# Regularly update to latest stable
dotnet list package --outdated
dotnet add package Quark.Core --version <latest>
```

---

## Roadmap and Upcoming Changes

### Phase 6: Hosting and Client (In Progress)

**Expected API Changes:**
- New `SiloHostBuilder` API
- Client gateway for remote actor access
- Simplified configuration

**Migration Impact:** Medium
- Manual setup → Builder pattern
- Direct actor access → Client gateway

### Phase 7: Production Hardening (Planned)

**Expected API Changes:**
- Observability APIs (metrics, tracing)
- Enhanced error handling
- Performance optimizations

**Migration Impact:** Low
- Mostly additive changes
- Backward compatible APIs

### Phase 8: Advanced Features (Future)

**Expected API Changes:**
- Event sourcing enhancements
- Additional storage providers
- Transaction support

**Migration Impact:** Low to Medium
- Opt-in features
- Minimal impact on existing code

---

## Version History

### 0.1.0-alpha (January 2026)

**Initial Release**
- Core actor runtime
- Supervision hierarchies
- State persistence
- Timers and reminders
- Reactive streaming
- Clustering
- Source generators

**Known Issues:**
- Hosting layer incomplete
- Limited storage providers
- No automated migration tools

---

## Quick Reference

### Update Commands

```bash
# Update to specific version
dotnet add package Quark.Core --version 0.1.0-alpha

# Update to latest
dotnet add package Quark.Core

# List outdated packages
dotnet list package --outdated

# Clean and rebuild
dotnet clean && dotnet build
```

### Export/Import State

```bash
# Export (using custom tool/script)
dotnet run --project MyApp.Tools export-state --output state.json

# Transform (if needed)
dotnet run --project MyApp.Tools transform-state --input state.json --output state-v2.json

# Import (using custom tool/script)
dotnet run --project MyApp.Tools import-state --input state-v2.json
```

---

## Next Steps

- **[Migration Guides](Migration-Guides)** - Overview of all migration guides
- **[Getting Started](Getting-Started)** - Learn current version
- **[FAQ](FAQ)** - Common questions
- **[Release Notes](https://github.com/thnak/Quark/releases)** - Latest changes

---

**Questions?** Open a [discussion](https://github.com/thnak/Quark/discussions) or [issue](https://github.com/thnak/Quark/issues).
