using Quark.Core.Abstractions.Lifecycle;

namespace Quark.Core.Abstractions;

/// <summary>
/// Abstract base class for all grain implementations.
/// </summary>
public abstract class Grain : IGrain
{
    private IGrainContext? _grainContext;

    /// <summary>Gets the context for this grain activation.</summary>
    protected IGrainContext GrainContext =>
        _grainContext ?? throw new InvalidOperationException("Grain has not been activated.");

    /// <summary>Gets the identity of this grain.</summary>
    protected GrainId GrainId => GrainContext.GrainId;

    /// <summary>Called when the grain is first activated. Override to perform async initialization.</summary>
    public virtual Task OnActivateAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Called before the grain is deactivated. Override to persist state or clean up.</summary>
    public virtual Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    /// <summary>
    /// Framework-only. Called by the runtime to bind the grain to its activation context.
    /// </summary>
    internal void SetContext(IGrainContext context) => _grainContext = context;
}
