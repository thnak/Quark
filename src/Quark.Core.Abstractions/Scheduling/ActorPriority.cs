namespace Quark.Core.Abstractions.Scheduling;

/// <summary>
///     Relative scheduling priority of a whole activation (grain), governing which <em>actor</em> a
///     worker runs next when several are ready — distinct from <see cref="MessagePriority"/>, which
///     orders messages within one actor. Higher-priority activations are dispatched ahead of
///     lower-priority ones. The scheduler resolves an activation's effective band as the maximum of its
///     actor priority and the priority of its highest pending message, so an urgent message to a
///     low-priority actor still schedules promptly.
/// </summary>
public enum ActorPriority : byte
{
    /// <summary>Scheduled only when no higher-priority activation is ready.</summary>
    Low = 0,

    /// <summary>The default for an activation with no declared priority.</summary>
    Normal = 1,

    /// <summary>Scheduled ahead of <see cref="Normal"/> and <see cref="Low"/> activations.</summary>
    High = 2,
}
