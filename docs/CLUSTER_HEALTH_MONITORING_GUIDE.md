# Advanced Cluster Health Monitoring Usage Guide

## Overview

Quark's Advanced Cluster Health Monitoring (Phase 7.4) provides intelligent node management with automatic eviction of unhealthy silos based on configurable policies.

## Features

- **Health Score Tracking**: Monitor CPU, memory, and network latency per silo
- **Predictive Failure Detection**: Detect failing silos before complete failure
- **Automatic Silo Eviction**: Remove unhealthy nodes automatically
- **Flexible Policies**: Configure timeout-based, health-score-based, or hybrid eviction
- **Split-Brain Detection**: Identify and handle network partitions

## Basic Setup

### 1. Register Health Monitoring

```csharp
using Quark.Extensions.DependencyInjection;
using Quark.Abstractions.Clustering;

var builder = WebApplication.CreateBuilder(args);

// Add Quark Silo
builder.Services.AddQuarkSilo(options =>
{
    options.SiloId = "silo-1";
    options.AdvertisedAddress = "localhost";
    options.SiloPort = 5000;
});

// Add Cluster Health Monitoring
builder.Services.AddClusterHealthMonitoring(options =>
{
    // Choose eviction policy
    options.Policy = SiloEvictionPolicy.Hybrid; // Both timeout and health score

    // Timeout-based settings
    options.HeartbeatTimeoutSeconds = 30; // Evict if no heartbeat for 30s

    // Health-score-based settings
    options.HealthScoreThreshold = 30.0; // Evict if score drops below 30
    options.ConsecutiveUnhealthyChecks = 3; // Require 3 consecutive bad scores

    // Health check frequency
    options.HealthCheckIntervalSeconds = 10; // Check every 10 seconds

    // Advanced features
    options.EnableSplitBrainDetection = true;
    options.EnableAutomaticRebalancing = true;
    options.MinimumClusterSizeForQuorum = 3;
});

var app = builder.Build();
```

### 2. Update Health Scores (Optional)

If you want to report custom health metrics:

```csharp
using Quark.Abstractions.Clustering;

public class HealthReporter : BackgroundService
{
    private readonly IClusterHealthMonitor _healthMonitor;
    private readonly ILogger<HealthReporter> _logger;

    public HealthReporter(
        IClusterHealthMonitor healthMonitor,
        ILogger<HealthReporter> logger)
    {
        _healthMonitor = healthMonitor;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Collect system metrics
                var cpuUsage = GetCpuUsage(); // Your method
                var memoryUsage = GetMemoryUsage(); // Your method
                var latency = await MeasureNetworkLatency(); // Your method

                // Report to cluster
                var healthScore = new SiloHealthScore(
                    cpuUsage,
                    memoryUsage,
                    latency,
                    DateTimeOffset.UtcNow);

                await _healthMonitor.UpdateHealthScoreAsync(healthScore, stoppingToken);

                _logger.LogInformation(
                    "Health score: {Score:F1} (CPU: {Cpu}%, Mem: {Mem}%, Latency: {Latency}ms)",
                    healthScore.OverallScore,
                    cpuUsage,
                    memoryUsage,
                    latency);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to report health score");
            }

            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        }
    }

    private double GetCpuUsage()
    {
        // Implement CPU usage collection
        // Return value between 0-100
        return Process.GetCurrentProcess().TotalProcessorTime.TotalMilliseconds / 
               Environment.ProcessorCount / 1000.0;
    }

    private double GetMemoryUsage()
    {
        // Implement memory usage collection
        var process = Process.GetCurrentProcess();
        var totalMemory = GC.GetTotalMemory(false);
        var available = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        return (double)totalMemory / available * 100.0;
    }

    private async Task<double> MeasureNetworkLatency()
    {
        // Implement network latency measurement
        // Return latency in milliseconds
        var sw = Stopwatch.StartNew();
        // Ping Redis or another silo
        sw.Stop();
        return sw.Elapsed.TotalMilliseconds;
    }
}

// Register the background service
builder.Services.AddHostedService<HealthReporter>();
```

### 3. Handle Health Events

```csharp
using Quark.Abstractions.Clustering;

public class HealthEventHandler : BackgroundService
{
    private readonly IClusterHealthMonitor _healthMonitor;
    private readonly ILogger<HealthEventHandler> _logger;

    public HealthEventHandler(
        IClusterHealthMonitor healthMonitor,
        ILogger<HealthEventHandler> logger)
    {
        _healthMonitor = healthMonitor;
        _logger = logger;

        // Subscribe to events
        _healthMonitor.SiloEvicted += OnSiloEvicted;
        _healthMonitor.SiloHealthDegraded += OnSiloHealthDegraded;
    }

    private void OnSiloEvicted(object? sender, SiloEvictedEventArgs e)
    {
        _logger.LogWarning(
            "Silo {SiloId} was evicted from cluster. Reason: {Reason}",
            e.SiloInfo.SiloId,
            e.Reason);

        // Take action: notify ops team, trigger autoscaling, etc.
    }

    private void OnSiloHealthDegraded(object? sender, SiloHealthDegradedEventArgs e)
    {
        _logger.LogWarning(
            "Silo {SiloId} health degraded. Score: {Score:F1}. Predicted failure: {Predicted}",
            e.SiloInfo.SiloId,
            e.HealthScore.OverallScore,
            e.PredictedFailure);

        // Take proactive action before failure
        if (e.PredictedFailure)
        {
            // Start migrating actors, prepare backup, etc.
        }
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        return Task.CompletedTask;
    }
}

builder.Services.AddHostedService<HealthEventHandler>();
```

## Eviction Policies

### Timeout-Based Eviction

Removes silos that haven't sent heartbeats within the timeout period.

```csharp
options.Policy = SiloEvictionPolicy.TimeoutBased;
options.HeartbeatTimeoutSeconds = 30;
```

**Use when**: Network is reliable but nodes may crash without warning.

### Health-Score-Based Eviction

Removes silos with persistently low health scores.

```csharp
options.Policy = SiloEvictionPolicy.HealthScoreBased;
options.HealthScoreThreshold = 30.0; // 0-100 scale
options.ConsecutiveUnhealthyChecks = 3;
```

**Use when**: You want to remove overloaded or degraded nodes before they fail.

### Hybrid Eviction

Combines both approaches for maximum resilience.

```csharp
options.Policy = SiloEvictionPolicy.Hybrid;
options.HeartbeatTimeoutSeconds = 30;
options.HealthScoreThreshold = 30.0;
options.ConsecutiveUnhealthyChecks = 3;
```

**Use when**: You want comprehensive health management (recommended).

### No Eviction

Disables automatic eviction (manual management).

```csharp
options.Policy = SiloEvictionPolicy.None;
```

**Use when**: During development or when you want full manual control.

## Health Score Calculation

Health scores are calculated using a weighted formula:

- **CPU Usage** (30%): Lower is better (0% CPU = max score)
- **Memory Usage** (30%): Lower is better (0% memory = max score)
- **Network Latency** (40%): Lower is better (0ms = max score, 1000ms = 0 score)

**Overall Score Formula**:
```
score = (100 - cpuPercent) * 0.3 + 
        (100 - memoryPercent) * 0.3 + 
        max(0, 100 - latencyMs / 10) * 0.4
```

### Custom Health Score Calculator

Implement `IHealthScoreCalculator` for custom scoring logic:

```csharp
public class CustomHealthScoreCalculator : IHealthScoreCalculator
{
    public SiloHealthScore CalculateHealthScore(
        double cpuUsagePercent,
        double memoryUsagePercent,
        double networkLatencyMs)
    {
        // Your custom calculation
        return new SiloHealthScore(
            cpuUsagePercent,
            memoryUsagePercent,
            networkLatencyMs,
            DateTimeOffset.UtcNow);
    }

    public bool PredictFailure(IReadOnlyList<SiloHealthScore> historicalScores)
    {
        // Your custom prediction logic
        return false;
    }

    public bool DetectGradualDegradation(IReadOnlyList<SiloHealthScore> historicalScores)
    {
        // Your custom degradation detection
        return false;
    }
}

// Register custom calculator
builder.Services.AddSingleton<IHealthScoreCalculator, CustomHealthScoreCalculator>();
```

## Monitoring Health Checks

Health scores are integrated with ASP.NET Core health checks:

```csharp
app.MapHealthChecks("/health");
```

Response includes health metrics:

```json
{
  "status": "Healthy",
  "results": {
    "quark-silo": {
      "status": "Healthy",
      "description": "Silo silo-1 is active with 42 actors",
      "data": {
        "SiloId": "silo-1",
        "Status": "Active",
        "ActiveActors": 42,
        "ClusterSize": 3,
        "HealthScore": 85.3,
        "CpuUsage": 15.2,
        "MemoryUsage": 42.1,
        "NetworkLatency": 12.5
      }
    }
  }
}
```

## Best Practices

1. **Start with Hybrid Policy**: Provides both timeout and health-based protection
2. **Tune Thresholds**: Adjust based on your workload (start conservative)
3. **Monitor Events**: Always log eviction and degradation events
4. **Report Custom Metrics**: Provide accurate health scores for better decisions
5. **Test Failure Scenarios**: Verify eviction behavior in staging
6. **Enable Split-Brain Detection**: Essential for multi-region deployments
7. **Set Appropriate Quorum**: Use at least 3 nodes for quorum-based decisions

## Troubleshooting

### Silos Not Being Evicted

- Check `Policy` is not set to `None`
- Verify health scores are being reported
- Ensure `ConsecutiveUnhealthyChecks` threshold is appropriate
- Check event handlers are registered

### Too Many False Evictions

- Increase `HeartbeatTimeoutSeconds`
- Increase `ConsecutiveUnhealthyChecks`
- Lower `HealthScoreThreshold`
- Review health score calculation logic

### Split-Brain Not Detected

- Verify `EnableSplitBrainDetection = true`
- Ensure cluster has at least `MinimumClusterSizeForQuorum` nodes
- Check network latency reporting is accurate

## Example: Complete Setup

```csharp
var builder = WebApplication.CreateBuilder(args);

// Quark Silo with Health Monitoring
builder.Services
    .AddQuarkSilo(options =>
    {
        options.SiloId = Environment.GetEnvironmentVariable("SILO_ID") ?? "silo-1";
        options.AdvertisedAddress = Environment.GetEnvironmentVariable("SILO_ADDRESS") ?? "localhost";
        options.SiloPort = int.Parse(Environment.GetEnvironmentVariable("SILO_PORT") ?? "5000");
    })
    .AddClusterHealthMonitoring(options =>
    {
        options.Policy = SiloEvictionPolicy.Hybrid;
        options.HeartbeatTimeoutSeconds = 30;
        options.HealthScoreThreshold = 30.0;
        options.ConsecutiveUnhealthyChecks = 3;
        options.HealthCheckIntervalSeconds = 10;
        options.EnableSplitBrainDetection = true;
        options.EnableAutomaticRebalancing = true;
    });

// ASP.NET Core Health Checks
builder.Services
    .AddHealthChecks()
    .AddCheck<QuarkSiloHealthCheck>("quark-silo");

// Background Services
builder.Services.AddHostedService<HealthReporter>();
builder.Services.AddHostedService<HealthEventHandler>();

var app = builder.Build();

app.MapHealthChecks("/health");

await app.RunAsync();
```

## See Also

- [Quark Health Checks Documentation](../wiki/Health-Checks.md)
- [Clustering Documentation](../wiki/Clustering.md)
- [OpenTelemetry Integration](../wiki/OpenTelemetry.md)
