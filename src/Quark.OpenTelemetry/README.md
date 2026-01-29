# Quark.OpenTelemetry

OpenTelemetry integration for Quark framework, providing distributed tracing and metrics instrumentation.

## Features

- **Distributed Tracing**: Activity tracking for actor activations, invocations, state operations, and streaming
- **Metrics**: Comprehensive metrics for actor lifecycle, performance, and resource utilization
- **Semantic Conventions**: Standardized attribute names for Quark-specific operations
- **AOT Compatible**: Zero reflection, works with Native AOT compilation

## Usage

### Adding OpenTelemetry to Your Silo

```csharp
using OpenTelemetry;
using OpenTelemetry.Trace;
using OpenTelemetry.Metrics;
using Quark.OpenTelemetry;

var builder = WebApplication.CreateBuilder(args);

// Add OpenTelemetry tracing
builder.Services.AddOpenTelemetry()
    .WithTracing(tracerBuilder =>
    {
        tracerBuilder
            .AddQuarkInstrumentation("MySiloService", "1.0.0")
            .AddConsoleExporter()
            .AddOtlpExporter(); // Export to OTLP collector
    })
    .WithMetrics(meterBuilder =>
    {
        meterBuilder
            .AddQuarkInstrumentation("MySiloService", "1.0.0")
            .AddPrometheusExporter()
            .AddConsoleExporter();
    });

// Add Quark silo
builder.Services.AddQuarkSilo(options =>
{
    options.SiloId = "silo-1";
    options.RedisConnectionString = "localhost:6379";
});

var app = builder.Build();

// Enable Prometheus scraping endpoint
app.UseOpenTelemetryPrometheusScrapingEndpoint();

await app.RunAsync();
```

## Activity Names

The following activities are tracked:

- `quark.actor.activate` - Actor activation
- `quark.actor.deactivate` - Actor deactivation
- `quark.actor.invoke` - Actor method invocation
- `quark.state.load` - State load operation
- `quark.state.save` - State save operation
- `quark.state.delete` - State delete operation
- `quark.stream.publish` - Stream message publish
- `quark.stream.consume` - Stream message consumption
- `quark.reminder.tick` - Reminder tick
- `quark.timer.tick` - Timer tick
- `quark.silo.startup` - Silo startup
- `quark.silo.shutdown` - Silo shutdown

## Attributes

Each activity includes semantic attributes:

- `quark.actor.type` - The actor type name
- `quark.actor.id` - The actor ID
- `quark.actor.method` - The actor method being invoked
- `quark.silo.id` - The silo ID
- `quark.silo.status` - The silo status
- `quark.call.local` - Whether the call is local or remote
- `quark.stream.id` - The stream ID
- `quark.stream.namespace` - The stream namespace
- `quark.reminder.name` - The reminder name
- `quark.timer.name` - The timer name
- `quark.placement.policy` - The placement policy used

## Metrics

The following metrics are exported:

### Counters
- `quark.actor.activations` - Number of actor activations
- `quark.actor.deactivations` - Number of actor deactivations
- `quark.actor.invocations` - Number of actor method invocations
- `quark.actor.failures` - Number of actor failures
- `quark.actor.restarts` - Number of actor restarts
- `quark.state.loads` - Number of state load operations
- `quark.state.saves` - Number of state save operations
- `quark.stream.messages.published` - Number of stream messages published
- `quark.stream.messages.consumed` - Number of stream messages consumed
- `quark.reminder.ticks` - Number of reminder ticks
- `quark.timer.ticks` - Number of timer ticks

### Histograms
- `quark.actor.activation.duration` - Actor activation duration (ms)
- `quark.actor.invocation.duration` - Actor method invocation duration (ms)
- `quark.state.load.duration` - State load duration (ms)
- `quark.state.save.duration` - State save duration (ms)
- `quark.mailbox.queue.depth` - Mailbox queue depth (messages)

### Gauges
- `quark.actor.active` - Number of currently active actors

## Integration with Application Insights, Jaeger, Zipkin

The instrumentation works with any OpenTelemetry-compatible backend:

```csharp
// Azure Application Insights
tracerBuilder.AddAzureMonitorTraceExporter(options =>
{
    options.ConnectionString = "...";
});

// Jaeger
tracerBuilder.AddJaegerExporter(options =>
{
    options.AgentHost = "localhost";
    options.AgentPort = 6831;
});

// Zipkin
tracerBuilder.AddZipkinExporter(options =>
{
    options.Endpoint = new Uri("http://localhost:9411/api/v2/spans");
});
```

## Example: Instrumenting Actor Code

To use tracing in your actor code, use the `QuarkActivitySource`:

```csharp
using System.Diagnostics;
using Quark.OpenTelemetry;

[Actor]
public class MyActor : ActorBase
{
    public async Task ProcessOrderAsync(Order order)
    {
        using var activity = QuarkActivitySource.Source.StartActivity(
            QuarkActivitySource.Activities.ActorInvocation);
        
        activity?.SetTag(QuarkActivitySource.Attributes.ActorType, nameof(MyActor));
        activity?.SetTag(QuarkActivitySource.Attributes.ActorId, ActorId);
        activity?.SetTag(QuarkActivitySource.Attributes.ActorMethod, nameof(ProcessOrderAsync));
        
        // Your actor logic here
        await Task.Delay(100);
        
        QuarkMetrics.ActorInvocations.Add(1, 
            new KeyValuePair<string, object?>(QuarkActivitySource.Attributes.ActorType, nameof(MyActor)));
    }
}
```

## License

This project is part of the Quark framework and is licensed under the MIT License.
