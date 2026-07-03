using Quark.Core.Abstractions.Identity;

namespace Quark.Diagnostics.Abstractions;

/// <summary>Fired when a scheduler drain pass completes (budget hit, queue empty, or deactivation).</summary>
public readonly struct SchedulerDrainCompletedEvent(GrainId grainId, int itemsProcessed, double durationMs)
{
    public GrainId GrainId { get; } = grainId;
    /// <summary>Number of work items processed during this drain pass.</summary>
    public int ItemsProcessed { get; } = itemsProcessed;
    /// <summary>Wall-clock duration of the drain pass in milliseconds.</summary>
    public double DurationMs { get; } = durationMs;
}
