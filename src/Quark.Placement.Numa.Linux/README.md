# Quark.Placement.Numa.Linux

Linux-specific NUMA-aware actor placement for Quark. Uses Linux kernel APIs (`/sys/devices/system/node/`) for NUMA topology detection.

## Overview

This package provides NUMA (Non-Uniform Memory Access) optimization for actor placement on Linux systems with multi-socket CPUs. By intelligently placing actors on specific NUMA nodes, it minimizes memory access latency and maximizes CPU cache efficiency.

## Features

- Automatic NUMA topology detection using Linux sysfs
- Per-NUMA-node CPU and memory metrics
- Affinity group support for co-locating related actors
- Load-balanced or sequential placement strategies
- Real-time node utilization tracking

## Installation

```bash
dotnet add package Quark.Placement.Numa.Linux
```

## Usage

```csharp
using Quark.Extensions.DependencyInjection;
using Quark.Placement.Abstractions;
using Quark.Placement.Numa.Linux;

// In your Startup.cs or Program.cs:
services.AddNumaOptimization(options =>
{
    options.Enabled = true;
    options.BalancedPlacement = true;
    
    // Define affinity groups - actors in the same group will be co-located
    options.AffinityGroups["OrderProcessing"] = new List<string>
    {
        "OrderActor",
        "InventoryActor",
        "PaymentActor"
    };
});

// Register the Linux-specific implementation
services.AddSingleton<INumaPlacementStrategy, LinuxNumaPlacementStrategy>();
```

## Requirements

- Linux kernel 2.6.18 or later (with NUMA support)
- Multi-socket CPU (or system with NUMA topology)
- Read access to `/sys/devices/system/node/`

## How It Works

1. **Topology Detection**: Scans `/sys/devices/system/node/` to discover NUMA nodes
2. **Metrics Collection**: Reads CPU lists and memory info from sysfs
3. **Placement Decision**: Selects optimal NUMA node based on strategy and current utilization
4. **Affinity Tracking**: Maintains actor-to-node mappings for affinity group enforcement

## Performance Impact

On NUMA systems, proper actor placement can provide:
- 20-40% reduction in memory latency
- 10-30% improvement in throughput for memory-intensive workloads
- Better CPU cache utilization

## AOT Compatibility

⚠️ This package is **NOT** AOT-compatible. It uses platform-specific APIs that require runtime reflection.

## License

MIT License
