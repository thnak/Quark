# Quark Performance Profiling & Analysis

This directory contains the performance profiling and analysis packages for the Quark actor framework.

## Package Structure

### Quark.Profiling.Abstractions
Core abstractions and interfaces for performance profiling. This package contains:
- `IHardwareMetricsCollector` - Interface for collecting hardware metrics
- `IActorProfiler` - Interface for actor-level profiling
- `IClusterDashboardDataProvider` - Interface for cluster visualization data
- `ILoadTestOrchestrator` - Interface for load testing
- Data models for metrics and profiling results

### Quark.Profiling.Linux (Primary Platform)
Linux-specific implementation of hardware metrics collection using `/proc` filesystem.
Provides efficient, zero-allocation metrics collection:
- Process and system CPU usage
- Memory usage (process and system)
- Thread counts
- Network I/O statistics

### Quark.Profiling.Windows (Secondary Platform)
Windows-specific implementation of hardware metrics collection.
Note: Some features have limited functionality compared to Linux due to AOT constraints.

### Quark.Profiling.Dashboard
Dashboard data providers and service registration extensions.
Provides API data (no UI) for:
- Actor distribution heat maps
- Silo resource utilization
- Network traffic patterns
- Placement policy effectiveness

### Quark.Profiling.LoadTesting
Built-in load testing tools for Quark actors:
- Workload generation
- Distributed load testing orchestration
- Latency percentile reporting (p50, p95, p99, p999)

## Quick Start

### 1. Install Packages

```bash
dotnet add package Quark.Profiling.Abstractions
dotnet add package Quark.Profiling.Linux       # For Linux
dotnet add package Quark.Profiling.Windows     # For Windows
dotnet add package Quark.Profiling.Dashboard   # For dashboard data
dotnet add package Quark.Profiling.LoadTesting # For load testing
```

### 2. Register Services

```csharp
services.AddQuarkProfiling(config =>
{
    // Automatically detects and registers platform-specific hardware collector
    config.AddPlatformHardwareMetrics();
});
```

Or register platform-specific manually:

```csharp
services.AddQuarkProfiling(config =>
{
    config.AddLinuxHardwareMetrics();   // Linux only
    // or
    config.AddWindowsHardwareMetrics(); // Windows only
});
```

### 3. Use Actor Profiler

```csharp
public class MyActor : ActorBase
{
    private readonly IActorProfiler _profiler;

    public MyActor(string actorId, IActorProfiler profiler) : base(actorId)
    {
        _profiler = profiler;
    }

    public override Task OnActivateAsync(CancellationToken cancellationToken = default)
    {
        _profiler.StartProfiling(nameof(MyActor), ActorId);
        return Task.CompletedTask;
    }

    public async Task<int> ProcessAsync(int input)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        
        // Your logic here
        await Task.Delay(10);
        
        sw.Stop();
        _profiler.RecordMethodInvocation(nameof(MyActor), ActorId, nameof(ProcessAsync), sw.Elapsed.TotalMilliseconds);
        
        return input * 2;
    }
}
```

### 4. Query Profiling Data

```csharp
var actorProfiler = serviceProvider.GetRequiredService<IActorProfiler>();

// Get all profiling data
var allData = actorProfiler.GetAllProfilingData();

// Get data for specific actor type
var counterData = actorProfiler.GetProfilingDataByType("CounterActor");

// Get data for specific actor instance
var specificActor = actorProfiler.GetProfilingData("CounterActor", "counter-1");

Console.WriteLine($"Actor: {specificActor.ActorType}:{specificActor.ActorId}");
Console.WriteLine($"Total invocations: {specificActor.TotalInvocations}");
Console.WriteLine($"Average duration: {specificActor.AverageDurationMs:F2}ms");
Console.WriteLine($"Min duration: {specificActor.MinDurationMs:F2}ms");
Console.WriteLine($"Max duration: {specificActor.MaxDurationMs:F2}ms");
```

### 5. Get Hardware Metrics

```csharp
var hardwareCollector = serviceProvider.GetRequiredService<IHardwareMetricsCollector>();

var snapshot = await hardwareCollector.GetMetricsSnapshotAsync();
Console.WriteLine($"Process CPU: {snapshot.ProcessCpuUsage:F2}%");
Console.WriteLine($"System CPU: {snapshot.SystemCpuUsage:F2}%");
Console.WriteLine($"Process Memory: {snapshot.ProcessMemoryUsage / 1024 / 1024}MB");
Console.WriteLine($"System Memory Available: {snapshot.SystemMemoryAvailable / 1024 / 1024}MB");
Console.WriteLine($"Thread Count: {snapshot.ThreadCount}");
```

### 6. Use Dashboard Data Provider

```csharp
var dashboardProvider = serviceProvider.GetRequiredService<IClusterDashboardDataProvider>();

// Get actor distribution
var distribution = await dashboardProvider.GetActorDistributionAsync();
Console.WriteLine($"Actors per silo: {string.Join(", ", distribution.ActorCountPerSilo)}");

// Get silo resources
var resources = await dashboardProvider.GetSiloResourcesAsync();
foreach (var silo in resources)
{
    Console.WriteLine($"Silo {silo.SiloId}: CPU={silo.CpuUsage:F2}%, Memory={silo.MemoryUsagePercent:F2}%");
}

// Get placement effectiveness
var placement = await dashboardProvider.GetPlacementEffectivenessAsync();
Console.WriteLine($"Load distribution score: {placement.LoadDistributionScore:F2}");
Console.WriteLine($"Local call ratio: {placement.LocalCallRatio:F2}");
```

### 7. Run Load Tests

```csharp
var loadTestOrchestrator = serviceProvider.GetRequiredService<ILoadTestOrchestrator>();

var scenario = new LoadTestScenario
{
    ActorType = "MyActor",
    ConcurrentActors = 100,
    MessagesPerActor = 1000,
    MessageRateLimit = 10000 // messages per second
};

var result = await loadTestOrchestrator.StartLoadTestAsync(scenario);

Console.WriteLine($"Test completed in {result.Duration.TotalSeconds:F2}s");
Console.WriteLine($"Total messages: {result.TotalMessages}");
Console.WriteLine($"Success rate: {result.SuccessRate:F2}%");
Console.WriteLine($"Messages/sec: {result.MessagesPerSecond:F2}");
Console.WriteLine($"Latency p50: {result.Latency.P50Ms:F2}ms");
Console.WriteLine($"Latency p95: {result.Latency.P95Ms:F2}ms");
Console.WriteLine($"Latency p99: {result.Latency.P99Ms:F2}ms");
```

## Dashboard Integration

The profiling packages provide **API data only**. UI implementation is left to users. Here's how to integrate with your own dashboard:

### Exposing Data via HTTP Endpoints

You can create your own HTTP endpoints to expose profiling data:

```csharp
// In your ASP.NET Core application
app.MapGet("/api/profiling/actors", async (IActorProfiler profiler) =>
{
    var data = profiler.GetAllProfilingData();
    return Results.Json(data);
});

app.MapGet("/api/profiling/hardware", async (IHardwareMetricsCollector collector) =>
{
    var snapshot = await collector.GetMetricsSnapshotAsync();
    return Results.Json(snapshot);
});

app.MapGet("/api/profiling/dashboard/distribution", async (IClusterDashboardDataProvider dashboard) =>
{
    var data = await dashboard.GetActorDistributionAsync();
    return Results.Json(data);
});
```

### Example Dashboard UIs

Build your own dashboard using:
- **Web UI**: React, Angular, Vue.js with charts (Chart.js, D3.js)
- **Real-time updates**: SignalR for live metrics
- **Grafana**: Export metrics to Prometheus format
- **Custom tools**: PowerShell scripts, CLI tools, desktop apps

## Performance Considerations

- **Actor Profiler**: Uses `ConcurrentDictionary` for lock-free operation
- **Hardware Metrics (Linux)**: Direct `/proc` filesystem reads, minimal overhead
- **Hardware Metrics (Windows)**: Uses `Process` API, some limitations for AOT
- **Load Testing**: Runs in-process, supports rate limiting

## Platform Support

| Feature | Linux | Windows |
|---------|-------|---------|
| Process CPU | ✅ Full | ✅ Full |
| System CPU | ✅ Full | ⚠️ Limited |
| Memory Metrics | ✅ Full | ✅ Full |
| Network I/O | ✅ Full | ⚠️ Limited |
| Thread Count | ✅ Full | ✅ Full |

## AOT Compatibility

All packages are fully AOT-compatible (Native AOT):
- ✅ Zero reflection at runtime
- ✅ Compile-time code generation
- ✅ No dynamic IL emission
- ✅ Minimal allocations

## Examples

See `examples/` directory for complete examples:
- Basic profiling usage
- Load testing scenarios
- Dashboard data integration

## License

MIT License - see LICENSE file for details
