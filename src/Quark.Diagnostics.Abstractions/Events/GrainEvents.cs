using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Identity;

namespace Quark.Diagnostics.Abstractions;

/// <summary>Fired just before a new grain activation is created.</summary>
public readonly struct GrainActivatingEvent(GrainId grainId, string behaviorTypeName)
{
    public GrainId GrainId { get; } = grainId;
    public string BehaviorTypeName { get; } = behaviorTypeName;
}

/// <summary>Fired after a grain activation completes <c>OnActivateAsync</c> and is marked Active.</summary>
public readonly struct GrainActivatedEvent(GrainId grainId, string behaviorTypeName, TimeSpan activationDuration)
{
    public GrainId GrainId { get; } = grainId;
    public string BehaviorTypeName { get; } = behaviorTypeName;
    public TimeSpan ActivationDuration { get; } = activationDuration;
}

/// <summary>Fired at the start of the grain deactivation sequence.</summary>
public readonly struct GrainDeactivatingEvent(GrainId grainId, DeactivationReason reason)
{
    public GrainId GrainId { get; } = grainId;
    public DeactivationReason Reason { get; } = reason;
}

/// <summary>Fired after the grain has fully deactivated and been removed from the activation table.</summary>
public readonly struct GrainDeactivatedEvent(GrainId grainId, DeactivationReason reason, TimeSpan lifetime)
{
    public GrainId GrainId { get; } = grainId;
    public DeactivationReason Reason { get; } = reason;
    /// <summary>How long this activation was alive (from Active to Inactive).</summary>
    public TimeSpan Lifetime { get; } = lifetime;
}
