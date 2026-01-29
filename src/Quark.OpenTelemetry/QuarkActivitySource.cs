using System.Diagnostics;

namespace Quark.OpenTelemetry;

/// <summary>
/// Provides ActivitySource instances for Quark framework telemetry.
/// </summary>
public static class QuarkActivitySource
{
    /// <summary>
    /// The name of the Quark activity source.
    /// </summary>
    public const string SourceName = "Quark.Framework";

    /// <summary>
    /// The version of the Quark framework.
    /// </summary>
    public const string Version = "0.1.0";

    /// <summary>
    /// The main ActivitySource for Quark framework operations.
    /// </summary>
    public static readonly ActivitySource Source = new ActivitySource(SourceName, Version);

    /// <summary>
    /// Semantic conventions for Quark telemetry attributes.
    /// </summary>
    public static class Attributes
    {
        /// <summary>
        /// The actor type name.
        /// </summary>
        public const string ActorType = "quark.actor.type";

        /// <summary>
        /// The actor ID.
        /// </summary>
        public const string ActorId = "quark.actor.id";

        /// <summary>
        /// The actor method being invoked.
        /// </summary>
        public const string ActorMethod = "quark.actor.method";

        /// <summary>
        /// The silo ID.
        /// </summary>
        public const string SiloId = "quark.silo.id";

        /// <summary>
        /// The silo status.
        /// </summary>
        public const string SiloStatus = "quark.silo.status";

        /// <summary>
        /// Whether the call is local or remote.
        /// </summary>
        public const string IsLocal = "quark.call.local";

        /// <summary>
        /// The stream ID for streaming operations.
        /// </summary>
        public const string StreamId = "quark.stream.id";

        /// <summary>
        /// The stream namespace.
        /// </summary>
        public const string StreamNamespace = "quark.stream.namespace";

        /// <summary>
        /// The reminder name.
        /// </summary>
        public const string ReminderName = "quark.reminder.name";

        /// <summary>
        /// The timer name.
        /// </summary>
        public const string TimerName = "quark.timer.name";

        /// <summary>
        /// The placement policy used.
        /// </summary>
        public const string PlacementPolicy = "quark.placement.policy";
    }

    /// <summary>
    /// Activity names for different operations.
    /// </summary>
    public static class Activities
    {
        /// <summary>
        /// Actor activation activity.
        /// </summary>
        public const string ActorActivation = "quark.actor.activate";

        /// <summary>
        /// Actor deactivation activity.
        /// </summary>
        public const string ActorDeactivation = "quark.actor.deactivate";

        /// <summary>
        /// Actor method invocation activity.
        /// </summary>
        public const string ActorInvocation = "quark.actor.invoke";

        /// <summary>
        /// State load activity.
        /// </summary>
        public const string StateLoad = "quark.state.load";

        /// <summary>
        /// State save activity.
        /// </summary>
        public const string StateSave = "quark.state.save";

        /// <summary>
        /// State delete activity.
        /// </summary>
        public const string StateDelete = "quark.state.delete";

        /// <summary>
        /// Stream publish activity.
        /// </summary>
        public const string StreamPublish = "quark.stream.publish";

        /// <summary>
        /// Stream consume activity.
        /// </summary>
        public const string StreamConsume = "quark.stream.consume";

        /// <summary>
        /// Reminder tick activity.
        /// </summary>
        public const string ReminderTick = "quark.reminder.tick";

        /// <summary>
        /// Timer tick activity.
        /// </summary>
        public const string TimerTick = "quark.timer.tick";

        /// <summary>
        /// Silo startup activity.
        /// </summary>
        public const string SiloStartup = "quark.silo.startup";

        /// <summary>
        /// Silo shutdown activity.
        /// </summary>
        public const string SiloShutdown = "quark.silo.shutdown";
    }
}
