using Quark.Core.Abstractions.Identity;

namespace Quark.Diagnostics.Abstractions;

/// <summary>Fired when an activation is added to the scheduler ready queue.</summary>
public readonly struct SchedulerActivationScheduledEvent(GrainId grainId)
{
    public GrainId GrainId { get; } = grainId;
}
