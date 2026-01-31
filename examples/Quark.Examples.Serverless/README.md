# Quark Serverless Actors Example

This example demonstrates **Serverless Actors** with automatic scaling from zero - a key feature for pay-per-use and event-driven scenarios.

## What are Serverless Actors?

Serverless actors automatically:
- **Deactivate when idle** - No resource consumption when not processing work
- **Scale to zero** - Complete shutdown when no traffic
- **Reactivate on demand** - Near-instant activation on first request (< 10ms with AOT)
- **Cost-efficient** - Only pay for actual usage

## Features Demonstrated

1. **Auto-Deactivation**: Actors automatically deactivate after configurable idle timeout
2. **Scale to Zero**: All actors can be deactivated, consuming zero resources
3. **Activity Tracking**: Built-in tracking of actor activity for deactivation decisions
4. **Configurable Policies**: Customizable idle timeout and minimum active actors
5. **Minimal Overhead**: <10ms activation time with Native AOT compilation

## Configuration

```csharp
.WithServerlessActors(options =>
{
    options.Enabled = true;
    options.IdleTimeout = TimeSpan.FromSeconds(10);  // Deactivate after 10s idle
    options.CheckInterval = TimeSpan.FromSeconds(5); // Check every 5s
    options.MinimumActiveActors = 0;                 // Scale to zero
})
```

## Running the Example

```bash
# From the repository root
dotnet run --project examples/Quark.Examples.Serverless

# Or build and run
dotnet build examples/Quark.Examples.Serverless
dotnet examples/Quark.Examples.Serverless/bin/Debug/net10.0/Quark.Examples.Serverless.dll
```

## Expected Output

```
=== Quark Serverless Actors Example ===

Starting host...
✓ Host started with serverless actors enabled

Creating serverless worker actors...
info: ServerlessWorkerActor: ServerlessWorker worker-1 ACTIVATED at 2026-01-31T...
info: ServerlessWorkerActor: ServerlessWorker worker-2 ACTIVATED at 2026-01-31T...
✓ Created 2 active actors

Processing work with actors...
✓ Processed: Task 1 (by worker-1)
✓ Processed: Task 2 (by worker-2)

Waiting 15 seconds for idle deactivation...
(Actors should auto-deactivate after 10 seconds of inactivity)
info: ServerlessWorkerActor: ServerlessWorker worker-1 DEACTIVATED at 2026-01-31T...
info: ServerlessWorkerActor: ServerlessWorker worker-2 DEACTIVATED at 2026-01-31T...
✓ Active actors after idle timeout: 0

✓ SUCCESS: All actors were auto-deactivated (scaled to zero)

Simulating new request (will create new actor instance)...
info: ServerlessWorkerActor: ServerlessWorker worker-3 ACTIVATED at 2026-01-31T...
✓ Processed: Task 3 (after idle period) (by worker-3)

=== Example completed successfully ===
```

## Use Cases

- **Serverless APIs**: HTTP endpoints with infrequent traffic
- **Webhooks**: Event-driven processing with sporadic calls
- **Scheduled Jobs**: Cron-like tasks that run periodically
- **Background Workers**: Occasional data processing tasks
- **Event Handlers**: Responding to external events (SQS, Kafka, etc.)

## Advanced Configuration

### Custom Deactivation Policy

```csharp
public class CustomDeactivationPolicy : IActorDeactivationPolicy
{
    public bool ShouldDeactivate(
        string actorId, 
        string actorType, 
        DateTimeOffset lastActivityTime, 
        int currentQueueDepth, 
        int activeCallCount)
    {
        // Custom logic - e.g., time-of-day based, load-based, etc.
        return currentQueueDepth == 0 && 
               DateTimeOffset.UtcNow.Hour >= 22; // Deactivate after 10 PM
    }
}

// Register custom policy
.WithServerlessActors(
    sp => new CustomDeactivationPolicy(),
    options => { options.Enabled = true; })
```

### Integration with Kubernetes

```yaml
apiVersion: apps/v1
kind: Deployment
metadata:
  name: quark-serverless-silo
spec:
  replicas: 1
  template:
    spec:
      containers:
      - name: silo
        image: my-quark-app:latest
        env:
        - name: ServerlessActors__Enabled
          value: "true"
        - name: ServerlessActors__IdleTimeout
          value: "00:05:00"  # 5 minutes
        resources:
          requests:
            memory: "128Mi"
            cpu: "100m"
          limits:
            memory: "512Mi"
            cpu: "500m"
```

## Performance Characteristics

- **Activation Time**: < 10ms (with Native AOT)
- **Memory Overhead**: < 1KB per idle actor
- **Deactivation Latency**: Configurable (1-60 seconds typical)
- **Throughput**: Same as regular actors during active periods

## Related Examples

- `Quark.Examples.StatelessWorkers` - High-throughput stateless processing
- `Quark.Examples.Basic` - Actor fundamentals
- `Quark.Examples.Supervision` - Actor lifecycle management

## Learn More

- [Serverless Actors Documentation](../../docs/ENHANCEMENTS.md#1021-serverless-actors)
- [Actor Lifecycle](../../docs/ACTOR_LIFECYCLE.md)
- [Performance Guide](../../docs/PERFORMANCE.md)
