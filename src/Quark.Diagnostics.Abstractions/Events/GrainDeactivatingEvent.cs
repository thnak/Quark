using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Identity;

namespace Quark.Diagnostics.Abstractions;

/// <summary>Fired at the start of the grain deactivation sequence.</summary>
public readonly struct GrainDeactivatingEvent(GrainId grainId, DeactivationReason reason)
{
    public GrainId GrainId { get; } = grainId;
    public DeactivationReason Reason { get; } = reason;
}