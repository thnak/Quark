using System.Diagnostics.Metrics;

namespace Quark.OpenTelemetry;

/// <summary>
/// Provides metrics instrumentation for Quark framework.
/// </summary>
public static class QuarkMetrics
{
    /// <summary>
    /// The name of the Quark meter.
    /// </summary>
    public const string MeterName = "Quark.Framework";

    /// <summary>
    /// The main Meter for Quark framework metrics.
    /// </summary>
    public static readonly Meter Meter = new Meter(MeterName, QuarkActivitySource.Version);

    /// <summary>
    /// Counter for actor activations.
    /// </summary>
    public static readonly Counter<long> ActorActivations = Meter.CreateCounter<long>(
        "quark.actor.activations",
        unit: "{activation}",
        description: "The number of actor activations");

    /// <summary>
    /// Counter for actor deactivations.
    /// </summary>
    public static readonly Counter<long> ActorDeactivations = Meter.CreateCounter<long>(
        "quark.actor.deactivations",
        unit: "{deactivation}",
        description: "The number of actor deactivations");

    /// <summary>
    /// Counter for actor method invocations.
    /// </summary>
    public static readonly Counter<long> ActorInvocations = Meter.CreateCounter<long>(
        "quark.actor.invocations",
        unit: "{invocation}",
        description: "The number of actor method invocations");

    /// <summary>
    /// Histogram for actor activation duration.
    /// </summary>
    public static readonly Histogram<double> ActorActivationDuration = Meter.CreateHistogram<double>(
        "quark.actor.activation.duration",
        unit: "ms",
        description: "The duration of actor activations in milliseconds");

    /// <summary>
    /// Histogram for actor method invocation duration.
    /// </summary>
    public static readonly Histogram<double> ActorInvocationDuration = Meter.CreateHistogram<double>(
        "quark.actor.invocation.duration",
        unit: "ms",
        description: "The duration of actor method invocations in milliseconds");

    /// <summary>
    /// Function to register an observable gauge for active actors count.
    /// </summary>
    /// <param name="observeValue">Function to retrieve the current active actor count.</param>
    /// <returns>The registered observable gauge.</returns>
    public static ObservableGauge<int> CreateActiveActorsGauge(Func<int> observeValue)
    {
        return Meter.CreateObservableGauge<int>(
            "quark.actor.active",
            observeValue,
            unit: "{actor}",
            description: "The number of currently active actors");
    }

    /// <summary>
    /// Counter for state load operations.
    /// </summary>
    public static readonly Counter<long> StateLoads = Meter.CreateCounter<long>(
        "quark.state.loads",
        unit: "{operation}",
        description: "The number of state load operations");

    /// <summary>
    /// Counter for state save operations.
    /// </summary>
    public static readonly Counter<long> StateSaves = Meter.CreateCounter<long>(
        "quark.state.saves",
        unit: "{operation}",
        description: "The number of state save operations");

    /// <summary>
    /// Histogram for state load duration.
    /// </summary>
    public static readonly Histogram<double> StateLoadDuration = Meter.CreateHistogram<double>(
        "quark.state.load.duration",
        unit: "ms",
        description: "The duration of state load operations in milliseconds");

    /// <summary>
    /// Histogram for state save duration.
    /// </summary>
    public static readonly Histogram<double> StateSaveDuration = Meter.CreateHistogram<double>(
        "quark.state.save.duration",
        unit: "ms",
        description: "The duration of state save operations in milliseconds");

    /// <summary>
    /// Counter for stream messages published.
    /// </summary>
    public static readonly Counter<long> StreamMessagesPublished = Meter.CreateCounter<long>(
        "quark.stream.messages.published",
        unit: "{message}",
        description: "The number of stream messages published");

    /// <summary>
    /// Counter for stream messages consumed.
    /// </summary>
    public static readonly Counter<long> StreamMessagesConsumed = Meter.CreateCounter<long>(
        "quark.stream.messages.consumed",
        unit: "{message}",
        description: "The number of stream messages consumed");

    /// <summary>
    /// Counter for reminder ticks.
    /// </summary>
    public static readonly Counter<long> ReminderTicks = Meter.CreateCounter<long>(
        "quark.reminder.ticks",
        unit: "{tick}",
        description: "The number of reminder ticks");

    /// <summary>
    /// Counter for timer ticks.
    /// </summary>
    public static readonly Counter<long> TimerTicks = Meter.CreateCounter<long>(
        "quark.timer.ticks",
        unit: "{tick}",
        description: "The number of timer ticks");

    /// <summary>
    /// Histogram for mailbox queue depth.
    /// </summary>
    public static readonly Histogram<int> MailboxQueueDepth = Meter.CreateHistogram<int>(
        "quark.mailbox.queue.depth",
        unit: "{message}",
        description: "The number of messages in actor mailbox queues");

    /// <summary>
    /// Counter for actor failures.
    /// </summary>
    public static readonly Counter<long> ActorFailures = Meter.CreateCounter<long>(
        "quark.actor.failures",
        unit: "{failure}",
        description: "The number of actor failures");

    /// <summary>
    /// Counter for actor restarts.
    /// </summary>
    public static readonly Counter<long> ActorRestarts = Meter.CreateCounter<long>(
        "quark.actor.restarts",
        unit: "{restart}",
        description: "The number of actor restarts");
}
