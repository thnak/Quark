namespace Quark.Tests.Unit.FailureSemantics;

/// <summary>
///     DI singleton test double recording whether an <c>IManagedActivationMemory&lt;T&gt;</c>'s
///     Destroy callback ran during deactivation — the grain's own state is discarded along with the
///     activation, so this has to be observed from outside the grain.
/// </summary>
public sealed class DeactivationTracker
{
    public bool ManagedHolderDestroyed { get; set; }
}
