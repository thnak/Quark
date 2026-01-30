using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Quark.Abstractions;
using Quark.Core.Actors;
using Quark.Profiling.Abstractions;
using Quark.Profiling.Dashboard;
using Quark.Profiling.LoadTesting;

Console.WriteLine("=== Quark Performance Profiling Example ===\n");

// Setup DI container
var services = new ServiceCollection();
services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Information));
services.AddSingleton<IActorFactory, ActorFactory>();

// Register profiling services with platform-specific hardware metrics
services.AddQuarkProfiling(config =>
{
    config.AddPlatformHardwareMetrics();
});

// Register load testing
services.AddSingleton<ILoadTestOrchestrator, LoadTestOrchestrator>();

var serviceProvider = services.BuildServiceProvider();

// Get services
var actorFactory = serviceProvider.GetRequiredService<IActorFactory>();
var actorProfiler = serviceProvider.GetRequiredService<IActorProfiler>();
var hardwareCollector = serviceProvider.GetService<IHardwareMetricsCollector>();
var dashboardProvider = serviceProvider.GetRequiredService<IClusterDashboardDataProvider>();
var loadTestOrchestrator = serviceProvider.GetRequiredService<ILoadTestOrchestrator>();

Console.WriteLine("1. Hardware Metrics Collection");
Console.WriteLine("================================");
if (hardwareCollector != null)
{
    var snapshot = await hardwareCollector.GetMetricsSnapshotAsync();
    Console.WriteLine($"Timestamp: {snapshot.Timestamp}");
    Console.WriteLine($"Process CPU: {snapshot.ProcessCpuUsage:F2}%");
    Console.WriteLine($"System CPU: {snapshot.SystemCpuUsage:F2}%");
    Console.WriteLine($"Process Memory: {snapshot.ProcessMemoryUsage / 1024.0 / 1024.0:F2} MB");
    Console.WriteLine($"System Memory Available: {snapshot.SystemMemoryAvailable / 1024.0 / 1024.0:F2} MB");
    Console.WriteLine($"System Memory Total: {snapshot.SystemMemoryTotal / 1024.0 / 1024.0:F2} MB");
    Console.WriteLine($"System Memory Usage: {snapshot.SystemMemoryUsagePercent:F2}%");
    Console.WriteLine($"Thread Count: {snapshot.ThreadCount}");
    Console.WriteLine($"Network Bytes Received/sec: {snapshot.NetworkBytesReceivedPerSecond}");
    Console.WriteLine($"Network Bytes Sent/sec: {snapshot.NetworkBytesSentPerSecond}");
}
else
{
    Console.WriteLine("Hardware metrics collector not available on this platform.");
}

Console.WriteLine("\n2. Actor Profiling");
Console.WriteLine("==================");

// Create and profile some actors
Console.WriteLine("Creating and profiling 5 counter actors...");
var actors = new List<CounterActor>();
for (int i = 0; i < 5; i++)
{
    var actor = actorFactory.CreateActor<CounterActor>($"counter-{i}");
    await actor.OnActivateAsync();
    actorProfiler.StartProfiling(nameof(CounterActor), actor.ActorId);
    actors.Add(actor);
}

// Simulate some work
Console.WriteLine("Simulating actor work...");
var random = new Random();
var sw = System.Diagnostics.Stopwatch.StartNew();
foreach (var actor in actors)
{
    for (int i = 0; i < 10; i++)
    {
        sw.Restart();
        await actor.IncrementAsync(random.Next(1, 100));
        actorProfiler.RecordMethodInvocation(nameof(CounterActor), actor.ActorId, nameof(actor.IncrementAsync), sw.Elapsed.TotalMilliseconds);
        
        if (i % 3 == 0)
        {
            sw.Restart();
            await actor.GetValueAsync();
            actorProfiler.RecordMethodInvocation(nameof(CounterActor), actor.ActorId, nameof(actor.GetValueAsync), sw.Elapsed.TotalMilliseconds);
        }
    }
}

// Display profiling results
Console.WriteLine("\nProfiling Results:");
var allProfilingData = actorProfiler.GetAllProfilingData().ToList();
foreach (var data in allProfilingData.OrderBy(d => d.ActorId))
{
    Console.WriteLine($"\nActor: {data.ActorType}:{data.ActorId}");
    Console.WriteLine($"  Total Invocations: {data.TotalInvocations}");
    Console.WriteLine($"  Average Duration: {data.AverageDurationMs:F3}ms");
    Console.WriteLine($"  Min Duration: {data.MinDurationMs:F3}ms");
    Console.WriteLine($"  Max Duration: {data.MaxDurationMs:F3}ms");
    Console.WriteLine($"  Total Allocations: {data.TotalAllocations} bytes");
    
    if (data.Methods.Any())
    {
        Console.WriteLine("  Method Statistics:");
        foreach (var method in data.Methods.Values.OrderByDescending(m => m.TotalDurationMs))
        {
            Console.WriteLine($"    {method.MethodName}: {method.InvocationCount} calls, avg {method.AverageDurationMs:F3}ms");
        }
    }
}

Console.WriteLine("\n3. Dashboard Data");
Console.WriteLine("=================");

// Actor distribution
var distribution = await dashboardProvider.GetActorDistributionAsync();
Console.WriteLine("Actor Distribution:");
foreach (var (siloId, count) in distribution.ActorCountPerSilo)
{
    Console.WriteLine($"  Silo {siloId}: {count} actors");
}
Console.WriteLine("\nActor Types:");
foreach (var (actorType, count) in distribution.ActorTypeDistribution)
{
    Console.WriteLine($"  {actorType}: {count}");
}

// Silo resources
var resources = await dashboardProvider.GetSiloResourcesAsync();
Console.WriteLine("\nSilo Resources:");
foreach (var silo in resources)
{
    Console.WriteLine($"  Silo: {silo.SiloId}");
    Console.WriteLine($"    CPU Usage: {silo.CpuUsage:F2}%");
    Console.WriteLine($"    Memory Usage: {silo.MemoryUsage / 1024.0 / 1024.0:F2} MB ({silo.MemoryUsagePercent:F2}%)");
    Console.WriteLine($"    Active Actors: {silo.ActiveActors}");
    Console.WriteLine($"    Threads: {silo.ThreadCount}");
}

// Placement effectiveness
var placement = await dashboardProvider.GetPlacementEffectivenessAsync();
Console.WriteLine("\nPlacement Effectiveness:");
Console.WriteLine($"  Load Distribution Score: {placement.LoadDistributionScore:F2}/100");
Console.WriteLine($"  Locality Score: {placement.LocalityScore:F2}/100");
Console.WriteLine($"  Local Call Ratio: {placement.LocalCallRatio:F2}");

Console.WriteLine("\n4. Load Testing");
Console.WriteLine("===============");
Console.WriteLine("Starting load test...");

var scenario = new LoadTestScenario
{
    ActorType = nameof(CounterActor),
    ConcurrentActors = 50,
    MessagesPerActor = 100,
    MessageRateLimit = 0 // unlimited
};

var result = await loadTestOrchestrator.StartLoadTestAsync(scenario);

Console.WriteLine("\nLoad Test Results:");
Console.WriteLine($"  Test ID: {result.TestId}");
Console.WriteLine($"  Duration: {result.Duration.TotalSeconds:F2}s");
Console.WriteLine($"  Total Messages: {result.TotalMessages}");
Console.WriteLine($"  Successful: {result.SuccessfulMessages}");
Console.WriteLine($"  Failed: {result.FailedMessages}");
Console.WriteLine($"  Success Rate: {result.SuccessRate:F2}%");
Console.WriteLine($"  Messages/sec: {result.MessagesPerSecond:F2}");
Console.WriteLine("\n  Latency Statistics:");
Console.WriteLine($"    Min: {result.Latency.MinMs:F3}ms");
Console.WriteLine($"    Mean: {result.Latency.MeanMs:F3}ms");
Console.WriteLine($"    p50: {result.Latency.P50Ms:F3}ms");
Console.WriteLine($"    p95: {result.Latency.P95Ms:F3}ms");
Console.WriteLine($"    p99: {result.Latency.P99Ms:F3}ms");
Console.WriteLine($"    p999: {result.Latency.P999Ms:F3}ms");
Console.WriteLine($"    Max: {result.Latency.MaxMs:F3}ms");
Console.WriteLine($"    StdDev: {result.Latency.StdDevMs:F3}ms");

Console.WriteLine("\n5. Cleanup");
Console.WriteLine("==========");
actorProfiler.ClearAllProfilingData();
Console.WriteLine("All profiling data cleared.");

Console.WriteLine("\n=== Example Complete ===");

// Example actor that uses profiling
[Actor(Name = "Counter")]
public class CounterActor : ActorBase
{
    private int _counter;

    public CounterActor(string actorId) : base(actorId)
    {
    }

    public override Task OnActivateAsync(CancellationToken cancellationToken = default)
    {
        // Get profiler from DI if needed (would require IServiceScope parameter)
        return Task.CompletedTask;
    }

    public async Task IncrementAsync(int value)
    {
        // Simulate some work
        await Task.Delay(1);
        _counter += value;
    }

    public async Task<int> GetValueAsync()
    {
        await Task.Delay(1);
        return _counter;
    }
}
