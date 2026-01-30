# Quark.Placement.Gpu.Cuda

NVIDIA CUDA-specific GPU acceleration for Quark actors. Provides GPU placement and compute capabilities for AI/ML workloads.

## Overview

This package enables actors performing compute-intensive operations to be placed with affinity to specific NVIDIA GPU devices, optimizing performance for AI inference, training, and scientific computing workloads.

## Features

- Automatic CUDA device detection
- GPU utilization and memory tracking
- Multiple device selection strategies (LeastUtilized, RoundRobin, etc.)
- GPU memory pooling support
- CPU fallback when GPU unavailable

## Installation

```bash
dotnet add package Quark.Placement.Gpu.Cuda
```

## Requirements

- NVIDIA GPU with CUDA support (Compute Capability 6.0+)
- NVIDIA drivers installed
- CUDA Toolkit (optional, for development)

## Usage

```csharp
using Quark.Extensions.DependencyInjection;
using Quark.Placement.Abstractions;
using Quark.Placement.Gpu.Cuda;

// In your Startup.cs or Program.cs:
services.AddGpuAcceleration(options =>
{
    options.Enabled = true;
    options.PreferredBackend = "cuda";
    options.DeviceSelectionStrategy = "LeastUtilized";
    
    // Specify which actor types should use GPU
    options.AcceleratedActorTypes = new List<string>
    {
        "InferenceActor",
        "TrainingActor",
        "ImageProcessingActor"
    };
    
    options.AllowCpuFallback = true;
});

// Register the CUDA-specific implementation
services.AddSingleton<IGpuPlacementStrategy, CudaGpuPlacementStrategy>();
```

## Supported Workflows

- **AI/ML Inference**: Place inference actors with GPU affinity
- **Batch Processing**: Distribute compute-heavy actors across multiple GPUs
- **Real-time Analytics**: GPU-accelerated data processing actors

## Device Selection Strategies

- **LeastUtilized**: Prefer GPU with lowest compute utilization
- **LeastMemoryUsed**: Prefer GPU with most available memory
- **RoundRobin**: Distribute actors evenly across GPUs
- **FirstAvailable**: Always use the first available GPU

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

## AOT Compatibility

⚠️ This package is **NOT** AOT-compatible. It uses CUDA Runtime APIs and native libraries.

## License

MIT License
