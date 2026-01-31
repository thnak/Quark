using Microsoft.Extensions.Logging;
using Quark.Abstractions;
using Quark.Abstractions.Migration;
using Quark.Core.Actors;
using Quark.Core.Actors.Migration;
using Quark.Examples.Serverless.Actors;

Console.WriteLine("=== Quark Serverless Actors Example ===");
Console.WriteLine();
Console.WriteLine("This example demonstrates serverless actors with auto-scaling from zero.");
Console.WriteLine("Actors are automatically deactivated after being idle for 10 seconds.");
Console.WriteLine();

// For this example, we'll demonstrate the feature without a full silo setup
// In production, you'd configure Redis clustering and gRPC transport
Console.WriteLine("Creating actor factory and activity tracker...");
var factory = new ActorFactory();
var activityTracker = new ActorActivityTracker();

// Create serverless configuration
var options = new ServerlessActorOptions
{
    Enabled = true,
    IdleTimeout = TimeSpan.FromSeconds(10),  // Deactivate after 10s idle
    CheckInterval = TimeSpan.FromSeconds(5), // Check every 5s
    MinimumActiveActors = 0                  // Scale to zero
};

Console.WriteLine($"✓ Serverless options configured (IdleTimeout: {options.IdleTimeout})");
Console.WriteLine();

// Create some actors
Console.WriteLine("Creating serverless worker actors...");
var worker1 = factory.CreateActor<ServerlessWorkerActor>("worker-1");
var worker2 = factory.CreateActor<ServerlessWorkerActor>("worker-2");
await worker1.OnActivateAsync();
await worker2.OnActivateAsync();

// Track their activity
activityTracker.RecordCallStarted("worker-1", "ServerlessWorkerActor");
activityTracker.RecordCallStarted("worker-2", "ServerlessWorkerActor");
Console.WriteLine($"✓ Created 2 active actors");
Console.WriteLine();

// Process some work
Console.WriteLine("Processing work with actors...");
var result1 = await worker1.ProcessDataAsync("Task 1");
var result2 = await worker2.ProcessDataAsync("Task 2");
Console.WriteLine($"✓ {result1}");
Console.WriteLine($"✓ {result2}");
Console.WriteLine();

// Complete the calls
activityTracker.RecordCallCompleted("worker-1", "ServerlessWorkerActor");
activityTracker.RecordCallCompleted("worker-2", "ServerlessWorkerActor");

// Check metrics
var metrics1 = await activityTracker.GetActivityMetricsAsync("worker-1");
var metrics2 = await activityTracker.GetActivityMetricsAsync("worker-2");
Console.WriteLine($"Worker 1 metrics: Queue={metrics1?.QueueDepth}, Calls={metrics1?.ActiveCallCount}, LastActivity={metrics1?.LastActivityTime:HH:mm:ss}");
Console.WriteLine($"Worker 2 metrics: Queue={metrics2?.QueueDepth}, Calls={metrics2?.ActiveCallCount}, LastActivity={metrics2?.LastActivityTime:HH:mm:ss}");
Console.WriteLine();

// Simulate idle detection
Console.WriteLine("Simulating idle detection (checking if actors should be deactivated)...");
var deactivationPolicy = new IdleTimeoutDeactivationPolicy(options.IdleTimeout);

// Wait a bit, then check again
Console.WriteLine("Waiting 5 seconds...");
await Task.Delay(TimeSpan.FromSeconds(5));

metrics1 = await activityTracker.GetActivityMetricsAsync("worker-1");
var shouldDeactivate1 = deactivationPolicy.ShouldDeactivate(
    "worker-1",
    "ServerlessWorkerActor",
    metrics1!.LastActivityTime,
    metrics1.QueueDepth,
    metrics1.ActiveCallCount);

Console.WriteLine($"Should deactivate worker-1? {shouldDeactivate1} (idle for ~5s)");
Console.WriteLine();

// Wait for idle timeout
Console.WriteLine("Waiting additional 7 seconds (total 12s - exceeds 10s idle timeout)...");
await Task.Delay(TimeSpan.FromSeconds(7));

metrics1 = await activityTracker.GetActivityMetricsAsync("worker-1");
shouldDeactivate1 = deactivationPolicy.ShouldDeactivate(
    "worker-1",
    "ServerlessWorkerActor",
    metrics1!.LastActivityTime,
    metrics1.QueueDepth,
    metrics1.ActiveCallCount);

Console.WriteLine($"Should deactivate worker-1? {shouldDeactivate1} (idle for ~12s)");

if (shouldDeactivate1)
{
    Console.WriteLine("Deactivating idle actors...");
    await worker1.OnDeactivateAsync();
    await worker2.OnDeactivateAsync();
    Console.WriteLine("✓ Actors deactivated (scaled to zero)");
}
Console.WriteLine();

// Demonstrate reactivation
Console.WriteLine("Simulating new request (creating new actor instance)...");
var worker3 = factory.CreateActor<ServerlessWorkerActor>("worker-3");
await worker3.OnActivateAsync();
activityTracker.RecordCallStarted("worker-3", "ServerlessWorkerActor");
var result3 = await worker3.ProcessDataAsync("Task 3 (after idle period)");
Console.WriteLine($"✓ {result3}");
activityTracker.RecordCallCompleted("worker-3", "ServerlessWorkerActor");
await worker3.OnDeactivateAsync();
Console.WriteLine();

Console.WriteLine("=== Example completed successfully ===");
Console.WriteLine();
Console.WriteLine("Key takeaways:");
Console.WriteLine("- IdleTimeoutDeactivationPolicy detects idle actors");
Console.WriteLine("- Actors with no pending work are candidates for deactivation");
Console.WriteLine("- IdleDeactivationService (background service) would handle this automatically");
Console.WriteLine("- Perfect for event-driven, pay-per-use workloads");
Console.WriteLine();
Console.WriteLine("In production:");
Console.WriteLine("- Use .WithServerlessActors() when configuring QuarkSilo");
Console.WriteLine("- IdleDeactivationService runs automatically in background");
Console.WriteLine("- Configure Redis clustering for distributed operation");
Console.WriteLine("- Deploy to Kubernetes/ECS for container orchestration");

