using Quark.Core.Abstractions.Identity;

namespace Quark.Core.Abstractions.Hosting;

/// <summary>
///     Marks a generated grain proxy and exposes its grain identity.
///     Used by the grain-ref serialiser to extract the key without reflection or boxing.
/// </summary>
public interface IGrainProxy
{
    GrainId GrainId { get; }
}
