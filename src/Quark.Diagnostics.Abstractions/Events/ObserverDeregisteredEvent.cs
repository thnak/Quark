using Quark.Core.Abstractions.Identity;

namespace Quark.Diagnostics.Abstractions;

/// <summary>Fired on the silo when an observer back-channel entry is removed (connection closed).</summary>
public readonly struct ObserverDeregisteredEvent(GrainId observerId)
{
    public GrainId ObserverId { get; } = observerId;
}