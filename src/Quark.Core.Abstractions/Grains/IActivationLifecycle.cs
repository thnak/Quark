namespace Quark.Core.Abstractions.Grains;

/// <summary>
///     Optional interface for behaviors that need to run logic on first activation
///     or before deactivation. Both hooks run on the grain's mailbox thread.
///     The engine resolves a fresh behavior instance via the same per-call scope
///     mechanism to invoke these hooks.
/// </summary>
public interface IActivationLifecycle : IGrainBehavior
{
    Task OnActivateAsync(CancellationToken ct);
    Task OnDeactivateAsync(DeactivationReason reason, CancellationToken ct);
}
