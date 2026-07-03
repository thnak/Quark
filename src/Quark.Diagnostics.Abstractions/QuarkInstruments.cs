using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Quark.Diagnostics.Abstractions;

/// <summary>
///     Central home for all Quark OpenTelemetry instruments.
///     Instruments are always created; they emit data only when an OTel SDK
///     subscribes to <c>"Quark.Runtime"</c> (ActivitySource or Meter).
/// </summary>
public static class QuarkInstruments
{
    public const string SourceName = "Quark.Runtime";
    public const string Version = "1.0.0";

    // ── Tracing ──────────────────────────────────────────────────────────────

    public static readonly ActivitySource ActivitySource = new(SourceName, Version);

    // ── Metrics ──────────────────────────────────────────────────────────────

    private static readonly Meter _meter = new(SourceName, Version);

    // Counters
    public static readonly Counter<long> GrainActivationsCreated =
        _meter.CreateCounter<long>("quark.grain.activations.created",
            description: "Total number of grain activations created.");

    public static readonly Counter<long> GrainActivationsDeactivated =
        _meter.CreateCounter<long>("quark.grain.activations.deactivated",
            description: "Total number of grain activations deactivated.");

    public static readonly Counter<long> GrainInvocations =
        _meter.CreateCounter<long>("quark.grain.invocations",
            description: "Total number of grain method invocations.");

    public static readonly Counter<long> GrainInvocationErrors =
        _meter.CreateCounter<long>("quark.grain.invocations.errors",
            description: "Total number of grain method invocations that threw an exception.");

    public static readonly Counter<long> GatewayMessagesReceived =
        _meter.CreateCounter<long>("quark.gateway.messages.received",
            description: "Total gateway messages received, tagged by message_type.");

    // UpDownCounters (gauges)
    public static readonly UpDownCounter<long> ActiveGrainActivations =
        _meter.CreateUpDownCounter<long>("quark.grain.activations.active",
            description: "Current number of live grain activations.");

    public static readonly UpDownCounter<long> ActiveGatewayConnections =
        _meter.CreateUpDownCounter<long>("quark.gateway.connections.active",
            description: "Current number of active TCP gateway connections.");

    // Histograms
    public static readonly Histogram<double> GrainActivationDuration =
        _meter.CreateHistogram<double>("quark.grain.activation.duration_ms", unit: "ms",
            description: "Time from GrainActivation creation to OnActivateAsync completion.");

    public static readonly Histogram<double> GrainInvocationDuration =
        _meter.CreateHistogram<double>("quark.grain.invocation.duration_ms", unit: "ms",
            description: "End-to-end grain method invocation duration.");

    public static readonly Histogram<double> MailboxWaitDuration =
        _meter.CreateHistogram<double>("quark.grain.mailbox.wait_ms", unit: "ms",
            description: "Time a work item spends in the mailbox queue before execution starts.");

    // ── Scheduler metrics ────────────────────────────────────────────────────

    public static readonly UpDownCounter<long> SchedulerReadyQueueDepth =
        _meter.CreateUpDownCounter<long>("quark.scheduler.ready_queue.depth",
            description: "Current number of activations waiting in the scheduler ready queue.");

    public static readonly UpDownCounter<long> SchedulerActiveDrains =
        _meter.CreateUpDownCounter<long>("quark.scheduler.active_drains",
            description: "Current number of activation drain passes running concurrently.");

    public static readonly Counter<long> SchedulerDrainItems =
        _meter.CreateCounter<long>("quark.scheduler.drain.items",
            description: "Total work items processed across all scheduler drain passes.");

    public static readonly Counter<long> SchedulerDrainYields =
        _meter.CreateCounter<long>("quark.scheduler.drain.yields",
            description: "Total number of times a drain pass yielded because the budget was reached.");

    public static readonly Counter<long> SchedulerOverloadRejections =
        _meter.CreateCounter<long>("quark.scheduler.overload.rejections",
            description: "Total number of activations rejected because the scheduler ready queue was full.");

    public static readonly Histogram<double> SchedulerActivationWaitDuration =
        _meter.CreateHistogram<double>("quark.scheduler.activation_wait.duration_ms", unit: "ms",
            description: "Time an activation spent in the scheduler ready queue before a worker picked it up.");

    public static readonly Histogram<double> SchedulerDrainDuration =
        _meter.CreateHistogram<double>("quark.scheduler.drain.duration_ms", unit: "ms",
            description: "Wall-clock duration of a single scheduler drain pass.");
}
