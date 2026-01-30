# Quark.Examples.Placement

This example demonstrates advanced placement strategies in Quark, including NUMA (Non-Uniform Memory Access) optimization and GPU acceleration with improved type-safe configuration.

## What This Example Shows

1. **NUMA Optimization Configuration**
   - Enabling NUMA-aware actor placement
   - Configuring affinity groups for co-locating related actors
   - Setting CPU and memory utilization thresholds
   - Detecting NUMA topology on multi-socket systems

2. **GPU Acceleration Configuration (Improved)**
   - **Enum-based configuration** for type safety
   - Using `[GpuBound]` attribute for actor marking
   - **Source-generated actor type lists** (zero reflection!)
   - Configuring device selection strategies
   - Enabling CPU fallback when GPU unavailable

3. **Hardware Detection**
   - Detecting available NUMA nodes and their properties
   - Detecting available GPU devices and their capabilities
   - Querying device utilization metrics

## Running the Example

```bash
cd examples/Quark.Examples.Placement
dotnet run
```

## Expected Output

The example will:
1. Configure NUMA optimization with affinity groups
2. Configure GPU acceleration for GPU-bound actors
3. Detect available NUMA nodes (or create a virtual node)
4. Detect available GPU devices (if present)
5. Display topology information
6. Show that 1 GPU-bound actor was discovered via source generation

## Improvements in This Version

### 1. Enum-Based Configuration (Type-Safe)
**Before (string-based):**
```csharp
options.PreferredBackend = "cuda";  // Typo-prone
options.DeviceSelectionStrategy = "LeastUtilized";  // No IntelliSense
```

**After (enum-based):**
```csharp
options.PreferredBackend = GpuBackend.Cuda;  // Type-safe
options.DeviceSelectionStrategy = GpuDeviceSelectionStrategy.LeastUtilized;  // IntelliSense
```

### 2. Source-Generated Actor Lists (Zero Reflection)
**Before (string-based):**
```csharp
options.AcceleratedActorTypes = new List<string>
{
    "InferenceActor",  // Manual maintenance, error-prone
};
```

**After (attribute + source generator):**
```csharp
[Actor]
[GpuBound]  // Mark actors that need GPU
public class InferenceActor : ActorBase { }

// In configuration:
options.AcceleratedActorTypes = Quark_Examples_PlacementAcceleratedActorTypes.All;
// Automatically discovered at compile-time, zero reflection!
```

## Production Usage

```csharp
// Mark actors with [GpuBound]
[Actor]
[GpuBound]
public class InferenceActor : ActorBase { }

// Configure with enums and generated types
services.AddGpuAcceleration(options =>
{
    options.Enabled = true;
    options.PreferredBackend = GpuBackend.Cuda;
    options.DeviceSelectionStrategy = GpuDeviceSelectionStrategy.LeastUtilized;
    
    // Use source-generated list (compile-time discovery, zero reflection)
    options.AcceleratedActorTypes = MyAssemblyAcceleratedActorTypes.All;
});

services.AddSingleton<IGpuPlacementStrategy, CudaGpuPlacementStrategy>();
```

## Source Generator Benefits

The `[GpuBound]` attribute works with Quark's source generator to:

1. **Discover GPU-bound actors at compile time** - No reflection needed
2. **Generate type-safe collections** - Uses `IReadOnlySet<Type>` for fast lookups
3. **Provide easy configuration** - Just use `{Assembly}AcceleratedActorTypes.All`
4. **Enable IntelliSense** - Fully discoverable in IDE

Generated code example:
```csharp
// Auto-generated in Quark.Generated namespace
public static class Quark_Examples_PlacementAcceleratedActorTypes
{
    public static IReadOnlySet<Type> All { get; } = new HashSet<Type>
    {
        typeof(Quark.Examples.Placement.InferenceActor),
        // ... all [GpuBound] actors
    };
}
```

## Platform Support

- **NUMA Optimization:**
  - Linux: `Quark.Placement.Numa.Linux` (primary)
  - Windows: `Quark.Placement.Numa.Windows` (secondary)

- **GPU Acceleration:**
  - NVIDIA CUDA: `Quark.Placement.Gpu.Cuda`
  - OpenCL: Coming soon

## Notes

- These features are **optional plugins** - they don't affect the core framework
- They are **NOT AOT-compatible** due to platform-specific APIs
- Designed for production workloads on specialized hardware
- Gracefully degrade when hardware features are unavailable
- **Zero reflection** - All type discovery happens at compile time
