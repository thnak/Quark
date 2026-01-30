# Quark.Placement.Gpu.Cuda

NVIDIA CUDA-specific GPU acceleration for Quark actors. Provides GPU placement and compute capabilities for AI/ML workloads.

## Overview

This package enables actors performing compute-intensive operations to be placed with affinity to specific NVIDIA GPU devices, optimizing performance for AI inference, training, and scientific computing workloads.

## Features

- Automatic CUDA device detection
- GPU utilization and memory tracking
- Multiple device selection strategies (enum-based, type-safe)
- GPU memory pooling support
- CPU fallback when GPU unavailable
- Compile-time actor discovery with `[GpuBound]` attribute

## Installation

```bash
dotnet add package Quark.Placement.Gpu.Cuda
```

## Requirements

- NVIDIA GPU with CUDA support (Compute Capability 6.0+)
- NVIDIA drivers installed
- CUDA Toolkit (optional, for development)

## Usage

### 1. Mark Actors with [GpuBound] Attribute

```csharp
using Quark.Abstractions;
using Quark.Core.Actors;
using Quark.Placement.Abstractions;

[Actor]
[GpuBound]  // Source generator will discover this
public class InferenceActor : ActorBase
{
    public InferenceActor(string actorId) : base(actorId) { }
    
    // Actor implementation
}
```

### 2. Configure GPU Acceleration

```csharp
using Quark.Extensions.DependencyInjection;
using Quark.Placement.Abstractions;
using Quark.Placement.Gpu.Cuda;
using Quark.Generated;  // For source-generated types

services.AddGpuAcceleration(options =>
{
    options.Enabled = true;
    options.PreferredBackend = GpuBackend.Cuda;  // Enum-based
    options.DeviceSelectionStrategy = GpuDeviceSelectionStrategy.LeastUtilized;
    
    // Use source-generated list of GPU-bound actors (zero reflection!)
    options.AcceleratedActorTypes = MyAssemblyAcceleratedActorTypes.All;
    
    options.AllowCpuFallback = true;
});

// Register the CUDA-specific implementation
services.AddSingleton<IGpuPlacementStrategy, CudaGpuPlacementStrategy>();
```

## Configuration Options

### GpuBackend Enum
- `Auto` - Automatically detect the best available backend
- `Cuda` - NVIDIA CUDA backend
- `OpenCL` - OpenCL backend (cross-platform, when available)

### GpuDeviceSelectionStrategy Enum
- `LeastUtilized` - Prefer GPU with lowest compute utilization
- `LeastMemoryUsed` - Prefer GPU with most available memory
- `RoundRobin` - Distribute actors evenly across GPUs
- `FirstAvailable` - Always use the first available GPU

## Source Generator

The `[GpuBound]` attribute works with Quark's source generator to create a compile-time list of all GPU-bound actors:

```csharp
// Automatically generated in Quark.Generated namespace:
public static class MyAssemblyAcceleratedActorTypes
{
    public static IReadOnlySet<Type> All { get; } = new HashSet<Type>
    {
        typeof(InferenceActor),
        typeof(ImageProcessingActor),
        // ... all actors marked with [GpuBound]
    };
}
```

**Benefits:**
- ✅ Zero reflection - all type discovery at compile time
- ✅ Type-safe - no string-based actor type names
- ✅ Easy configuration - just use `.All` property
- ✅ Fast lookups - uses `HashSet<Type>` internally

## Supported Workflows

- **AI/ML Inference**: Place inference actors with GPU affinity
- **Batch Processing**: Distribute compute-heavy actors across multiple GPUs
- **Real-time Analytics**: GPU-accelerated data processing actors

## Current Implementation Status

⚠️ **Note**: The current implementation is a placeholder that provides the framework for CUDA integration. In production, this would use:
- CUDA Runtime API for device queries
- NVML (NVIDIA Management Library) for detailed metrics
- `nvidia-smi` for device management

## Performance Impact

GPU acceleration can provide:
- 10-100x speedup for AI inference workloads
- 5-50x speedup for matrix operations
- Significant throughput improvements for parallel compute tasks

## Migration from String-Based Configuration

**Before:**
```csharp
options.PreferredBackend = "cuda";
options.DeviceSelectionStrategy = "LeastUtilized";
options.AcceleratedActorTypes = new List<string> { "InferenceActor" };
```

**After:**
```csharp
options.PreferredBackend = GpuBackend.Cuda;
options.DeviceSelectionStrategy = GpuDeviceSelectionStrategy.LeastUtilized;
options.AcceleratedActorTypes = MyAssemblyAcceleratedActorTypes.All;
```

## AOT Compatibility

⚠️ This package is **NOT** AOT-compatible. It uses CUDA Runtime APIs and native libraries.

## License

MIT License
