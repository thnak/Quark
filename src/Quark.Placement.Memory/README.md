# Quark.Placement.Memory

Memory-aware actor placement and rebalancing for Quark Framework.

## Overview

This package provides memory-conscious load balancing that prevents out-of-memory (OOM) conditions by monitoring memory usage and proactively migrating actors from memory-constrained silos.

## Features

- **Memory Monitoring**: Tracks heap memory usage, GC metrics, and memory pressure
- **Smart Placement**: Avoids placing actors on silos with high memory usage
- **Proactive Rebalancing**: Automatically migrates actors from memory-constrained silos
- **OOM Prevention**: Rejects new actor placement when memory is critical
- **Configurable Thresholds**: Control when warnings and migrations are triggered
- **Migration Cooldown**: Prevents thrashing by rate-limiting migrations

## Usage

```csharp
// Register services
services.AddSingleton<IMemoryMonitor, MemoryMonitor>();
services.Configure<MemoryAwarePlacementOptions>(options =>
{
    options.WarningThresholdBytes = 1024L * 1024 * 1024;  // 1 GB
    options.CriticalThresholdBytes = 1536L * 1024 * 1024; // 1.5 GB
    options.MemoryPressureThreshold = 0.7;  // 70% utilization
});
services.AddSingleton<IPlacementPolicy, MemoryAwarePlacementPolicy>();
services.AddSingleton<IActorRebalancer, MemoryRebalancingCoordinator>();
```

## Configuration Options

- **WarningThresholdBytes**: Memory usage level that triggers warnings (default: 1 GB)
- **CriticalThresholdBytes**: Memory usage level that triggers rebalancing (default: 1.5 GB)
- **MemoryPressureThreshold**: Memory pressure ratio (0.0-1.0) that affects placement (default: 0.7)
- **MemoryReservationPercentage**: Safety buffer percentage (default: 0.2 = 20%)
- **RejectPlacementOnCriticalMemory**: Whether to reject placements at critical memory (default: true)

## How It Works

### Memory Monitoring
1. `MemoryMonitor` tracks per-actor memory usage
2. Collects system-level metrics: total memory, available memory, GC stats
3. Calculates memory pressure (0.0 = no pressure, 1.0 = critical)

### Placement Policy
1. When placing an actor, checks current memory pressure
2. If pressure is high, prefers silos with lower memory usage
3. If pressure is critical, may reject placement to prevent OOM

### Proactive Rebalancing
1. `MemoryRebalancingCoordinator` periodically evaluates memory pressure
2. When pressure exceeds threshold, identifies top memory-consuming actors
3. Migrates actors to silos with more available memory
4. Uses cooldown period to prevent migration thrashing

## Performance Considerations

- Lightweight monitoring with minimal overhead
- Uses GC memory info APIs (no reflection)
- Cached metrics to avoid repeated system calls
- Migration cooldown prevents excessive rebalancing

## AOT Compatibility

✅ Fully compatible with Native AOT compilation. Uses only GC APIs and diagnostics.

## Integration with Profiling

This package integrates with `Quark.Profiling.Abstractions` to track actor memory usage over time. Enable profiling for more accurate memory estimates.
