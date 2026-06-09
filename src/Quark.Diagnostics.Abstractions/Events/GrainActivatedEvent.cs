using Quark.Core.Abstractions.Identity;

namespace Quark.Diagnostics.Abstractions;

/// <summary>Fired after a grain activation completes <c>OnActivateAsync</c> and is marked Active.</summary>
public readonly struct GrainActivatedEvent(GrainId grainId, string behaviorTypeName, TimeSpan activationDuration)
{
    public GrainId GrainId { get; } = grainId;
    public string BehaviorTypeName { get; } = behaviorTypeName;
    public TimeSpan ActivationDuration { get; } = activationDuration;
}