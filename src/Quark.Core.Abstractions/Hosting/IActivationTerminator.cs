using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Identity;

namespace Quark.Core.Abstractions.Hosting;

/// <summary>
///     Best-effort, fire-and-forget termination of a grain activation by <see cref="GrainId"/>,
///     wherever it lives.  Local activations are deactivated via their mailbox; remote activations
///     receive a one-way <c>TerminateRequest</c> frame.
///     Never blocks the caller; never throws for an unreachable target.
/// </summary>
public interface IActivationTerminator
{
    void Terminate(GrainId target, DeactivationReason reason);
}
