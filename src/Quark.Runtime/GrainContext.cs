using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Lifecycle;
using Quark.Core.Abstractions.Reminders;
using Quark.Core.Abstractions.Timers;

namespace Quark.Runtime;

/// <summary>
///     Concrete implementation of <see cref="IGrainContext" /> for a single grain activation.
/// </summary>
public sealed class GrainContext : IGrainContext
{
    private volatile GrainActivationStatus _status = GrainActivationStatus.Activating;
    private Func<Func<Task>, ValueTask>? _scheduler;
    private Action<DeactivationReason>? _deactivationCallback;
    private Grain? _grain;
    private readonly Lock _timersLock = new();
    private readonly List<IGrainTimer> _timers = [];
    private long _deactivationNotBeforeTicks;

    /// <summary>Creates a context for the supplied grain identity.</summary>
    public GrainContext(GrainId grainId, IGrainFactory grainFactory, IServiceProvider serviceProvider)
    {
        GrainId = grainId;
        GrainFactory = grainFactory;
        ServiceProvider = serviceProvider;
        Lifecycle = new LifecycleSubject();
    }

    /// <summary>The lifecycle subject for this activation.</summary>
    public LifecycleSubject Lifecycle { get; }

    /// <summary>The reason this grain was asked to deactivate (set during deactivation).</summary>
    public DeactivationReason? DeactivationReason { get; private set; }

    /// <inheritdoc />
    public GrainId GrainId { get; }

    /// <inheritdoc />
    public IGrainFactory GrainFactory { get; }

    /// <inheritdoc />
    public IServiceProvider ServiceProvider { get; }

    /// <inheritdoc />
    public ILifecycleSubject ObservableLifecycle => Lifecycle;

    /// <inheritdoc />
    public GrainActivationStatus ActivationStatus => _status;

    /// <inheritdoc />
    public void Deactivate(DeactivationReason reason)
    {
        if (_status != GrainActivationStatus.Active &&
            _status != GrainActivationStatus.Activating)
        {
            return;
        }

        DeactivationReason = reason;
        _status = GrainActivationStatus.Deactivating;
        if (_deactivationCallback is { } cb)
        {
            cb(reason);
        }
        else
        {
            _ = StopInternalAsync(CancellationToken.None);
        }
    }

    /// <inheritdoc />
    public void DelayDeactivation(TimeSpan timeSpan)
    {
        long newTicks = (DateTimeOffset.UtcNow + timeSpan).UtcTicks;
        long current;
        do
        {
            current = Interlocked.Read(ref _deactivationNotBeforeTicks);
            if (newTicks <= current) return;
        } while (Interlocked.CompareExchange(ref _deactivationNotBeforeTicks, newTicks, current) != current);
    }

    /// <summary>
    ///     Returns <see langword="true"/> when automatic deactivation is currently allowed
    ///     — either no deadline has been set or the deadline has already passed.
    ///     Called by <see cref="GrainIdleCollector"/> before triggering idle deactivation.
    /// </summary>
    internal bool IsDeactivationAllowed(DateTimeOffset now)
    {
        long notBefore = Interlocked.Read(ref _deactivationNotBeforeTicks);
        return notBefore == 0 || now.UtcTicks >= notBefore;
    }

    /// <inheritdoc />
    public IGrainTimer RegisterTimer<TState>(
        Func<TState, CancellationToken, Task> callback,
        TState state,
        GrainTimerCreationOptions options)
    {
        if (_scheduler is null)
        {
            throw new InvalidOperationException(
                "Grain is not activated yet. Call RegisterGrainTimer from OnActivateAsync or a grain method.");
        }

        if (_status is GrainActivationStatus.Deactivating or GrainActivationStatus.Inactive)
        {
            throw new InvalidOperationException(
                "Cannot register a timer on a deactivating or inactive grain.");
        }

        var timer = new GrainTimer<TState>(callback, state, options, _scheduler);
        lock (_timersLock)
        {
            _timers.Add(timer);
        }
        return timer;
    }

    /// <inheritdoc />
    public IReminderService? ReminderService =>
        ServiceProvider.GetService(typeof(IReminderService)) as IReminderService;

    /// <summary>
    ///     Wires the grain's scheduler so timers can post callbacks through the turn-based queue.
    ///     Called by <see cref="GrainActivation" /> immediately after construction.
    /// </summary>
    internal void SetScheduler(Func<Func<Task>, ValueTask> scheduler)
    {
        _scheduler = scheduler;
    }

    /// <summary>
    ///     Registers the callback that <see cref="Deactivate" /> invokes to schedule the
    ///     deactivation work-item on the grain's turn-based queue.
    ///     Called by <see cref="GrainActivation" /> immediately after construction.
    /// </summary>
    internal void SetDeactivationCallback(Action<DeactivationReason> callback)
    {
        _deactivationCallback = callback;
    }

    /// <summary>
    ///     Runs the activation sequence: sets the context on the grain and calls lifecycle start.
    /// </summary>
    public async Task ActivateAsync(Grain grain, CancellationToken cancellationToken = default)
    {
        _grain = grain;
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
        DisposeTimers();
        await grain.OnDeactivateAsync(reason, cancellationToken).ConfigureAwait(false);
        await Lifecycle.StopAsync(cancellationToken).ConfigureAwait(false);
        _status = GrainActivationStatus.Inactive;
    }

    private void DisposeTimers()
    {
        IGrainTimer[] timers;
        lock (_timersLock)
        {
            timers = [.. _timers];
            _timers.Clear();
        }
        foreach (IGrainTimer timer in timers)
        {
            timer.Dispose();
        }
    }

    private async Task StopInternalAsync(CancellationToken cancellationToken)
    {
        try
        {
            DisposeTimers();
            if (_grain is { } grain && DeactivationReason is { } reason)
            {
                await grain.OnDeactivateAsync(reason, cancellationToken).ConfigureAwait(false);
            }

            await Lifecycle.StopAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _status = GrainActivationStatus.Inactive;
        }
    }
}
