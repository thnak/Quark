using Quark.Core.Abstractions.Hosting;

namespace Quark.Runtime;

/// <summary>
///     Scoped <see cref="IActivationChildren"/> that projects the shell's <see cref="ChildRegistry"/>
///     into the per-call scope.  Mirrors <see cref="Quark.Persistence.Abstractions.ActivationMemoryAccessor{TState}"/>.
/// </summary>
internal sealed class ActivationChildrenAccessor(ChildRegistry registry) : IActivationChildren
{
    public void Attach(GrainId child, ChildTerminationMode mode = ChildTerminationMode.Cascade)
        => registry.Attach(child, mode);

    public bool Detach(GrainId child)
        => registry.Detach(child);
}
