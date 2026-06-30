using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using System.Threading.Tasks.Sources;
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
    // One pooled MailboxWorkItem cached per thread; avoids per-call heap allocation on the hot path.
    [ThreadStatic]
    private static MailboxWorkItem? t_cachedItem;

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

    // Stored so Deactivate() can post a fire-and-forget item without creating a capturing lambda.
    private DeactivationReason _pendingDeactivationReason = null!;

    private readonly Channel<MailboxWorkItem> _queue;
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
    private static Channel<MailboxWorkItem> CreateQueue(int capacity)
        => capacity <= 0
            ? Channel.CreateUnbounded<MailboxWorkItem>(
                new UnboundedChannelOptions { SingleReader = true, AllowSynchronousContinuations = false })
            : Channel.CreateBounded<MailboxWorkItem>(
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
        _pendingDeactivationReason = reason;
        var item = new MailboxWorkItem();
        item.InitFireAndForget(RunPendingDeactivationAsync);
        _queue.Writer.TryWrite(item);

        _ = _processingLoop.ContinueWith(
            _ => _onDeactivated?.Invoke() ?? Task.CompletedTask,
            TaskScheduler.Default).Unwrap();
    }

    // Instance method reference used by Deactivate() to avoid an allocating lambda closure.
    private ValueTask RunPendingDeactivationAsync() => RunDeactivationAsync(_pendingDeactivationReason);

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
            try
            {
                await PostAsync(() => RunDeactivationAsync(DeactivationReason.ShuttingDown)).ConfigureAwait(false);
            }
            catch
            {
                // Deactivation errors are already logged inside RunDeactivationAsync.
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
    public ValueTask PostAsync(Func<ValueTask> workItem)
    {
        if (_isReentrant)
        {
            _cts.Token.ThrowIfCancellationRequested();
            Interlocked.Exchange(ref _lastAccessedTicks, DateTimeOffset.UtcNow.UtcTicks);
            return workItem();
        }

        return PostCoreAsync(workItem);
    }

    private async ValueTask PostCoreAsync(Func<ValueTask> workItem)
    {
        Interlocked.Exchange(ref _lastAccessedTicks, DateTimeOffset.UtcNow.UtcTicks);
        int depth = Interlocked.Increment(ref _pendingWorkCount);
        _diagnostics.OnMailboxEnqueued(new MailboxEnqueuedEvent(GrainId, depth));

        // Rent from the thread-local pool; fall back to allocation on miss.
        MailboxWorkItem item = t_cachedItem ?? new MailboxWorkItem();
        t_cachedItem = null;
        item.Initialize(workItem, ExecutionContext.Capture());

        if (_mailboxFullMode == MailboxFullMode.RejectWhenFull && _mailboxCapacity > 0)
        {
            // Fail fast when the bounded mailbox is full rather than waiting for space.
            if (!_queue.Writer.TryWrite(item))
            {
                Interlocked.Decrement(ref _pendingWorkCount);
                item.ReturnOnEnqueueFailure();
                throw new MailboxFullException(GrainId, _mailboxCapacity);
            }
        }
        else
        {
            try
            {
                await _queue.Writer.WriteAsync(item, _cts.Token).ConfigureAwait(false);
            }
            catch
            {
                item.ReturnOnEnqueueFailure();
                throw;
            }
        }

        // Await completion signal from RunLoopAsync. Pool return happens in ExecuteAsync's finally.
        await item.WaitAsync().ConfigureAwait(false);
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
        IGrainBehavior behavior = await GrainScopeBinder.BindAndResolveAsync(sp, this, ct).ConfigureAwait(false);
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
        await foreach (MailboxWorkItem item in _queue.Reader.ReadAllAsync(ct).ConfigureAwait(false))
        {
            Interlocked.Decrement(ref _pendingWorkCount);
            Interlocked.Exchange(ref _workItemStartedAt, Stopwatch.GetTimestamp());
            try
            {
                await item.ExecuteAsync().ConfigureAwait(false);
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

    // -----------------------------------------------------------------------

    /// <summary>
    ///     Pooled per-call work item for the mailbox.
    ///     Replaces TaskCompletionSource + lambda closure with a reusable object and static callback.
    ///     The caller awaits <see cref="WaitAsync"/>; the runner calls <see cref="ExecuteAsync"/>.
    ///
    ///     Pool-return safety: ManualResetValueTaskSourceCore invokes registered continuations
    ///     synchronously inside SetResult/SetException, which means GetResult() may run before
    ///     ExecuteAsync's finally block.  Returning the item to the pool from GetResult() would
    ///     therefore race with the finally block clearing WorkItem/Context on a re-rented item.
    ///     To avoid this, pool return happens in ExecuteAsync's finally (after SetResult returns)
    ///     for the normal execution path, and in <see cref="ReturnOnEnqueueFailure"/> for the
    ///     path where enqueue failed before the item reached the consumer.
    /// </summary>
    private sealed class MailboxWorkItem : IValueTaskSource
    {
        private ManualResetValueTaskSourceCore<bool> _core;

        internal Func<ValueTask>? WorkItem;
        internal ExecutionContext? Context;

        // Bridge slot: ExecutionContext.Run is synchronous, so the static callback stores the
        // resulting ValueTask here and we await it after Run() returns.
        internal ValueTask PendingVt;

        private bool _isFireAndForget;

        // Static callback: receives 'this' as state — no closure object allocated.
        private static readonly ContextCallback s_runInContext = static s =>
        {
            var self = (MailboxWorkItem)s!;
            self.PendingVt = self.WorkItem!();
        };

        /// <summary>Prepare for an awaited (pooled) work item. Resets the MVTSC for reuse.</summary>
        internal void Initialize(Func<ValueTask> work, ExecutionContext? ctx)
        {
            WorkItem = work;
            Context = ctx;
            _isFireAndForget = false;
            _core.Reset();
        }

        /// <summary>
        ///     Prepare for a fire-and-forget work item (no result signaling, not returned to pool).
        ///     Used by <see cref="Deactivate"/> for the one-shot deactivation work item.
        /// </summary>
        internal void InitFireAndForget(Func<ValueTask> work)
        {
            WorkItem = work;
            Context = null;
            _isFireAndForget = true;
        }

        /// <summary>
        ///     Return to the thread-local pool when enqueue fails before the item reached the consumer.
        ///     Safe to call from <see cref="PostCoreAsync"/> because <see cref="ExecuteAsync"/> never ran.
        /// </summary>
        internal void ReturnOnEnqueueFailure()
        {
            WorkItem = null;
            Context = null;
            if (!_isFireAndForget && t_cachedItem is null)
                t_cachedItem = this;
        }

        /// <summary>Awaitable signal that completes when <see cref="ExecuteAsync"/> finishes.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ValueTask WaitAsync() => new(this, _core.Version);

        /// <summary>
        ///     Runs the work item under its captured <see cref="ExecutionContext"/> and signals
        ///     the awaiter when done.  Called exclusively by <see cref="RunLoopAsync"/>.
        /// </summary>
        internal async ValueTask ExecuteAsync()
        {
            try
            {
                if (Context is null)
                {
                    await WorkItem!().ConfigureAwait(false);
                }
                else
                {
                    ExecutionContext.Run(Context, s_runInContext, this);
                    await PendingVt.ConfigureAwait(false);
                    PendingVt = default;
                }

                if (!_isFireAndForget)
                    _core.SetResult(true);
                    // SetResult may run the caller's continuation synchronously (and thus GetResult),
                    // but pool return is deferred to the finally block below — after SetResult returns.
            }
            catch (Exception ex)
            {
                if (_isFireAndForget)
                    throw; // let RunLoopAsync catch and log it
                _core.SetException(ex);
            }
            finally
            {
                // Clear state and return to pool. Runs after SetResult/SetException completes
                // (including any synchronous continuations), so no concurrent renter sees stale state.
                WorkItem = null;
                Context = null;
                PendingVt = default;
                if (!_isFireAndForget && t_cachedItem is null)
                    t_cachedItem = this;
            }
        }

        // IValueTaskSource — called by the awaiter in PostCoreAsync after WaitAsync().
        // No pool return here: GetResult may fire synchronously inside SetResult, before
        // ExecuteAsync's finally runs, which would cause a re-rented item's state to be corrupted.
        void IValueTaskSource.GetResult(short token) => _core.GetResult(token);

        ValueTaskSourceStatus IValueTaskSource.GetStatus(short token) => _core.GetStatus(token);

        void IValueTaskSource.OnCompleted(
            Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
            => _core.OnCompleted(continuation, state, token, flags);
    }
}
