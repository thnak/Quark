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

    /// <summary>
    /// Gets the grain factory for this activation.
    /// Use to obtain references to other grains from inside a grain.
    /// Drop-in equivalent of Orleans' <c>GrainFactory</c> property.
    /// </summary>
    protected IGrainFactory GrainFactory => GrainContext.GrainFactory;

    /// <summary>Gets the DI service provider for this activation.</summary>
    protected IServiceProvider ServiceProvider => GrainContext.ServiceProvider;

    /// <summary>Called when the grain is first activated. Override to perform async initialization.</summary>
    public virtual Task OnActivateAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    /// <summary>Called before the grain is deactivated. Override to persist state or clean up.</summary>
    public virtual Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken) =>
        Task.CompletedTask;

    /// <summary>
    /// Requests that this grain be deactivated once it becomes idle.
    /// Drop-in equivalent of Orleans' <c>DeactivateOnIdle()</c>.
    /// </summary>
    protected void DeactivateOnIdle() =>
        GrainContext.Deactivate(DeactivationReason.ApplicationRequested);

    /// <summary>
    /// Delays automatic deactivation by <paramref name="timeSpan"/> from now.
    /// Not yet implemented in M3; reserved for the idle-timeout scheduler (M3/M6).
    /// Drop-in equivalent of Orleans' <c>DelayDeactivation(TimeSpan)</c>.
    /// </summary>
    protected void DelayDeactivation(TimeSpan timeSpan)
    {
        // TODO (M3/M6): pass hint to per-grain idle timer.
        _ = timeSpan; // suppress unused warning until implemented.
    }

    /// <summary>
    /// Framework-only. Called by the runtime to bind the grain to its activation context.
    /// </summary>
    internal void SetContext(IGrainContext context) => _grainContext = context;
}

