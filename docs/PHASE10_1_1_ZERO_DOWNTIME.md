# Phase 10.1.1: Zero Downtime & Rolling Upgrades

This document describes the implementation status and usage of Phase 10.1.1 features for zero-downtime deployments and rolling upgrades.

## Overview

Phase 10.1.1 enables enterprise-grade deployment capabilities for production updates without service disruption. The implementation is split into three main areas:

1. **Graceful Shutdown** ✅ COMPLETED (already implemented in QuarkSilo)
2. **Live Actor Migration** 🚧 PARTIAL (abstractions and core implementations complete)
3. **Version-Aware Placement** 🚧 PARTIAL (abstractions and core implementations complete)

## Features Implemented

### 1. Graceful Shutdown ✅

Already implemented in `Quark.Hosting.QuarkSilo`. Features include:

- Stop accepting new actor activations on termination signal
- Configurable shutdown timeout via `QuarkSiloOptions.ShutdownTimeout`
- Actor deactivation with state persistence
- ReminderTickManager graceful stop
- Transport layer coordination
- Cluster membership coordination

**Usage:**

```csharp
services.Configure<QuarkSiloOptions>(options =>
{
    options.ShutdownTimeout = TimeSpan.FromSeconds(60); // Custom timeout
    options.EnableReminders = true; // Ensure reminder cleanup
});
```

### 2. Hot Actor Detection ✅

Track actor activity levels to identify "hot" (actively processing) and "cold" (idle) actors for migration prioritization.

**Interfaces:**
- `IActorActivityTracker` - Tracks message queue depth, active calls, stream subscriptions
- `ActorActivityMetrics` - Activity metrics with hot/cold detection

**Implementation:**
- `ActorActivityTracker` - Concurrent, thread-safe activity tracking
- Activity score calculation (0.0-1.0) based on multiple factors
- Migration priority sorting (cold actors migrate first)

**Usage:**

```csharp
// Register the service
services.AddActorActivityTracking();

// Use the tracker (typically done by framework internals)
var tracker = serviceProvider.GetRequiredService<IActorActivityTracker>();

// Record activity
tracker.RecordMessageEnqueued("actor-1", "TestActor");
tracker.RecordCallStarted("actor-1", "TestActor");

// Get metrics
var metrics = await tracker.GetActivityMetricsAsync("actor-1");
Console.WriteLine($"Actor is {(metrics.IsHot ? "hot" : "cold")}");

// Get migration priority list (cold actors first)
var priorityList = await tracker.GetMigrationPriorityListAsync();
```

### 3. Actor Migration Coordination ✅

Orchestrate actor migration with drain pattern, state transfer, and activation on target silo.

**Interfaces:**
- `IActorMigrationCoordinator` - Coordinates the migration lifecycle
- `MigrationResult` - Result of migration operation
- `MigrationStatus` - Status enum (NotStarted, InProgress, Completed, Failed, Cancelled)

**Implementation:**
- `ActorMigrationCoordinator` - Full migration lifecycle
- Drain pattern (stop routing new messages)
- Wait for in-flight operations to complete
- State transfer coordination (uses existing persistent storage)
- Reminder migration support

**Usage:**

```csharp
// Register the service
services.AddActorMigration();

// Migrate an actor
var coordinator = serviceProvider.GetRequiredService<IActorMigrationCoordinator>();
var result = await coordinator.MigrateActorAsync(
    actorId: "order-123",
    actorType: "OrderActor",
    targetSiloId: "silo-2");

if (result.IsSuccessful)
{
    Console.WriteLine($"Successfully migrated {result.ActorId}");
}
else
{
    Console.WriteLine($"Migration failed: {result.ErrorMessage}");
}

// Check migration status
var status = await coordinator.GetMigrationStatusAsync("order-123");
if (status == MigrationStatus.InProgress)
{
    Console.WriteLine("Migration in progress...");
}
```

### 4. Version-Aware Placement ✅

Track assembly versions across silos and enable version-compatible actor placement during rolling upgrades.

**Interfaces:**
- `IVersionTracker` - Track actor type versions across silos
- `IVersionCompatibilityChecker` - Determine version compatibility
- `AssemblyVersionInfo` - Version information for actor types
- `VersionCompatibilityMode` - Compatibility modes (Strict, Patch, Minor, Major)

**Implementation:**
- `VersionTracker` - Cluster-wide version tracking
- `VersionCompatibilityChecker` - Multi-mode version comparison
- Silo capability registration and discovery
- Compatible silo finding

**Usage:**

```csharp
// Register the service
services.AddVersionAwarePlacement();

// Register versions for current silo
var tracker = serviceProvider.GetRequiredService<IVersionTracker>();
var versions = new Dictionary<string, AssemblyVersionInfo>
{
    ["OrderActor"] = new AssemblyVersionInfo("2.1.0", "OrderService"),
    ["PaymentActor"] = new AssemblyVersionInfo("1.5.0", "PaymentService")
};
await tracker.RegisterSiloVersionsAsync(versions);

// Find compatible silos for an actor type
var compatibleSilos = await tracker.FindCompatibleSilosAsync("OrderActor", "2.1.0");

// Check version compatibility
var checker = serviceProvider.GetRequiredService<IVersionCompatibilityChecker>();
bool compatible = checker.AreVersionsCompatible(
    requestedVersion: "2.1.0",
    availableVersion: "2.0.0",
    VersionCompatibilityMode.Minor); // true (2.x compatible)
```

### 5. Convenience Extension Method ✅

Register all Phase 10.1.1 services at once:

```csharp
services.AddZeroDowntimeUpgrades();
```

This is equivalent to:

```csharp
services.AddActorActivityTracking();
services.AddActorMigration();
services.AddVersionAwarePlacement();
```

## Configuration

QuarkSilo already supports configuration options for migration features:

```csharp
services.Configure<QuarkSiloOptions>(options =>
{
    // Graceful shutdown (IMPLEMENTED)
    options.ShutdownTimeout = TimeSpan.FromSeconds(60);
    
    // Live migration (PLANNED - configuration options ready)
    options.EnableLiveMigration = true;
    options.MigrationTimeout = TimeSpan.FromSeconds(30);
    options.MaxConcurrentMigrations = 10;
    
    // Version-aware placement (PLANNED - configuration options ready)
    options.EnableVersionAwarePlacement = true;
    options.AssemblyVersion = "2.1.0";
});
```

## Testing

All implementations include comprehensive unit tests:

- **ActorActivityTrackerTests**: 12 tests covering activity tracking, hot/cold detection, priority ordering
- **VersionCompatibilityCheckerTests**: 19 tests covering all compatibility modes and edge cases
- **ActorMigrationCoordinatorTests**: 13 tests covering migration lifecycle, status tracking, reminder migration
- **VersionTrackerTests**: 17 tests covering version registration, capability tracking, silo discovery

**Total: 61 new tests, all passing ✅**

Run tests:

```bash
dotnet test --filter "FullyQualifiedName~ActorActivityTrackerTests"
dotnet test --filter "FullyQualifiedName~VersionCompatibilityCheckerTests"
dotnet test --filter "FullyQualifiedName~ActorMigrationCoordinatorTests"
dotnet test --filter "FullyQualifiedName~VersionTrackerTests"
```

## Remaining Work

### Integration Tasks

1. **Mailbox Integration**: ✅ COMPLETED - Wire up `IActorActivityTracker` with mailbox operations to automatically track message queue depth
2. **Silo Lifecycle Integration**: Integrate migration coordinator with QuarkSilo shutdown sequence
3. **Automatic Version Detection**: ✅ COMPLETED - Auto-detect assembly versions on silo startup using source generator
4. **Cluster Synchronization**: Sync version information via Redis cluster membership

### Advanced Features

1. **E-Tag Concurrency**: Implement atomic state transfer with optimistic concurrency control
2. **Message Queue Migration**: Capture and replay in-flight messages on target silo
3. **Timer Migration**: Transfer timer state during migration
4. **Cross-Silo Protocol**: Direct communication between silos for migration coordination

### Integration Tests

1. End-to-end migration flow tests
2. Version-aware placement in multi-silo cluster
3. Rolling upgrade scenarios with version compatibility

## Automatic Version Detection ✅

Phase 10.1.1 now includes automatic assembly version detection via source generation. The `AssemblyVersionSourceGenerator` creates a compile-time registry of all actor types and their versions.

### How It Works

1. **Compile-Time Generation**: The source generator scans for classes with `[Actor]` attribute
2. **FrozenDictionary Creation**: Generates `Quark.Generated.ActorVersionRegistry` with a FrozenDictionary mapping actor types to versions
3. **Zero Reflection**: Completely AOT-compatible, no runtime reflection required
4. **Automatic Registration**: Use the `RegisterActorVersions` extension method to register detected versions

### Usage

**Option 1: Automatic Registration (Recommended)**

```csharp
// Add version-aware placement
services.AddVersionAwarePlacement();

// Automatically register all actor types from the generated registry
services.RegisterActorVersions(Quark.Generated.ActorVersionRegistry.VersionMap);
```

**Option 2: Manual Registration**

```csharp
// Add version-aware placement
services.AddVersionAwarePlacement();

// Manually specify versions for selective registration
var versions = new Dictionary<string, AssemblyVersionInfo>
{
    ["OrderActor"] = new AssemblyVersionInfo("2.1.0", "OrderService"),
    ["PaymentActor"] = new AssemblyVersionInfo("1.5.0", "PaymentService")
};
await versionTracker.RegisterSiloVersionsAsync(versions);
```

### Generated Code Example

The source generator creates code like this at compile-time:

```csharp
// <auto-generated/>
namespace Quark.Generated
{
    public static class ActorVersionRegistry
    {
        public static FrozenDictionary<string, AssemblyVersionInfo> VersionMap
        {
            get
            {
                return new Dictionary<string, AssemblyVersionInfo>
                {
                    ["CounterActor"] = new AssemblyVersionInfo("0.1.0", "MyApp"),
                    ["OrderActor"] = new AssemblyVersionInfo("0.1.0", "MyApp")
                }.ToFrozenDictionary();
            }
        }
        
        public static AssemblyVersionInfo? GetVersion(string actorType) { /* ... */ }
        public static IReadOnlyCollection<string> GetAllActorTypes() { /* ... */ }
    }
}
```

### Benefits

- **Zero Reflection**: Fully AOT-compatible with no runtime performance cost
- **Type-Safe**: Compile-time generation ensures accuracy
- **Zero Configuration**: Versions extracted from assembly metadata automatically
- **Efficient Lookup**: Uses FrozenDictionary for optimal read performance
- **No Manual Maintenance**: Actor versions stay in sync with assembly version

## Mailbox Activity Tracking Integration ✅

Phase 10.1.1 now includes automatic activity tracking at the mailbox level. The `ChannelMailbox` class has been enhanced to optionally integrate with `IActorActivityTracker` for real-time monitoring of actor activity.

### How It Works

1. **Optional Dependency Injection**: `ChannelMailbox` constructor accepts optional `IActorActivityTracker` and actor type name
2. **Message Enqueue Tracking**: When a message is posted to the mailbox, `RecordMessageEnqueued` is called
3. **Message Dequeue Tracking**: When a message begins processing, `RecordMessageDequeued` is called
4. **Call Lifecycle Tracking**: 
   - `RecordCallStarted` when processing begins
   - `RecordCallCompleted` when processing finishes (in finally block)
5. **Cleanup**: When mailbox is disposed, actor is automatically removed from tracker

### Usage

```csharp
// Create mailbox with activity tracking enabled
var activityTracker = serviceProvider.GetRequiredService<IActorActivityTracker>();
var mailbox = new ChannelMailbox(
    actor,
    capacity: 1000,
    deadLetterQueue: deadLetterQueue,
    activityTracker: activityTracker,  // Optional
    actorType: "OrderActor");           // Optional

// Mailbox automatically tracks:
// - Message enqueue (when PostAsync is called)
// - Message dequeue (when processing starts)
// - Call start (when method invocation begins)
// - Call completion (when method invocation ends)
// - Actor removal (when mailbox is disposed)
```

### Key Features

- **Zero Overhead When Disabled**: If `activityTracker` is null, no tracking code executes
- **Non-Invasive**: Existing mailbox functionality unchanged, tracking is additive
- **Thread-Safe**: All tracking operations use thread-safe `IActorActivityTracker` methods
- **Automatic Cleanup**: Actors automatically removed from tracker on disposal
- **Real-Time Metrics**: Activity data available immediately for migration decisions

### Integration Points

The mailbox integration enables automatic population of:
- **Queue Depth**: Tracked via enqueue/dequeue calls
- **Active Call Count**: Tracked via call start/completion
- **Last Activity Time**: Updated on every tracking call
- **Activity Score**: Calculated automatically based on metrics

This automation means applications don't need manual instrumentation - simply provide the tracker and actor type, and the framework handles the rest.



## Architecture Notes

### AOT Compatibility ✅

All implementations are fully AOT-compatible:
- No reflection usage
- No runtime IL emission
- Concurrent data structures for thread safety
- Value types and structs where appropriate

### Performance

- Activity tracking uses `Interlocked` operations for lock-free updates
- Version checking uses pre-parsed version tuples
- Migration coordination tracks active migrations efficiently
- All operations are async and cancellable

### State Persistence

The existing state storage infrastructure already supports:
- Optimistic concurrency via `SaveWithVersionAsync` and E-Tags
- Atomic state operations
- This eliminates the need for custom state transfer logic

### Reminders

Reminders are persisted in `IReminderTable` and survive migration automatically. The migration coordinator re-registers reminders to ensure consistency after migration.

## Example Scenarios

### Scenario 1: Graceful Shutdown

```csharp
// On SIGTERM or Ctrl+C
// QuarkSilo automatically:
// 1. Marks silo as ShuttingDown
// 2. Stops accepting new activations
// 3. Deactivates all actors (calls OnDeactivateAsync)
// 4. Waits for in-flight operations (up to ShutdownTimeout)
// 5. Stops transport and cluster membership
// 6. Unregisters from cluster
```

### Scenario 2: Rolling Upgrade

```csharp
// Phase 1: Deploy new silos (v2.1.0) alongside old silos (v2.0.0)
var newSiloVersions = new Dictionary<string, AssemblyVersionInfo>
{
    ["OrderActor"] = new AssemblyVersionInfo("2.1.0")
};
await versionTracker.RegisterSiloVersionsAsync(newSiloVersions);

// Phase 2: Migrate cold actors first
var priorityList = await activityTracker.GetMigrationPriorityListAsync();
foreach (var metrics in priorityList.Where(m => m.IsCold))
{
    await migrationCoordinator.MigrateActorAsync(
        metrics.ActorId,
        metrics.ActorType,
        targetSiloId: "new-silo-1");
}

// Phase 3: Gracefully shutdown old silos
// (actors are already migrated, shutdown is quick)
```

### Scenario 3: Canary Deployment

```csharp
// Deploy canary silo with v2.2.0-beta
var canarySilo = new Dictionary<string, AssemblyVersionInfo>
{
    ["OrderActor"] = new AssemblyVersionInfo("2.2.0-beta")
};
versionTracker.UpdateSiloCapabilities("canary-silo", canarySilo);

// Route 10% of new activations to canary silo
// (requires placement strategy integration - planned)
```

## References

- **ENHANCEMENTS.md**: Section 10.1.1 - Zero Downtime & Rolling Upgrades
- **QuarkSilo.cs**: Graceful shutdown implementation
- **IActorRebalancer.cs**: Integration point for migration decisions
- **IStateStorage.cs**: Optimistic concurrency support
