using Quark.Core.Abstractions.Identity;

namespace Quark.Core.Abstractions.Hosting;

/// <summary>
///     Ambient per-call context. Registered as Scoped; available to behavior constructors
///     and any service they inject. Carries the identity of the grain being called.
/// </summary>
public interface ICallContext
{
    GrainId GrainId { get; }
}
