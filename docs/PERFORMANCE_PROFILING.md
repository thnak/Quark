# Quark Performance Profiling & Analysis

Complete guide to performance profiling, hardware metrics collection, and load testing in Quark.

## Table of Contents

- [Overview](#overview)
- [Packages](#packages)
- [Quick Start](#quick-start)
- [Actor Profiling](#actor-profiling)
- [Hardware Metrics](#hardware-metrics)
- [Dashboard Data](#dashboard-data)
- [Load Testing](#load-testing)
- [Integration Patterns](#integration-patterns)
- [Best Practices](#best-practices)

## Overview

Quark's performance profiling system provides comprehensive tools for:

1. **Actor Profiling** - Track per-actor performance metrics
2. **Hardware Metrics** - Monitor system resources (Linux and Windows)
3. **Dashboard Data** - Cluster visualization data (API only)
4. **Load Testing** - Built-in load generation and analysis

All packages are **100% AOT-compatible** with zero runtime reflection.

## Packages

### Core Abstractions

**Quark.Profiling.Abstractions**
- `IHardwareMetricsCollector` - Hardware metrics interface
- `IActorProfiler` - Actor profiling interface
- `IClusterDashboardDataProvider` - Dashboard data interface
- `ILoadTestOrchestrator` - Load testing interface

Install:
```bash
dotnet add package Quark.Profiling.Abstractions
```

### Platform-Specific

**Quark.Profiling.Linux** (Primary Platform)
- Efficient `/proc` filesystem-based metrics
- Full system and process metrics
- Network I/O tracking

Install:
```bash
dotnet add package Quark.Profiling.Linux
```

**Quark.Profiling.Windows** (Secondary Platform)
- Process API-based metrics
- Basic system metrics (limited for AOT)

Install:
```bash
dotnet add package Quark.Profiling.Windows
```

### Dashboard & Testing

**Quark.Profiling.Dashboard**
- Actor profiler implementation
- Dashboard data providers
- Service registration extensions

Install:
```bash
dotnet add package Quark.Profiling.Dashboard
```

**Quark.Profiling.LoadTesting**
- Load test orchestration
- Latency percentile calculations
- Concurrent workload generation

Install:
```bash
dotnet add package Quark.Profiling.LoadTesting
```

## Quick Start

### 1. Register Services

```csharp
using Microsoft.Extensions.DependencyInjection;
using Quark.Profiling.Dashboard;

var services = new ServiceCollection();

// Register profiling with automatic platform detection
services.AddQuarkProfiling(config =>
{
    config.AddPlatformHardwareMetrics();
});

// Or register platform-specific
services.AddQuarkProfiling(config =>
{
    config.AddLinuxHardwareMetrics();   // Linux only
    // or
    config.AddWindowsHardwareMetrics(); // Windows only
});

var serviceProvider = services.BuildServiceProvider();
```

### 2. Profile Actors

```csharp
var actorFactory = serviceProvider.GetRequiredService<IActorFactory>();
var actorProfiler = serviceProvider.GetRequiredService<IActorProfiler>();

// Create and start profiling
var actor = actorFactory.CreateActor<MyActor>("my-actor-1");
actorProfiler.StartProfiling(nameof(MyActor), actor.ActorId);

// Execute methods
var sw = Stopwatch.StartNew();
await actor.DoWorkAsync();
actorProfiler.RecordMethodInvocation(nameof(MyActor), actor.ActorId, nameof(actor.DoWorkAsync), sw.Elapsed.TotalMilliseconds);

// Get profiling data
var data = actorProfiler.GetProfilingData(nameof(MyActor), actor.ActorId);
Console.WriteLine($"Avg duration: {data.AverageDurationMs:F2}ms");
```

### 3. Monitor Hardware

```csharp
var hardwareCollector = serviceProvider.GetService<IHardwareMetricsCollector>();
if (hardwareCollector != null)
{
    var snapshot = await hardwareCollector.GetMetricsSnapshotAsync();
    Console.WriteLine($"CPU: {snapshot.ProcessCpuUsage:F2}%");
    Console.WriteLine($"Memory: {snapshot.ProcessMemoryUsage / 1024 / 1024}MB");
}
```

### 4. Run Load Tests

```csharp
var loadTestOrchestrator = serviceProvider.GetRequiredService<ILoadTestOrchestrator>();

var scenario = new LoadTestScenario
{
    ActorType = "MyActor",
    ConcurrentActors = 100,
    MessagesPerActor = 1000
};

var result = await loadTestOrchestrator.StartLoadTestAsync(scenario);
Console.WriteLine($"Messages/sec: {result.MessagesPerSecond:F2}");
Console.WriteLine($"p99 latency: {result.Latency.P99Ms:F2}ms");
```

## Actor Profiling

### Starting Profiling

```csharp
actorProfiler.StartProfiling("CounterActor", "counter-1");
```

### Recording Method Invocations

```csharp
var sw = Stopwatch.StartNew();
await actor.ProcessAsync(data);
sw.Stop();

actorProfiler.RecordMethodInvocation(
    actorType: "CounterActor",
    actorId: "counter-1",
    methodName: "ProcessAsync",
    durationMs: sw.Elapsed.TotalMilliseconds
);
```

### Recording Allocations

```csharp
actorProfiler.RecordAllocation(
    actorType: "CounterActor",
    actorId: "counter-1",
    bytes: 1024
);
```

### Querying Profiling Data

```csharp
// Get all actors
var allData = actorProfiler.GetAllProfilingData();

// Get by type
var counterActors = actorProfiler.GetProfilingDataByType("CounterActor");

// Get specific actor
var specific = actorProfiler.GetProfilingData("CounterActor", "counter-1");

// Access method-level data
foreach (var method in specific.Methods.Values)
{
    Console.WriteLine($"{method.MethodName}:");
    Console.WriteLine($"  Calls: {method.InvocationCount}");
    Console.WriteLine($"  Avg: {method.AverageDurationMs:F3}ms");
    Console.WriteLine($"  Min: {method.MinDurationMs:F3}ms");
    Console.WriteLine($"  Max: {method.MaxDurationMs:F3}ms");
}
```

### Clearing Data

```csharp
// Clear specific actor
actorProfiler.ClearProfilingData("CounterActor", "counter-1");

// Clear all data
actorProfiler.ClearAllProfilingData();
```

## Hardware Metrics

### Available Metrics

**Linux (Full Support):**
- Process CPU usage (%)
- System CPU usage (%)
- Process memory usage (bytes)
- System memory available (bytes)
- System memory total (bytes)
- Thread count
- Network bytes received/sec
- Network bytes sent/sec

**Windows (Partial Support):**
- Process CPU usage (%)
- Process memory usage (bytes)
- System memory available (bytes)
- Thread count
- System CPU and network metrics limited (AOT constraints)

### Getting Metrics

```csharp
// Get full snapshot
var snapshot = await hardwareCollector.GetMetricsSnapshotAsync();

// Individual metrics
var cpuUsage = await hardwareCollector.GetProcessCpuUsageAsync();
var memoryUsage = await hardwareCollector.GetProcessMemoryUsageAsync();
var threadCount = await hardwareCollector.GetThreadCountAsync();
```

### Continuous Monitoring

```csharp
using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));
while (await timer.WaitForNextTickAsync())
{
    var snapshot = await hardwareCollector.GetMetricsSnapshotAsync();
    LogMetrics(snapshot);
}
```

## Dashboard Data

### Actor Distribution

```csharp
var dashboard = serviceProvider.GetRequiredService<IClusterDashboardDataProvider>();

var distribution = await dashboard.GetActorDistributionAsync();

// Actors per silo
foreach (var (siloId, count) in distribution.ActorCountPerSilo)
{
    Console.WriteLine($"Silo {siloId}: {count} actors");
}

// By actor type
foreach (var (actorType, count) in distribution.ActorTypeDistribution)
{
    Console.WriteLine($"{actorType}: {count}");
}
```

### Silo Resources

```csharp
var resources = await dashboard.GetSiloResourcesAsync();

foreach (var silo in resources)
{
    Console.WriteLine($"Silo: {silo.SiloId}");
    Console.WriteLine($"  CPU: {silo.CpuUsage:F2}%");
    Console.WriteLine($"  Memory: {silo.MemoryUsagePercent:F2}%");
    Console.WriteLine($"  Actors: {silo.ActiveActors}");
    Console.WriteLine($"  Threads: {silo.ThreadCount}");
}
```

### Network Traffic

```csharp
var network = await dashboard.GetNetworkTrafficAsync();
Console.WriteLine($"Total sent: {network.TotalBytesSentPerSecond} bytes/sec");
Console.WriteLine($"Total received: {network.TotalBytesReceivedPerSecond} bytes/sec");
```

### Placement Effectiveness

```csharp
var placement = await dashboard.GetPlacementEffectivenessAsync();
Console.WriteLine($"Load distribution: {placement.LoadDistributionScore:F2}/100");
Console.WriteLine($"Locality score: {placement.LocalityScore:F2}/100");
Console.WriteLine($"Local call ratio: {placement.LocalCallRatio:F2}");
```

## Load Testing

### Creating Scenarios

```csharp
var scenario = new LoadTestScenario
{
    ActorType = "ProcessorActor",
    ConcurrentActors = 100,          // Number of actors
    MessagesPerActor = 1000,         // Messages per actor
    DurationSeconds = 0,             // 0 = use message count
    MessageRateLimit = 10000         // Max messages/sec (0 = unlimited)
};
```

### Running Tests

```csharp
var result = await loadTestOrchestrator.StartLoadTestAsync(scenario);

Console.WriteLine($"Duration: {result.Duration.TotalSeconds:F2}s");
Console.WriteLine($"Messages/sec: {result.MessagesPerSecond:F2}");
Console.WriteLine($"Success rate: {result.SuccessRate:F2}%");

// Latency percentiles
Console.WriteLine($"p50: {result.Latency.P50Ms:F2}ms");
Console.WriteLine($"p95: {result.Latency.P95Ms:F2}ms");
Console.WriteLine($"p99: {result.Latency.P99Ms:F2}ms");
Console.WriteLine($"p999: {result.Latency.P999Ms:F2}ms");
```

### Monitoring Progress

```csharp
var testId = scenario.TestId;

// In another task/thread
while (true)
{
    var status = loadTestOrchestrator.GetTestStatus(testId);
    if (status == null || status.State == LoadTestState.Completed)
        break;
        
    Console.WriteLine($"Progress: {status.ProgressPercent:F2}%");
    Console.WriteLine($"Current rate: {status.CurrentMessagesPerSecond:F2} msg/sec");
    await Task.Delay(1000);
}
```

### Cancellation

```csharp
using var cts = new CancellationTokenSource();

var task = loadTestOrchestrator.StartLoadTestAsync(scenario, cts.Token);

// Cancel after 10 seconds
cts.CancelAfter(TimeSpan.FromSeconds(10));

try
{
    var result = await task;
}
catch (OperationCanceledException)
{
    Console.WriteLine("Load test cancelled");
}
```

## Integration Patterns

### ASP.NET Core Integration

```csharp
// In your web application
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddQuarkProfiling(config =>
{
    config.AddPlatformHardwareMetrics();
});

var app = builder.Build();

// Expose profiling endpoints
app.MapGet("/api/profiling/actors", (IActorProfiler profiler) =>
{
    var data = profiler.GetAllProfilingData();
    return Results.Json(data);
});

app.MapGet("/api/profiling/hardware", async (IHardwareMetricsCollector collector) =>
{
    var snapshot = await collector.GetMetricsSnapshotAsync();
    return Results.Json(snapshot);
});
```

### Real-Time Dashboard with SignalR

```csharp
public class MetricsHub : Hub
{
    private readonly IActorProfiler _profiler;
    private readonly IHardwareMetricsCollector _hardwareCollector;
    
    public async Task StreamMetrics(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            var metrics = new
            {
                Actors = _profiler.GetAllProfilingData(),
                Hardware = await _hardwareCollector.GetMetricsSnapshotAsync()
            };
            
            await Clients.Caller.SendAsync("MetricsUpdate", metrics);
        }
    }
}
```

### Prometheus Export

```csharp
public class PrometheusMetricsExporter
{
    public string ExportMetrics(IActorProfiler profiler)
    {
        var sb = new StringBuilder();
        
        foreach (var actor in profiler.GetAllProfilingData())
        {
            sb.AppendLine($"# TYPE quark_actor_invocations counter");
            sb.AppendLine($"quark_actor_invocations{{actor_type=\"{actor.ActorType}\",actor_id=\"{actor.ActorId}\"}} {actor.TotalInvocations}");
            
            sb.AppendLine($"# TYPE quark_actor_avg_duration_ms gauge");
            sb.AppendLine($"quark_actor_avg_duration_ms{{actor_type=\"{actor.ActorType}\",actor_id=\"{actor.ActorId}\"}} {actor.AverageDurationMs}");
        }
        
        return sb.ToString();
    }
}
```

## Best Practices

### 1. Selective Profiling

Don't profile all actors - profile selectively:

```csharp
// Profile only specific actor types
if (actorType == "HighValueActor" || actorType == "SlowActor")
{
    actorProfiler.StartProfiling(actorType, actorId);
}
```

### 2. Sampling

Use sampling for high-volume scenarios:

```csharp
// Profile 10% of requests
if (Random.Shared.Next(100) < 10)
{
    var sw = Stopwatch.StartNew();
    await actor.ProcessAsync();
    actorProfiler.RecordMethodInvocation(...);
}
```

### 3. Periodic Cleanup

Clear old profiling data regularly:

```csharp
using var timer = new PeriodicTimer(TimeSpan.FromMinutes(5));
while (await timer.WaitForNextTickAsync())
{
    actorProfiler.ClearAllProfilingData();
}
```

### 4. Hardware Metrics Intervals

Don't poll too frequently:

```csharp
// Good: 5-10 second intervals
using var timer = new PeriodicTimer(TimeSpan.FromSeconds(5));

// Bad: Sub-second intervals
// using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));
```

### 5. Load Test Environment

Run load tests in isolated environments:

```csharp
// Dedicated load test configuration
var scenario = new LoadTestScenario
{
    ActorType = "TestActor",
    ConcurrentActors = 1000,
    MessagesPerActor = 10000,
    MessageRateLimit = 50000  // Control rate to avoid overload
};
```

## Performance Impact

- **Actor Profiler**: Minimal overhead (~1-2% with ConcurrentDictionary)
- **Hardware Metrics (Linux)**: Very low overhead (direct /proc reads)
- **Hardware Metrics (Windows)**: Low overhead (Process API)
- **Load Testing**: Runs in-process, controlled rate limiting available

## Platform Compatibility

| Feature | Linux | Windows | macOS |
|---------|-------|---------|-------|
| Actor Profiling | ✅ Full | ✅ Full | ✅ Full |
| Process CPU | ✅ Full | ✅ Full | ✅ Full |
| System CPU | ✅ Full | ⚠️ Limited | ⚠️ Limited |
| Memory Metrics | ✅ Full | ✅ Full | ✅ Full |
| Network I/O | ✅ Full | ⚠️ Limited | ⚠️ Limited |
| Load Testing | ✅ Full | ✅ Full | ✅ Full |

✅ = Full support  
⚠️ = Partial support (AOT constraints)  
❌ = Not supported

## Examples

See `examples/Quark.Examples.Profiling/` for a complete working example demonstrating all features.

## License

MIT License - see LICENSE file for details
