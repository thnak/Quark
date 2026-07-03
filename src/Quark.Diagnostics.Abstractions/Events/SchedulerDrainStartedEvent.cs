using Quark.Core.Abstractions.Identity;

namespace Quark.Diagnostics.Abstractions;

/// <summary>Fired when a scheduler worker begins draining an activation's mailbox.</summary>
public readonly struct SchedulerDrainStartedEvent(GrainId grainId)
{
    public GrainId GrainId { get; } = grainId;
}
