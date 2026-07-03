using Quark.Core.Abstractions.Identity;

namespace Quark.Diagnostics.Abstractions;

/// <summary>
///     Fired when a drain pass yields the activation back to the scheduler ready queue because
///     the drain budget was reached and more work remains.
/// </summary>
public readonly struct SchedulerDrainYieldedEvent(GrainId grainId, int itemsProcessed)
{
    public GrainId GrainId { get; } = grainId;
    /// <summary>Number of work items processed before yielding (equals the configured drain budget).</summary>
    public int ItemsProcessed { get; } = itemsProcessed;
}
