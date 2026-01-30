# Quark.Examples.Placement

This example demonstrates advanced placement strategies in Quark, including NUMA (Non-Uniform Memory Access) optimization and GPU acceleration.

## What This Example Shows

1. **NUMA Optimization Configuration**
   - Enabling NUMA-aware actor placement
   - Configuring affinity groups for co-locating related actors
   - Setting CPU and memory utilization thresholds
   - Detecting NUMA topology on multi-socket systems

2. **GPU Acceleration Configuration**
   - Enabling GPU-accelerated actor placement
   - Selecting GPU backend (CUDA)
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
2. Configure GPU acceleration for specific actor types
3. Detect available NUMA nodes (or create a virtual node)
4. Detect available GPU devices (if present)
5. Display topology information

## Key Concepts

### NUMA Optimization

NUMA (Non-Uniform Memory Access) is a computer memory design where memory access time depends on the memory location relative to the processor. On multi-socket systems:
- Each socket has local memory with fast access
- Remote memory (on other sockets) has higher latency
- Proper actor placement reduces cross-socket memory traffic

**Benefits:**
- 20-40% reduction in memory latency
- 10-30% improvement in throughput
- Better CPU cache utilization

### GPU Acceleration

GPU acceleration allows compute-intensive actors to leverage GPU hardware:
- AI/ML inference and training
- Scientific computing
- Image/video processing
- Large-scale data transformations

**Benefits:**
- 10-100x speedup for AI workloads
- Efficient parallel processing
- Reduced CPU load

## Production Usage

In production environments, you would:

1. **For NUMA Optimization:**
   ```csharp
   services.AddNumaOptimization(options =>
   {
       options.Enabled = true;
       options.BalancedPlacement = true;
       
       // Define affinity groups based on your actor communication patterns
       options.AffinityGroups["PaymentProcessing"] = new List<string>
       {
           "PaymentActor",
           "FraudDetectionActor",
           "NotificationActor"
       };
   });
   
   services.AddSingleton<INumaPlacementStrategy, LinuxNumaPlacementStrategy>();
   ```

2. **For GPU Acceleration:**
   ```csharp
   services.AddGpuAcceleration(options =>
   {
       options.Enabled = true;
       options.AcceleratedActorTypes = new List<string>
       {
           "InferenceActor",
           "ImageProcessingActor"
       };
   });
   
   services.AddSingleton<IGpuPlacementStrategy, CudaGpuPlacementStrategy>();
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
