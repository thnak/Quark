using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Identity;

namespace Quark.Diagnostics.Abstractions;

/// <summary>Fired after the grain has fully deactivated and been removed from the activation table.</summary>
public readonly struct GrainDeactivatedEvent(GrainId grainId, DeactivationReason reason, TimeSpan lifetime)
{
    public GrainId GrainId { get; } = grainId;
    public DeactivationReason Reason { get; } = reason;
    /// <summary>How long this activation was alive (from Active to Inactive).</summary>
    public TimeSpan Lifetime { get; } = lifetime;
}