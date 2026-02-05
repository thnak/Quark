using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Quark.Extensions.DependencyInjection;
using Quark.Placement.Abstractions;
using Quark.Placement.Numa.Linux;
using Quark.Placement.Gpu.Cuda;

namespace Quark.Examples.Placement;

/// <summary>
/// Example demonstrating advanced placement strategies (NUMA and GPU optimization).
/// This example shows how to configure NUMA-aware and GPU-accelerated actor placement.
/// </summary>
public class Program
{
    public static async Task Main(string[] args)
    {
        Console.WriteLine("=== Quark Advanced Placement Strategies Example ===\n");

        var host = Host.CreateDefaultBuilder(args)
            .ConfigureServices((context, services) =>
            {
                // Configure NUMA optimization
                services.AddNumaOptimization(options =>
                {
                    options.Enabled = true;
                    options.BalancedPlacement = true;
                    options.NodeCpuThreshold = 0.90;
                    options.NodeMemoryThreshold = 0.85;
                    
                    // Define affinity groups - actors in the same group will be co-located
                    options.AffinityGroups["DataProcessing"] = new List<string>
                    {
                        "DataLoaderActor",
                        "DataTransformerActor",
                        "DataWriterActor"
                    };
                    
                    Console.WriteLine("✓ NUMA optimization configured");
                    Console.WriteLine($"  - Balanced placement: {options.BalancedPlacement}");
                    Console.WriteLine($"  - CPU threshold: {options.NodeCpuThreshold * 100}%");
                    Console.WriteLine($"  - Memory threshold: {options.NodeMemoryThreshold * 100}%");
                    Console.WriteLine($"  - Affinity groups: {options.AffinityGroups.Count}");
                });

                // Register Linux-specific NUMA implementation
                services.AddSingleton<INumaPlacementStrategy, LinuxNumaPlacementStrategy>();
                Console.WriteLine("✓ Linux NUMA strategy registered\n");

                // Configure GPU acceleration
                services.AddGpuAcceleration(options =>
                {
                    options.Enabled = true;
                    options.PreferredBackend = GpuBackend.Cuda;
                    options.DeviceSelectionStrategy = GpuDeviceSelectionStrategy.LeastUtilized;
                    options.AllowCpuFallback = true;
                    
                    // Note: Source-generated list of GPU-bound actors would go here
                    // options.AcceleratedActorTypes = Quark_Examples_PlacementAcceleratedActorTypes.All;
                    // For this example, the GpuBoundSourceGenerator would generate this if actors are marked with [GpuBound]
                    
                    Console.WriteLine("✓ GPU acceleration configured");
                    Console.WriteLine($"  - Backend: {options.PreferredBackend}");
                    Console.WriteLine($"  - Strategy: {options.DeviceSelectionStrategy}");
                    Console.WriteLine($"  - CPU fallback: {options.AllowCpuFallback}");
                    Console.WriteLine($"  - Accelerated types: {options.AcceleratedActorTypes?.Count ?? 0}");
                });

                // Register CUDA-specific GPU implementation
                services.AddSingleton<IGpuPlacementStrategy, CudaGpuPlacementStrategy>();
                Console.WriteLine("✓ CUDA GPU strategy registered\n");
            })
            .Build();

        // Demonstrate NUMA node detection
        var numaStrategy = host.Services.GetRequiredService<INumaPlacementStrategy>();
        Console.WriteLine("--- NUMA Topology Detection ---");
        var numaNodes = await numaStrategy.GetAvailableNumaNodesAsync();
        Console.WriteLine($"Detected {numaNodes.Count} NUMA node(s):");
        foreach (var node in numaNodes)
        {
            Console.WriteLine($"  Node {node.NodeId}:");
            Console.WriteLine($"    - Processors: {node.ProcessorIds.Count}");
            Console.WriteLine($"    - Memory: {node.MemoryCapacityBytes / (1024 * 1024 * 1024)} GB total");
            Console.WriteLine($"    - Available: {node.AvailableMemoryBytes / (1024 * 1024 * 1024)} GB");
            Console.WriteLine($"    - CPU Util: {node.CpuUtilizationPercent:F2}%");
        }
        Console.WriteLine();

        // Demonstrate GPU device detection
        var gpuStrategy = host.Services.GetRequiredService<IGpuPlacementStrategy>();
        Console.WriteLine("--- GPU Device Detection ---");
        var gpuDevices = await gpuStrategy.GetAvailableGpuDevicesAsync();
        if (gpuDevices.Count > 0)
        {
            Console.WriteLine($"Detected {gpuDevices.Count} GPU device(s):");
            foreach (var device in gpuDevices)
            {
                Console.WriteLine($"  Device {device.DeviceId}: {device.DeviceName}");
                Console.WriteLine($"    - Vendor: {device.Vendor}");
                Console.WriteLine($"    - Memory: {device.TotalMemoryBytes / (1024 * 1024)} MB total");
                Console.WriteLine($"    - Available: {device.AvailableMemoryBytes / (1024 * 1024)} MB");
                Console.WriteLine($"    - Utilization: {device.UtilizationPercent:F2}%");
            }
        }
        else
        {
            Console.WriteLine("No GPU devices detected (CPU fallback enabled)");
        }
        Console.WriteLine();

        Console.WriteLine("=== Example Complete ===");
        Console.WriteLine("\nKey Takeaways:");
        Console.WriteLine("1. NUMA optimization reduces memory latency on multi-socket systems");
        Console.WriteLine("2. GPU acceleration enables high-performance compute for AI/ML actors");
        Console.WriteLine("3. Both features are optional plugins, keeping the core framework lean");
        Console.WriteLine("4. Per-platform implementations allow OS-specific optimizations");
        Console.WriteLine("5. These features are designed for production workloads, not AOT scenarios");

        await host.RunAsync();
    }
}

// Example actor types that would benefit from placement optimization