using Quark.Core.Abstractions.Identity;

namespace Quark.Core.Abstractions.Hosting;

/// <summary>
///     Ambient per-call context. Registered as Scoped; available to behavior constructors
///     and any service they inject. Carries the identity of the grain being called.
/// </summary>
public interface ICallContext
{
    GrainId GrainId { get; }

    /// <summary>
    ///     The caller-supplied idempotency key for this call, or <c>null</c> when none was set.
    ///     Stamped server-side from the <c>x-quark-idem</c> message header.
    /// </summary>
    string? IdempotencyKey => null;
}
