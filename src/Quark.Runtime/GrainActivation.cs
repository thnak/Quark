using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;
using Quark.Core.Abstractions.Timers;
using Quark.Persistence.Abstractions;

namespace Quark.Runtime;

/// <summary>
///     Represents a single live grain activation on this silo.
///     Owns the sequential mailbox and the activation-scoped memory bag.
///     Behavior objects are constructed per-call from a fresh <see cref="IServiceScope" />.
/// </summary>
public sealed class GrainActivation : IAsyncDisposable
{
    private readonly CancellationTokenSource _cts = new();
    private readonly ILogger<GrainActivation> _logger;
    private readonly Task _processingLoop;
    private readonly bool _isReentrant;
    private readonly IServiceProvider _root;
    private readonly ConcurrentDictionary<Type, object> _memoryBag = new();
    private readonly Lock _timersLock = new();
    private readonly List<IGrainTimer> _timers = [];
    private Func<Task>? _onDeactivated;
    private long _lastAccessedTicks;
    private long _deactivationNotBeforeTicks;
    private volatile GrainActivationStatus _status = GrainActivationStatus.Activating;

    private readonly Channel<Func<Task>> _queue = Channel.CreateUnbounded<Func<Task>>(
        new UnboundedChannelOptions { SingleReader = true, AllowSynchronousContinuations = false });

    public GrainActivation(
        GrainId grainId,
        GrainType grainType,
        bool isReentrant,
        IServiceProvider root,
        ILogger<GrainActivation> logger)
    {
        GrainId = grainId;
        GrainType = grainType;
        _isReentrant = isReentrant;
        _root = root;
        _logger = logger;
        _processingLoop = RunLoopAsync(_cts.Token);
        _lastAccessedTicks = DateTimeOffset.UtcNow.UtcTicks;
    }

    // Probe constructor — no processing loop, used only by BehaviorStartupValidator.
    private GrainActivation(GrainId grainId, GrainType grainType, IServiceProvider root)
    {
        GrainId = grainId;
        GrainType = grainType;
        _isReentrant = false;
        _root = root;
        _logger = NullLogger<GrainActivation>.Instance;
        _processingLoop = Task.CompletedTask;
        _status = GrainActivationStatus.Active;
        _lastAccessedTicks = 0;
    }

    /// <summary>
    ///     Creates a lightweight probe shell for startup DI validation.
    ///     Does not start a processing loop.
    /// </summary>
    internal static GrainActivation CreateProbe(GrainId grainId, GrainType grainType, IServiceProvider root)
        => new(grainId, grainType, root);

    /// <summary>The stable identity of this grain.</summary>
    public GrainId GrainId { get; }

    /// <summary>The grain type key used to look up the behavior class.</summary>
    public GrainType GrainType { get; }

    /// <summary>Current lifecycle status of this activation.</summary>
    public GrainActivationStatus ActivationStatus => _status;

    /// <summary>Marks this activation as fully active. Called by LocalGrainCallInvoker after OnActivateAsync.</summary>
    internal void MarkActive() => _status = GrainActivationStatus.Active;

    /// <summary>
    ///     Returns or creates the <see cref="StateHolder{TState}" /> for the given state type.
    ///     One holder per (activation, TState); shared between IActivationMemory and
    ///     IPersistentActivationMemory accessors.
    /// </summary>
    public StateHolder<TState> GetOrCreateHolder<TState>() where TState : class, new()
        => (StateHolder<TState>)_memoryBag.GetOrAdd(typeof(TState), static _ => new StateHolder<TState>());

    /// <summary>
    ///     Gets or creates an activation-scoped singleton of type <typeparamref name="T" />.
    ///     The factory is invoked at most once per activation lifetime.
    ///     Use for services that must be shared across all per-call scopes of the same activation.
    /// </summary>
    public T GetOrCreate<T>(Func<T> factory) where T : class
        => (T)_memoryBag.GetOrAdd(typeof(T), _ => factory());

    /// <summary>
    ///     Registers a grain-scoped timer. The timer posts callbacks through this grain's mailbox.
    ///     Automatically disposed when the activation deactivates.
    /// </summary>
    public IGrainTimer RegisterTimer<TState>(
        Func<TState, CancellationToken, Task> callback,
        TState state,
        GrainTimerCreationOptions options)
    {
        if (_status is GrainActivationStatus.Deactivating or GrainActivationStatus.Inactive)
            throw new InvalidOperationException("Cannot register a timer on a deactivating or inactive grain.");

        var timer = new GrainTimer<TState>(callback, state, options, PostAsync);
        lock (_timersLock) { _timers.Add(timer); }
        return timer;
    }

    /// <summary>
    ///     Requests deactivation. Posts the full lifecycle teardown as the next mailbox work item.
    /// </summary>
    public void Deactivate(DeactivationReason reason)
    {
        if (_status != GrainActivationStatus.Active && _status != GrainActivationStatus.Activating)
            return;

        _status = GrainActivationStatus.Deactivating;
        _queue.Writer.TryWrite(() => RunDeactivationAsync(reason));

        _ = _processingLoop.ContinueWith(
            _ => _onDeactivated?.Invoke() ?? Task.CompletedTask,
            TaskScheduler.Default).Unwrap();
    }

    /// <inheritdoc cref="IGrainContext.DelayDeactivation" />
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
    ///     Returns <see langword="true"/> when idle deactivation is currently permitted —
    ///     no delay deadline has been set, or the deadline has already passed.
    /// </summary>
    internal bool IsDeactivationAllowed(DateTimeOffset now)
    {
        long notBefore = Interlocked.Read(ref _deactivationNotBeforeTicks);
        return notBefore == 0 || now.UtcTicks >= notBefore;
    }

    /// <summary>
    ///     Returns <see langword="true"/> when this activation has received no calls for longer
    ///     than <paramref name="threshold"/> as measured from <paramref name="now"/>.
    /// </summary>
    public bool IsIdleLongerThan(TimeSpan threshold, DateTimeOffset now)
    {
        long lastTicks = Interlocked.Read(ref _lastAccessedTicks);
        return (now.UtcTicks - lastTicks) > threshold.Ticks;
    }

    /// <summary>
    ///     Registers a callback invoked after the grain's deactivation sequence completes.
    ///     Used by <see cref="LocalGrainCallInvoker"/> to remove the activation from the table.
    /// </summary>
    internal void SetOnDeactivated(Func<Task> onDeactivated)
    {
        _onDeactivated = onDeactivated;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_status is GrainActivationStatus.Active or GrainActivationStatus.Activating)
        {
            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                await PostAsync(async () =>
                {
                    try
                    {
                        await RunDeactivationAsync(DeactivationReason.ShuttingDown).ConfigureAwait(false);
                    }
                    finally
                    {
                        done.TrySetResult();
                    }
                }).ConfigureAwait(false);
                await done.Task.ConfigureAwait(false);
            }
            catch
            {
                done.TrySetResult();
            }
        }

        await _cts.CancelAsync();
        _queue.Writer.TryComplete();
        try
        {
            await _processingLoop.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        _cts.Dispose();
    }

    // -----------------------------------------------------------------------

    /// <summary>
    ///     Posts a unit of work to this grain's sequential mailbox and awaits its completion.
    ///     Reentrant grains bypass the queue and execute immediately.
    /// </summary>
    public async ValueTask PostAsync(Func<Task> workItem)
    {
        if (_isReentrant)
        {
            _cts.Token.ThrowIfCancellationRequested();
            Interlocked.Exchange(ref _lastAccessedTicks, DateTimeOffset.UtcNow.UtcTicks);
            await workItem().ConfigureAwait(false);
            return;
        }

        Interlocked.Exchange(ref _lastAccessedTicks, DateTimeOffset.UtcNow.UtcTicks);
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ctx = ExecutionContext.Capture();

        Func<Task> dispatched;
        if (ctx is null)
        {
            dispatched = async () =>
            {
                try { await workItem().ConfigureAwait(false); tcs.TrySetResult(); }
                catch (Exception ex) { tcs.TrySetException(ex); }
            };
        }
        else
        {
            dispatched = async () =>
            {
                try
                {
                    Task? task = null;
                    ExecutionContext.Run(ctx, _ => task = workItem(), null);
                    await task!.ConfigureAwait(false);
                    tcs.TrySetResult();
                }
                catch (Exception ex) { tcs.TrySetException(ex); }
            };
        }

        await _queue.Writer.WriteAsync(dispatched, _cts.Token).ConfigureAwait(false);
        await tcs.Task.ConfigureAwait(false);
    }

    private async Task RunDeactivationAsync(DeactivationReason reason)
    {
        DisposeTimers();
        try
        {
            await RunLifecycleHookAsync(
                lifecycle => lifecycle.OnDeactivateAsync(reason, CancellationToken.None)).ConfigureAwait(false);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Error running OnDeactivateAsync lifecycle hook for {GrainId}", GrainId);
        }
        _status = GrainActivationStatus.Inactive;
        _queue.Writer.TryComplete();
    }

    internal async Task RunLifecycleHookAsync(Func<IActivationLifecycle, Task> hook)
    {
        using IServiceScope scope = _root.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        ((ActivationShellAccessor)sp.GetRequiredService<IActivationShellAccessor>()).Shell = this;
        sp.GetRequiredService<ICallContextSetter>().Set(GrainId);
        IGrainBehavior behavior = sp.GetRequiredService<IBehaviorResolver>().Resolve(GrainType);
        if (behavior is IActivationLifecycle lifecycle)
            await hook(lifecycle).ConfigureAwait(false);
    }

    private void DisposeTimers()
    {
        IGrainTimer[] timers;
        lock (_timersLock)
        {
            timers = [.. _timers];
            _timers.Clear();
        }
        foreach (IGrainTimer t in timers) t.Dispose();
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        await foreach (Func<Task> work in _queue.Reader.ReadAllAsync(ct).ConfigureAwait(false))
        {
            try
            {
                await work().ConfigureAwait(false);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error executing grain method on {GrainId}", GrainId);
            }
        }
    }
}
