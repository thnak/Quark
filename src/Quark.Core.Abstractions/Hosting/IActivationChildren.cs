using Quark.Core.Abstractions.Identity;

namespace Quark.Core.Abstractions.Hosting;

/// <summary>
///     Scoped accessor that lets a behavior declare parent/child grain relationships.
///     Inject into a behavior constructor to opt-in to cascading termination.
///     The child set lives on the shell (in-memory) and is lost on deactivation —
///     re-<see cref="Attach"/> in <c>OnActivateAsync</c> for durable trees.
/// </summary>
public interface IActivationChildren
{
    /// <summary>
    ///     Declares <paramref name="child"/> as a child of this activation.
    ///     <paramref name="mode"/> controls what happens when the parent terminates.
    /// </summary>
    void Attach(GrainId child, ChildTerminationMode mode = ChildTerminationMode.Cascade);

    /// <summary>
    ///     Removes the child relationship for <paramref name="child"/>.
    ///     Returns <see langword="true"/> if the child was attached.
    /// </summary>
    bool Detach(GrainId child);
}
