using System.Collections.Concurrent;
using System.Diagnostics;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Timers;
using Quark.Diagnostics.Abstractions;
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
    private readonly IQuarkDiagnosticListener _diagnostics;
    private Func<Task>? _onDeactivated;
    private long _lastAccessedTicks;
    private long _deactivationNotBeforeTicks;
    private long _activatedAtTicks;   // Stopwatch ticks when MarkActive() is called
    private long _workItemStartedAt;  // Stopwatch ticks when a work item starts executing; 0 = idle
    private int _pendingWorkCount;    // work items queued but not yet executing
    private volatile GrainActivationStatus _status = GrainActivationStatus.Activating;

    private readonly Channel<Func<ValueTask>> _queue;
    private readonly int _mailboxCapacity;
    private readonly MailboxFullMode _mailboxFullMode;

    public GrainActivation(
        GrainId grainId,
        GrainType grainType,
        bool isReentrant,
        IServiceProvider root,
        ILogger<GrainActivation> logger,
        IQuarkDiagnosticListener? diagnostics = null,
        int mailboxCapacity = 0,
        MailboxFullMode mailboxFullMode = MailboxFullMode.Wait)
    {
        GrainId = grainId;
        GrainType = grainType;
        _isReentrant = isReentrant;
        _root = root;
        _logger = logger;
        _diagnostics = diagnostics ?? NullDiagnosticListener.Instance;
        _mailboxCapacity = mailboxCapacity;
        _mailboxFullMode = mailboxFullMode;
        _queue = CreateQueue(mailboxCapacity);
        _processingLoop = RunLoopAsync(_cts.Token);
        _lastAccessedTicks = DateTimeOffset.UtcNow.UtcTicks;
    }

    // A bounded mailbox caps the work a single grain can queue. We always create bounded channels in
    // FullMode.Wait; RejectWhenFull is enforced in PostAsync via TryWrite so a rejection raises a
    // clean MailboxFullException rather than silently dropping work.
    private static Channel<Func<ValueTask>> CreateQueue(int capacity)
        => capacity <= 0
            ? Channel.CreateUnbounded<Func<ValueTask>>(
                new UnboundedChannelOptions { SingleReader = true, AllowSynchronousContinuations = false })
            : Channel.CreateBounded<Func<ValueTask>>(
                new BoundedChannelOptions(capacity)
                {
                    SingleReader = true,
                    AllowSynchronousContinuations = false,
                    FullMode = BoundedChannelFullMode.Wait,
                });

    // Probe constructor — no processing loop, used only by BehaviorStartupValidator.
    private GrainActivation(GrainId grainId, GrainType grainType, IServiceProvider root)
    {
        GrainId = grainId;
        GrainType = grainType;
        _isReentrant = false;
        _root = root;
        _logger = NullLogger<GrainActivation>.Instance;
        _diagnostics = NullDiagnosticListener.Instance;
        _queue = CreateQueue(0);
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
    internal void MarkActive()
    {
        _status = GrainActivationStatus.Active;
        Interlocked.Exchange(ref _activatedAtTicks, Stopwatch.GetTimestamp());
    }

    /// <summary>
    ///     Stopwatch ticks at which the current mailbox work item started executing.
    ///     Zero means the mailbox is idle.  Read by <see cref="Quark.Diagnostics.StuckGrainDetector" />.
    /// </summary>
    internal long WorkItemStartedAt => Interlocked.Read(ref _workItemStartedAt);

    /// <summary>
    ///     Number of work items currently queued (not yet executing).
    ///     Read by <see cref="StuckGrainDetector" /> for diagnostic context.
    /// </summary>
    internal int PendingWorkCount => Volatile.Read(ref _pendingWorkCount);

    /// <summary>
    ///     Returns or creates the <see cref="StateHolder{TState}" /> for the given state type.
    ///     One holder per (activation, TState); shared between IActivationMemory and
    ///     IPersistentActivationMemory accessors.
    /// </summary>
    public StateHolder<TState> GetOrCreateHolder<TState>() where TState : class, new()
        => (StateHolder<TState>)_memoryBag.GetOrAdd(typeof(TState), static _ => new StateHolder<TState>());

    /// <summary>
    ///     Returns or creates the <see cref="ManagedActivationMemoryHolder{T}" /> for the given resource type.
    ///     One holder per (activation, T). Automatically disposed after <c>OnDeactivateAsync</c> completes.
    ///     Use <see cref="ManagedActivationMemoryHolder{T}.Init" /> and
    ///     <see cref="ManagedActivationMemoryHolder{T}.Destroy" /> to configure lifecycle delegates.
    /// </summary>
    public ManagedActivationMemoryHolder<T> GetOrCreateManagedHolder<T>() where T : class
        => (ManagedActivationMemoryHolder<T>)_memoryBag.GetOrAdd(
            typeof(ManagedKey<T>),
            static _ => new ManagedActivationMemoryHolder<T>());

    // Discriminator type so managed holders and state holders never share a key.
    private sealed class ManagedKey<T>;

    // Discriminator type so eager holders cannot collide with state or managed holders.
    private sealed class EagerKey<T>;

    /// <summary>
    ///     Returns or creates the <see cref="EagerActivationMemoryHolder{T}" /> for the given resource type.
    ///     One holder per (activation, T). Automatically disposed after <c>OnDeactivateAsync</c> completes.
    ///     The holder is initialized at activation time, before <c>OnActivateAsync</c>.
    /// </summary>
    public EagerActivationMemoryHolder<T> GetOrCreateEagerHolder<T>() where T : class
        => (EagerActivationMemoryHolder<T>)_memoryBag.GetOrAdd(
            typeof(EagerKey<T>),
            static _ => new EagerActivationMemoryHolder<T>());

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
        {
            throw new InvalidOperationException("Cannot register a timer on a deactivating or inactive grain.");
        }

        GrainId capturedId = GrainId;
        IQuarkDiagnosticListener capturedDiagnostics = _diagnostics;
        Func<TState, CancellationToken, Task> instrumented = async (s, ct) =>
        {
            long start = Stopwatch.GetTimestamp();
            Exception? error = null;
            try
            {
                await callback(s, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                error = ex;
                throw;
            }
            finally
            {
                capturedDiagnostics.OnTimerFired(new TimerFiredEvent(capturedId, Stopwatch.GetElapsedTime(start), error));
            }
        };

        var timer = new GrainTimer<TState>(instrumented, state, options, PostAsync);
        lock (_timersLock) { _timers.Add(timer); }
        return timer;
    }

    /// <summary>
    ///     Requests deactivation. Posts the full lifecycle teardown as the next mailbox work item.
    /// </summary>
    public void Deactivate(DeactivationReason reason)
    {
        if (_status != GrainActivationStatus.Active && _status != GrainActivationStatus.Activating)
        {
            return;
        }

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
            if (newTicks <= current)
            {
                return;
            }
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
        int depth = Interlocked.Increment(ref _pendingWorkCount);
        _diagnostics.OnMailboxEnqueued(new MailboxEnqueuedEvent(GrainId, depth));
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var ctx = ExecutionContext.Capture();

        Func<ValueTask> dispatched;
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

        if (_mailboxFullMode == MailboxFullMode.RejectWhenFull && _mailboxCapacity > 0)
        {
            // Fail fast when the bounded mailbox is full rather than waiting for space.
            if (!_queue.Writer.TryWrite(dispatched))
            {
                Interlocked.Decrement(ref _pendingWorkCount);
                throw new MailboxFullException(GrainId, _mailboxCapacity);
            }
        }
        else
        {
            await _queue.Writer.WriteAsync(dispatched, _cts.Token).ConfigureAwait(false);
        }

        await tcs.Task.ConfigureAwait(false);
    }

    private async ValueTask RunDeactivationAsync(DeactivationReason reason)
    {
        _diagnostics.OnGrainDeactivating(new GrainDeactivatingEvent(GrainId, reason));
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
        await DisposeManagedHoldersAsync().ConfigureAwait(false);
        _status = GrainActivationStatus.Inactive;
        _queue.Writer.TryComplete();

        long activatedAt = Interlocked.Read(ref _activatedAtTicks);
        TimeSpan lifetime = activatedAt > 0 ? Stopwatch.GetElapsedTime(activatedAt) : TimeSpan.Zero;
        _diagnostics.OnGrainDeactivated(new GrainDeactivatedEvent(GrainId, reason, lifetime));
        QuarkInstruments.GrainActivationsDeactivated.Add(1,
            new KeyValuePair<string, object?>("grain_type", GrainType.Value));
        QuarkInstruments.ActiveGrainActivations.Add(-1,
            new KeyValuePair<string, object?>("grain_type", GrainType.Value));
    }

    private async Task DisposeManagedHoldersAsync()
    {
        foreach (object obj in _memoryBag.Values)
        {
            if (obj is not IAsyncDisposable disposable)
            {
                continue;
            }

            try
            {
                await disposable.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "Error disposing managed activation memory holder for {GrainId}", GrainId);
            }
        }
    }

    internal async Task RunLifecycleHookAsync(
        Func<IActivationLifecycle, Task> hook,
        CancellationToken cancellationToken = default)
    {
        using IServiceScope scope = _root.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        IGrainBehavior behavior = await GrainScopeBinder.BindAndResolveAsync(
            sp,
            this,
            cancellationToken).ConfigureAwait(false);
        if (behavior is IActivationLifecycle lifecycle)
        {
            await hook(lifecycle).ConfigureAwait(false);
        }
    }

    // Runs the full activation sequence in a single scope:
    // 1. Bind shell accessor + call context.
    // 2. Resolve behavior (ctor fires; any IEagerActivationMemory<T>.Load() calls register factories).
    // 3. Initialize all eager holders with the scoped SP BEFORE OnActivateAsync.
    // 4. Call OnActivateAsync if the behavior implements IActivationLifecycle.
    internal async Task RunActivationAsync(CancellationToken ct)
    {
        using IServiceScope scope = _root.CreateScope();
        IServiceProvider sp = scope.ServiceProvider;
        ((ActivationShellAccessor)sp.GetRequiredService<IActivationShellAccessor>()).Shell = this;
        sp.GetRequiredService<ICallContextSetter>().Set(GrainId);
        IGrainBehavior behavior = sp.GetRequiredService<IBehaviorResolver>().Resolve(GrainType);
        await RunEagerInitAsync(sp, ct).ConfigureAwait(false);
        if (behavior is IActivationLifecycle lifecycle)
        {
            await lifecycle.OnActivateAsync(ct).ConfigureAwait(false);
        }
    }

    private async Task RunEagerInitAsync(IServiceProvider scopedServices, CancellationToken ct)
    {
        foreach (object obj in _memoryBag.Values)
        {
            if (obj is IEagerActivationMemoryHolder holder)
            {
                await holder.InitAsync(scopedServices, ct).ConfigureAwait(false);
            }
        }
    }

    private void DisposeTimers()
    {
        IGrainTimer[] timers;
        lock (_timersLock)
        {
            timers = [.. _timers];
            _timers.Clear();
        }
        foreach (IGrainTimer t in timers)
        {
            t.Dispose();
        }
    }

    private async Task RunLoopAsync(CancellationToken ct)
    {
        await foreach (Func<ValueTask> work in _queue.Reader.ReadAllAsync(ct).ConfigureAwait(false))
        {
            Interlocked.Decrement(ref _pendingWorkCount);
            Interlocked.Exchange(ref _workItemStartedAt, Stopwatch.GetTimestamp());
            try
            {
                await work().ConfigureAwait(false);
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Error executing grain method on {GrainId}", GrainId);
            }
            finally
            {
                Interlocked.Exchange(ref _workItemStartedAt, 0);
            }
        }
    }
}
