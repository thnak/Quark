using Quark.Core.Abstractions.Identity;

namespace Quark.Core.Abstractions.Hosting;

/// <summary>
///     Marks a generated observer proxy and exposes its grain identity.
///     Used by the observer-ref serialiser to extract the GrainId without reflection.
/// </summary>
public interface IGrainObserverProxy
{
    GrainId GrainId { get; }
}
