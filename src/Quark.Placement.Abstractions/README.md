# Quark.Placement.Abstractions

Core abstractions for advanced actor placement strategies in Quark, including NUMA and GPU optimizations.

## Overview

This package provides the interfaces and configuration classes for implementing custom actor placement strategies that optimize performance based on hardware topology.

## Key Interfaces

### `INumaPlacementStrategy`
Defines a strategy for NUMA-aware (Non-Uniform Memory Access) actor placement. NUMA optimization co-locates related actors on the same NUMA node to minimize memory access latency.

### `IGpuPlacementStrategy`
Defines a strategy for GPU-affinity actor placement. Enables actors performing compute-intensive operations (AI/ML, scientific computing) to be placed with affinity to specific GPU devices.

## Configuration Classes

### `NumaOptimizationOptions`
Configuration for NUMA-aware placement including:
- Affinity groups for co-locating related actors
- Load balancing strategies
- Node utilization thresholds
- Metrics refresh intervals

### `GpuAccelerationOptions`
Configuration for GPU acceleration including:
- Preferred GPU backend (CUDA, OpenCL)
- Device selection strategies (LeastUtilized, RoundRobin, etc.)
- Memory and compute utilization thresholds
- CPU fallback options

## Usage

This package provides only the abstractions. To use NUMA or GPU optimization:

1. Add this package: `Quark.Placement.Abstractions`
2. Add a platform-specific implementation:
   - For NUMA on Linux: `Quark.Placement.Numa.Linux`
   - For NUMA on Windows: `Quark.Placement.Numa.Windows`
   - For GPU with NVIDIA CUDA: `Quark.Placement.Gpu.Cuda`

## AOT Compatibility

This package is AOT-compatible. However, platform-specific implementations may use reflection or P/Invoke and are not AOT-compatible.

## License

MIT License
