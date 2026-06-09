using Quark.Core.Abstractions.Identity;

namespace Quark.Diagnostics.Abstractions;

/// <summary>Fired just before a new grain activation is created.</summary>
public readonly struct GrainActivatingEvent(GrainId grainId, string behaviorTypeName)
{
    public GrainId GrainId { get; } = grainId;
    public string BehaviorTypeName { get; } = behaviorTypeName;
}