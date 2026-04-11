using Quark.Core.Abstractions;
using Quark.Core.Abstractions.Lifecycle;

namespace Quark.Runtime;

/// <summary>
/// Concrete implementation of <see cref="IGrainContext"/> for a single grain activation.
/// </summary>
public sealed class GrainContext : IGrainContext
{
    private volatile GrainActivationStatus _status = GrainActivationStatus.Activating;

    /// <summary>Creates a context for the supplied grain identity with its own lifecycle.</summary>
    public GrainContext(GrainId grainId)
    {
        GrainId = grainId;
        Lifecycle = new LifecycleSubject();
    }

    /// <inheritdoc/>
    public GrainId GrainId { get; }

    /// <summary>The lifecycle subject for this activation.</summary>
    public LifecycleSubject Lifecycle { get; }

    /// <inheritdoc/>
    public ILifecycleSubject ObservableLifecycle => Lifecycle;

    /// <inheritdoc/>
    public GrainActivationStatus ActivationStatus => _status;

    /// <summary>The reason this grain was asked to deactivate (set during deactivation).</summary>
    public DeactivationReason? DeactivationReason { get; private set; }

    /// <inheritdoc/>
    public void Deactivate(DeactivationReason reason)
    {
        if (_status == GrainActivationStatus.Active ||
            _status == GrainActivationStatus.Activating)
        {
            DeactivationReason = reason;
            _status = GrainActivationStatus.Deactivating;
            _ = StopInternalAsync(default);
        }
    }

    /// <summary>
    /// Runs the activation sequence: sets the context on the grain and calls lifecycle start.
    /// </summary>
    public async Task ActivateAsync(Grain grain, CancellationToken cancellationToken = default)
    {
        grain.SetContext(this);
        await Lifecycle.StartAsync(cancellationToken).ConfigureAwait(false);
        await grain.OnActivateAsync(cancellationToken).ConfigureAwait(false);
        _status = GrainActivationStatus.Active;
    }

    /// <summary>Runs the deactivation sequence and calls lifecycle stop.</summary>
    public async Task DeactivateAsync(Grain grain, DeactivationReason reason,
        CancellationToken cancellationToken = default)
    {
        _status = GrainActivationStatus.Deactivating;
        DeactivationReason = reason;
        await grain.OnDeactivateAsync(reason, cancellationToken).ConfigureAwait(false);
        await Lifecycle.StopAsync(cancellationToken).ConfigureAwait(false);
        _status = GrainActivationStatus.Inactive;
    }

    private async Task StopInternalAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Lifecycle.StopAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _status = GrainActivationStatus.Inactive;
        }
    }
}
