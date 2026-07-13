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
using Quark.Core.Abstractions.Scheduling;
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
    [ThreadStatic] private static MailboxWorkItem? _tCachedItem;

    private readonly CancellationTokenSource _cts = new();
    private readonly ILogger<GrainActivation> _logger;
    private readonly IActivationScheduler _scheduler;
    private readonly ReentrantSchedulingMode _reentrantMode;
    private readonly IServiceProvider _root;
    private readonly ConcurrentDictionary<Type, object> _memoryBag = new();
    private readonly Lock _timersLock = new();
    private readonly List<IGrainTimer> _timers = [];
    private readonly IQuarkDiagnosticListener _diagnostics;

    // Phase 2: scheduler-owned drain state (replaces per-activation _processingLoop).
    private int _scheduled;             // 0 = not in scheduler ready queue, 1 = scheduled
    private int _running;                // 0 = not draining, 1 = currently draining
    private long _schedulerEnqueueTime; // Stopwatch ticks when activation entered the scheduler ready queue

    // Phase 3: explicit completion signal (replaces _processingLoop awaiting in DisposeAsync).
    private readonly TaskCompletionSource _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private Func<Task>? _onDeactivated;
    private long _lastAccessedTicks;
    private long _deactivationNotBeforeTicks;
    private long _activatedAtTicks; // Stopwatch ticks when MarkActive() is called
    private long _workItemStartedAt; // Stopwatch ticks when a work item starts executing; 0 = idle
    private int _pendingWorkCount; // work items queued but not yet executing
    private int _consecutiveEmptyDrains; // drain passes in a row that processed 0 items while work remained queued
    private volatile GrainActivationStatus _status = GrainActivationStatus.Activating;

    // Stored so Deactivate() can schedule teardown without capturing a lambda closure.
    private DeactivationReason _pendingDeactivationReason = null!;

    // True only for CreateProbe() shells (startup validation, unit-test predicate boundaries).
    // Probes never run scheduler-driven drains — Deactivate() must not schedule real work for them.
    private readonly bool _isProbe;

    // Message-priority mailbox: one FIFO lane per MessagePriority value, indexed by (int)priority.
    // The drain reads the highest-priority non-empty lane first, so a higher-priority message jumps
    // ahead of lower-priority ones already queued for this grain (strict priority, FIFO within a lane).
    // Each lane reuses the same bounded/unbounded Channel machinery the single mailbox used, so a
    // bounded mailbox now bounds each lane independently (per-lane capacity).
    private const int LaneCount = 4; // must equal the number of MessagePriority values
    private readonly Channel<IMailboxWorkItem>[] _lanes;
    private readonly int _mailboxCapacity;
    private readonly MailboxFullMode _mailboxFullMode;

    // Part A (activation-scoped behaviors): true when this grain type opted into IActivationBehavior.
    // A single behavior instance + a single IServiceScope live for the whole activation — constructed
    // once (EnsureActivationBehavior), reused for every call, and disposed on deactivation — instead of
    // the per-call scope+resolution the default IGrainBehavior path pays. Only enabled for non-reentrant
    // grains (LocalGrainCallInvoker.CreateActivationAsync throws for reentrant + IActivationBehavior),
    // so the per-turn call-context refresh in BindActivationBehavior runs on a serial drain and is safe.
    private readonly bool _isActivationScoped;
    private IServiceScope? _activationScope;
    private IServiceProvider? _activationConstructionServices;
    private IGrainBehavior? _activationBehavior;
    private ICallContextSetter? _activationCallContextSetter;

    internal GrainActivation(
        GrainId grainId,
        GrainType grainType,
        bool isReentrant,
        IServiceProvider root,
        ILogger<GrainActivation> logger,
        IActivationScheduler scheduler,
        IQuarkDiagnosticListener? diagnostics = null,
        int mailboxCapacity = 0,
        MailboxFullMode mailboxFullMode = MailboxFullMode.Wait,
        bool isActivationScoped = false)
    {
        GrainId = grainId;
        GrainType = grainType;
        _reentrantMode = isReentrant ? ReentrantSchedulingMode.Immediate : ReentrantSchedulingMode.None;
        _root = root;
        _logger = logger;
        _diagnostics = diagnostics ?? NullDiagnosticListener.Instance;
        _mailboxCapacity = mailboxCapacity;
        _mailboxFullMode = mailboxFullMode;
        _lanes = CreateLanes(mailboxCapacity);
        _scheduler = scheduler;
        _isActivationScoped = isActivationScoped;
        _lastAccessedTicks = DateTimeOffset.UtcNow.UtcTicks;
    }

    // A bounded mailbox caps the work a single grain can queue. We always create bounded channels in
    // FullMode.Wait; RejectWhenFull is enforced in PostAsync via TryWrite so a rejection raises a
    // clean MailboxFullException rather than silently dropping work.
    private static Channel<IMailboxWorkItem> CreateQueue(int capacity)
        => capacity <= 0
            ? Channel.CreateUnbounded<IMailboxWorkItem>(
                new UnboundedChannelOptions { SingleReader = true, AllowSynchronousContinuations = false })
            : Channel.CreateBounded<IMailboxWorkItem>(
                new BoundedChannelOptions(capacity)
                {
                    SingleReader = true,
                    AllowSynchronousContinuations = false,
                    FullMode = BoundedChannelFullMode.Wait,
                });

    // One Channel per priority lane. A bounded mailbox bounds each lane at `capacity` independently;
    // this keeps the proven per-Channel enqueue/backpressure logic unchanged and stops a flood of one
    // priority from consuming another lane's capacity.
    private static Channel<IMailboxWorkItem>[] CreateLanes(int capacity)
    {
        var lanes = new Channel<IMailboxWorkItem>[LaneCount];
        for (int i = 0; i < LaneCount; i++)
            lanes[i] = CreateQueue(capacity);
        return lanes;
    }

    // Reads the next work item, highest-priority lane first (Urgent → Low), FIFO within a lane.
    private bool TryReadNext([System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out IMailboxWorkItem item)
    {
        for (int lane = LaneCount - 1; lane >= 0; lane--)
        {
            if (_lanes[lane].Reader.TryRead(out item))
                return true;
        }

        item = null;
        return false;
    }

    // True if any lane still holds an undispatched work item.
    private bool MailboxHasWork()
    {
        for (int lane = 0; lane < LaneCount; lane++)
        {
            if (_lanes[lane].Reader.TryPeek(out _))
                return true;
        }

        return false;
    }

    // Completes every lane's writer (mailbox shutdown).
    private void CompleteLanes()
    {
        for (int lane = 0; lane < LaneCount; lane++)
            _lanes[lane].Writer.TryComplete();
    }

    // Probe constructor — no processing loop, used only by BehaviorStartupValidator.
    private GrainActivation(GrainId grainId, GrainType grainType, IServiceProvider root)
    {
        GrainId = grainId;
        GrainType = grainType;
        _root = root;
        _logger = NullLogger<GrainActivation>.Instance;
        _diagnostics = NullDiagnosticListener.Instance;
        _lanes = CreateLanes(0);
        _scheduler = SimpleActivationScheduler.Instance;
        _status = GrainActivationStatus.Active;
        _lastAccessedTicks = 0;
        _isProbe = true;
        _completion.TrySetResult(); // probe never deactivates via normal path
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

    /// <summary>
    ///     True when this grain type opted into <c>IActivationBehavior</c> (per-activation lifetime).
    ///     The dispatch path uses <see cref="BindActivationBehavior"/> — one cached instance + scope —
    ///     instead of constructing a fresh behavior in a per-call scope.
    /// </summary>
    internal bool IsActivationScoped => _isActivationScoped;

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
    ///     Number of consecutive drain passes that processed zero items while the mailbox still
    ///     reported pending work (see <see cref="DrainAsync"/>). A rising count without ever
    ///     resetting to 0 indicates a livelock — the scheduler keeps rescheduling this activation
    ///     but it never makes progress. Read by <see cref="StuckGrainDetector" />.
    /// </summary>
    internal int ConsecutiveEmptyDrains => Volatile.Read(ref _consecutiveEmptyDrains);

    // Phase 2: scheduler polls this after a drain to decide whether to reschedule.
    internal bool HasPendingWork
        => MailboxHasWork() || _status == GrainActivationStatus.Deactivating;

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
    {
        return (ManagedActivationMemoryHolder<T>)_memoryBag.GetOrAdd(
            typeof(ManagedKey<T>),
            ValueFactory);

        static object ValueFactory(Type _) => new ManagedActivationMemoryHolder<T>();
    }

    // Discriminator type so managed holders and state holders never share a key.
    private sealed class ManagedKey<T>;

    // Discriminator type so eager holders cannot collide with state or managed holders.
    private sealed class EagerKey<T>;

    // Discriminator type for the single per-activation ChildRegistry entry.
    private sealed class ChildRegistryKey;

    /// <summary>
    ///     Returns or creates the <see cref="EagerActivationMemoryHolder{T}" /> for the given resource type.
    ///     One holder per (activation, T). Automatically disposed after <c>OnDeactivateAsync</c> completes.
    ///     The holder is initialized at activation time, before <c>OnActivateAsync</c>.
    /// </summary>
    public EagerActivationMemoryHolder<T> GetOrCreateEagerHolder<T>() where T : class
    {
        return (EagerActivationMemoryHolder<T>)_memoryBag.GetOrAdd(
            typeof(EagerKey<T>),
            ValueFactory);

        static object ValueFactory(Type _) => new EagerActivationMemoryHolder<T>();
    }

    /// <summary>
    ///     Returns or creates the <see cref="ChildRegistry" /> for this activation.
    ///     Used by <see cref="ActivationChildrenAccessor" /> to project the child set into the per-call scope.
    /// </summary>
    internal ChildRegistry GetOrCreateChildRegistry()
        => (ChildRegistry)_memoryBag.GetOrAdd(typeof(ChildRegistryKey), static _ => new ChildRegistry());

    /// <summary>
    ///     Gets or creates an activation-scoped singleton of type <typeparamref name="T" />.
    ///     The factory is invoked at most once per activation lifetime.
    ///     Use for services that must be shared across all per-call scopes of the same activation.
    /// </summary>
    public T GetOrCreate<T>(Func<T> factory) where T : class
    {
        return (T)_memoryBag.GetOrAdd(typeof(T), ValueFactory);

        object ValueFactory(Type _) => factory();
    }

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
        TimeProvider timeProvider = options.TimeProvider ?? _root.GetService<TimeProvider>() ?? TimeProvider.System;

        var timer = new GrainTimer<TState>(Instrumented, state, options, PostAsync, timeProvider);
        lock (_timersLock)
        {
            _timers.Add(timer);
        }

        return timer;

        async Task Instrumented(TState s, CancellationToken ct)
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
        }
    }

    /// <summary>
    ///     Requests deactivation.
    ///     Phase 3: sets Deactivating status and schedules a drain pass instead of writing a
    ///     fire-and-forget item to the bounded mailbox (which could be silently dropped when full).
    ///     DrainAsync detects Deactivating status after pre-queued work drains and runs teardown inline.
    /// </summary>
    public void Deactivate(DeactivationReason reason)
    {
        if (_status != GrainActivationStatus.Active && _status != GrainActivationStatus.Activating)
        {
            return;
        }

        _status = GrainActivationStatus.Deactivating;
        _pendingDeactivationReason = reason;

        // Probes are inert shells with no real mailbox/lifecycle to drain — leave status at
        // Deactivating and stop, matching the pre-scheduler behavior probe callers rely on.
        if (_isProbe)
        {
            return;
        }

        // Schedule a drain pass so the scheduler sees the Deactivating status
        // and runs teardown after any already-queued work drains (spec invariant 7).
        // ScheduleAsync is synchronously completed in both implementations.
        ValueTask vt = _scheduler.ScheduleAsync(this);
        if (!vt.IsCompletedSuccessfully)
        {
            vt.AsTask().ContinueWith(
                ContinuationFunction,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }

    private static void ContinuationFunction(Task _)
    {
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
            try
            {
                // Phase 3: post deactivation as a mailbox work item so it executes in FIFO order
                // after any items already queued. PostAsync awaits the work item's completion,
                // so RunDeactivationAsync will have called _completion.TrySetResult() before
                // control returns here.
                await PostAsync(() => RunDeactivationAsync(DeactivationReason.ShuttingDown))
                    .ConfigureAwait(false);
            }
            catch
            {
                // The mailbox/scheduler could not accept the deactivation work item — e.g. a
                // bounded mailbox still full of pre-dispose items (MailboxFullException), or the
                // scheduler's own ready-queue already completed by a racing shutdown
                // (ChannelClosedException). Either way RunDeactivationAsync never ran and never
                // will via the scheduler, so _completion would wait forever below. Drain whatever
                // is left and run teardown directly instead of relying on a scheduler that can no
                // longer service this activation.
                await DrainDirectlyAndDeactivateAsync().ConfigureAwait(false);
            }
        }

        await _cts.CancelAsync().ConfigureAwait(false);
        CompleteLanes();

        // Phase 3: await the explicit completion signal set by RunDeactivationAsync, replacing
        // the old `await _processingLoop` pattern. Handles the case where Deactivate() was
        // called externally before DisposeAsync, so deactivation is scheduler-driven not inline.
        try
        {
            await _completion.Task.ConfigureAwait(false);
        }
        catch
        {
            // Swallow — errors were already logged inside RunDeactivationAsync.
        }

        _cts.Dispose();
    }

    /// <summary>
    ///     Fallback teardown used when posting the shutdown deactivation through the normal
    ///     mailbox/scheduler path fails. Waits for any drain already in flight to finish (via the
    ///     same <see cref="TryBeginDrain"/> claim the scheduler uses, so this never runs
    ///     concurrently with a scheduler-driven drain), then drains remaining items and lets
    ///     <see cref="DrainAsync"/>'s own Deactivating-status check run teardown, exactly as it
    ///     would have if the scheduler had serviced the post.
    /// </summary>
    private async Task DrainDirectlyAndDeactivateAsync()
    {
        _pendingDeactivationReason = DeactivationReason.ShuttingDown;
        _status = GrainActivationStatus.Deactivating;

        var spin = new SpinWait();
        while (!TryBeginDrain())
        {
            if (_status == GrainActivationStatus.Inactive)
            {
                return; // A concurrent drain already reached Deactivating and tore this down.
            }
            spin.SpinOnce();
        }

        ActivationDrainResult result = await DrainAsync(int.MaxValue, CancellationToken.None).ConfigureAwait(false);
        CompleteDrain(result);
    }

    // -----------------------------------------------------------------------
    // Phase 2: narrow drain surface exposed to IActivationScheduler implementations.
    // The scheduler calls these to execute mailbox items; GrainActivation retains mailbox ownership.
    // -----------------------------------------------------------------------

    /// <summary>
    ///     Atomically marks this activation as "scheduled" in the scheduler's ready queue.
    ///     Returns <see langword="true"/> if the CAS succeeded (caller should enqueue the activation);
    ///     <see langword="false"/> if it was already scheduled (no duplicate enqueue needed).
    /// </summary>
    internal bool TryMarkScheduled()
        => Interlocked.CompareExchange(ref _scheduled, 1, 0) == 0;

    /// <summary>
    ///     Attempts to begin a drain: atomically acquires the drain lock.
    ///     Returns <see langword="false"/> if a drain is already running for this activation — this
    ///     should not happen in practice since <c>_scheduled</c> stays set for the whole drain (see
    ///     <see cref="CompleteDrain"/>), preventing a second ready-queue entry from ever being created
    ///     while a drain is in flight; it is handled defensively as a no-op.
    ///     Deliberately does NOT clear <c>_scheduled</c> here (on success or failure) — the flag stays
    ///     claimed for the entire drain so concurrent <see cref="PostAsync"/> calls do not create a
    ///     redundant ready-queue entry for an activation that is already being drained. That matters
    ///     under <see cref="SchedulerOverloadMode.RejectWhenFull"/>: a spurious duplicate entry would
    ///     consume a bounded ready-queue slot and could cause an unrelated activation's legitimate
    ///     schedule request to be falsely rejected. <see cref="CompleteDrain"/> releases the claim.
    /// </summary>
    internal bool TryBeginDrain()
        => Interlocked.CompareExchange(ref _running, 1, 0) == 0;

    /// <summary>
    ///     Drains up to <paramref name="maxItems"/> work items from the mailbox in FIFO order.
    ///     After exhausting available items, checks for a pending deactivation and runs teardown inline
    ///     (Phase 3: reliable deactivation — not written to the bounded mailbox, detected by status check).
    /// </summary>
    internal async ValueTask<ActivationDrainResult> DrainAsync(int maxItems, CancellationToken ct)
    {
        int count = 0;

        while (!ct.IsCancellationRequested && count < maxItems
               && TryReadNext(out IMailboxWorkItem? item))
        {
            Interlocked.Decrement(ref _pendingWorkCount);
            Interlocked.Exchange(ref _workItemStartedAt, Stopwatch.GetTimestamp());
            try
            {
                await item.ExecuteAsync().ConfigureAwait(false);
            }
            catch (Exception e)
            {
                // Fault isolation (invariant 9): one bad item must not stop later queued work.
                _logger.LogError(e, "Error executing grain method on {GrainId}", GrainId);
            }
            finally
            {
                Interlocked.Exchange(ref _workItemStartedAt, 0);
            }
            count++;
        }

        bool hasMore = MailboxHasWork();

        // Livelock detection: a drain pass that processes nothing while work remains queued means
        // this activation is being rescheduled without ever making progress (e.g. its own
        // cancellation token fired mid-drain, so TryRead keeps short-circuiting while TryPeek —
        // which ignores cancellation — keeps reporting the stranded items as "more work").
        if (count == 0 && hasMore)
        {
            Interlocked.Increment(ref _consecutiveEmptyDrains);
        }
        else
        {
            Interlocked.Exchange(ref _consecutiveEmptyDrains, 0);
        }

        // Phase 3: deactivation reliability. Deactivate() no longer writes to the bounded mailbox;
        // instead DrainAsync detects Deactivating status here, after all pre-queued work drains
        // (invariants 5 and 7). This is safe to run inline because _running=1 guarantees exclusivity.
        if (_status == GrainActivationStatus.Deactivating && !hasMore)
        {
            await RunDeactivationAsync(_pendingDeactivationReason).ConfigureAwait(false);
            return new ActivationDrainResult(false, true, count);
        }

        return new ActivationDrainResult(hasMore, _status == GrainActivationStatus.Inactive, count);
    }

    /// <summary>
    ///     Clears the drain lock and the scheduled claim after a drain pass.
    ///     Returns <see langword="true"/> if more work has appeared in the mailbox since the drain
    ///     ended (scheduler should reschedule); <see langword="false"/> if the mailbox is still empty.
    ///     Clears <c>_scheduled</c> BEFORE peeking the channel — a writer that arrives after the clear
    ///     wins <see cref="TryMarkScheduled"/> and self-enqueues; a writer that arrived before the clear
    ///     already wrote its item, so the peek below observes it. Either order picks the work up exactly
    ///     once, so there is no lost wakeup.
    /// </summary>
    internal bool CompleteDrain(ActivationDrainResult result)
    {
        Interlocked.Exchange(ref _running, 0);
        Interlocked.Exchange(ref _scheduled, 0);
        // Full memory barrier via Interlocked ensures items written by concurrent PostAsync calls
        // are visible before we peek the channel.
        return MailboxHasWork() || _status == GrainActivationStatus.Deactivating;
    }

    /// <summary>
    ///     Runs <see cref="DrainAsync"/> and immediately follows it with <see cref="CompleteDrain"/>,
    ///     returning the drain result plus whether the scheduler should reschedule this activation
    ///     afterward. Callers must have already confirmed <see cref="TryBeginDrain"/> succeeded.
    ///     Both <see cref="SimpleActivationScheduler"/> and <see cref="ActivationScheduler"/> route
    ///     their drain-then-complete step through this single method, since the DrainAsync/CompleteDrain
    ///     pairing — and the "reschedule if more work arrived" decision <see cref="CompleteDrain"/>'s
    ///     return value feeds — is the part of the drain protocol most at risk of silently drifting
    ///     between the two independent scheduler implementations.
    /// </summary>
    internal async ValueTask<(ActivationDrainResult Result, bool NeedsReschedule)> DrainAndCompleteAsync(
        int maxItems, CancellationToken ct)
    {
        ActivationDrainResult result = await DrainAsync(maxItems, ct).ConfigureAwait(false);
        bool needsReschedule = CompleteDrain(result);
        return (result, needsReschedule);
    }

    /// <summary>
    ///     Resets the scheduled flag to 0. Called by the scheduler when it cannot enqueue the
    ///     activation into the ready queue (e.g. <see cref="SchedulerOverloadMode.RejectWhenFull"/>).
    ///     Allows a future <see cref="TryMarkScheduled"/> to succeed.
    /// </summary>
    internal void AbortSchedule() => Interlocked.Exchange(ref _scheduled, 0);

    /// <summary>Records the Stopwatch timestamp at which the activation entered the scheduler ready queue.</summary>
    internal void SetSchedulerEnqueueTime()
        => Interlocked.Exchange(ref _schedulerEnqueueTime, Stopwatch.GetTimestamp());

    /// <summary>Returns and clears the scheduler-enqueue timestamp (0 if never set).</summary>
    internal long TakeSchedulerEnqueueTime()
        => Interlocked.Exchange(ref _schedulerEnqueueTime, 0);

    // -----------------------------------------------------------------------

    /// <summary>
    ///     Posts a unit of work to this grain's sequential mailbox and awaits its completion.
    ///     <see cref="ReentrantSchedulingMode.Immediate"/> activations bypass the queue and execute
    ///     inline — see that enum value's documentation for the scheduler-invisibility tradeoff.
    ///     Phase 3: rejects new work when the grain is already Deactivating or Inactive.
    /// </summary>
    public ValueTask PostAsync(Func<ValueTask> workItem) => PostAsync(workItem, MessagePriority.Normal);

    /// <summary>
    ///     Posts a unit of work to this grain's mailbox at the given <paramref name="priority"/> and
    ///     awaits its completion. A higher-priority post is drained ahead of lower-priority work already
    ///     queued for this grain, but never interrupts the turn currently executing (non-preemptive);
    ///     posts at the same priority preserve arrival order (FIFO). Reentrant activations run inline and
    ///     ignore priority — see <see cref="ReentrantSchedulingMode.Immediate"/>.
    /// </summary>
    public ValueTask PostAsync(Func<ValueTask> workItem, MessagePriority priority)
    {
        if (_reentrantMode == ReentrantSchedulingMode.Immediate)
        {
            _cts.Token.ThrowIfCancellationRequested();
            Interlocked.Exchange(ref _lastAccessedTicks, DateTimeOffset.UtcNow.UtcTicks);
            return workItem();
        }

        // Phase 3: explicit rejection to satisfy invariant 6 (terminal deactivation).
        GrainActivationStatus status = _status;
        if (status is GrainActivationStatus.Deactivating or GrainActivationStatus.Inactive)
        {
            return ValueTask.FromException(
                new OperationCanceledException($"Grain {GrainId} is {status.ToString().ToLowerInvariant()}."));
        }

        return PostCoreAsync(workItem, priority);
    }

    /// <summary>
    ///     State-passing overload of <see cref="PostAsync(Func{ValueTask})" /> that avoids per-call
    ///     closure allocation.  <paramref name="workItem" /> must be a static delegate (method group
    ///     or <c>static</c> lambda) — no implicit captures allowed.  All per-call data travels
    ///     through <paramref name="state" />.
    /// </summary>
    public ValueTask PostAsync<TState>(TState state, Func<TState, ValueTask> workItem)
    {
        if (_reentrantMode == ReentrantSchedulingMode.Immediate)
        {
            _cts.Token.ThrowIfCancellationRequested();
            Interlocked.Exchange(ref _lastAccessedTicks, DateTimeOffset.UtcNow.UtcTicks);
            return workItem(state);
        }

        GrainActivationStatus status = _status;
        if (status is GrainActivationStatus.Deactivating or GrainActivationStatus.Inactive)
        {
            return ValueTask.FromException(
                new OperationCanceledException($"Grain {GrainId} is {status.ToString().ToLowerInvariant()}."));
        }

        return PostCoreAsync(state, workItem, MessagePriority.Normal);
    }

    /// <summary>
    ///     State-passing, value-returning overload of <see cref="PostAsync(Func{ValueTask})" />.
    ///     <paramref name="workItem" /> must be a static delegate; the result is returned directly,
    ///     eliminating the need for a captured mutable variable in the caller.
    /// </summary>
    public ValueTask<TResult> PostAsync<TState, TResult>(TState state, Func<TState, ValueTask<TResult>> workItem)
    {
        if (_reentrantMode == ReentrantSchedulingMode.Immediate)
        {
            _cts.Token.ThrowIfCancellationRequested();
            Interlocked.Exchange(ref _lastAccessedTicks, DateTimeOffset.UtcNow.UtcTicks);
            return workItem(state);
        }

        GrainActivationStatus status = _status;
        if (status is GrainActivationStatus.Deactivating or GrainActivationStatus.Inactive)
        {
            return ValueTask.FromException<TResult>(
                new OperationCanceledException($"Grain {GrainId} is {status.ToString().ToLowerInvariant()}."));
        }

        return PostCoreAsync(state, workItem, MessagePriority.Normal);
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder))]
    private async ValueTask PostCoreAsync<TState>(TState state, Func<TState, ValueTask> workItem, MessagePriority priority)
    {
        Interlocked.Exchange(ref _lastAccessedTicks, DateTimeOffset.UtcNow.UtcTicks);
        int depth = Interlocked.Increment(ref _pendingWorkCount);
        _diagnostics.OnMailboxEnqueued(new MailboxEnqueuedEvent(GrainId, depth));

        MailboxWorkItem<TState> item = MailboxWorkItem<TState>.Rent();
        item.Initialize(state, workItem, ExecutionContext.Capture());

        if (_mailboxFullMode == MailboxFullMode.RejectWhenFull && _mailboxCapacity > 0)
        {
            if (!_lanes[(int)priority].Writer.TryWrite(item))
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
                await _lanes[(int)priority].Writer.WriteAsync(item, _cts.Token).ConfigureAwait(false);
            }
            catch
            {
                item.ReturnOnEnqueueFailure();
                throw;
            }
        }

        await _scheduler.ScheduleAsync(this, _cts.Token).ConfigureAwait(false);
        await item.WaitAsync().ConfigureAwait(false);
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    private async ValueTask<TResult> PostCoreAsync<TState, TResult>(TState state, Func<TState, ValueTask<TResult>> workItem, MessagePriority priority)
    {
        Interlocked.Exchange(ref _lastAccessedTicks, DateTimeOffset.UtcNow.UtcTicks);
        int depth = Interlocked.Increment(ref _pendingWorkCount);
        _diagnostics.OnMailboxEnqueued(new MailboxEnqueuedEvent(GrainId, depth));

        MailboxWorkItem<TState, TResult> item = MailboxWorkItem<TState, TResult>.Rent();
        item.Initialize(state, workItem, ExecutionContext.Capture());

        if (_mailboxFullMode == MailboxFullMode.RejectWhenFull && _mailboxCapacity > 0)
        {
            if (!_lanes[(int)priority].Writer.TryWrite(item))
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
                await _lanes[(int)priority].Writer.WriteAsync(item, _cts.Token).ConfigureAwait(false);
            }
            catch
            {
                item.ReturnOnEnqueueFailure();
                throw;
            }
        }

        await _scheduler.ScheduleAsync(this, _cts.Token).ConfigureAwait(false);
        return await item.WaitAsync().ConfigureAwait(false);
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder))]
    private async ValueTask PostCoreAsync(Func<ValueTask> workItem, MessagePriority priority)
    {
        Interlocked.Exchange(ref _lastAccessedTicks, DateTimeOffset.UtcNow.UtcTicks);
        int depth = Interlocked.Increment(ref _pendingWorkCount);
        _diagnostics.OnMailboxEnqueued(new MailboxEnqueuedEvent(GrainId, depth));

        // Rent from the thread-local pool; fall back to allocation on miss.
        MailboxWorkItem item = _tCachedItem ?? new MailboxWorkItem();
        _tCachedItem = null;
        item.Initialize(workItem, ExecutionContext.Capture());

        if (_mailboxFullMode == MailboxFullMode.RejectWhenFull && _mailboxCapacity > 0)
        {
            // Fail fast when the bounded mailbox is full rather than waiting for space.
            if (!_lanes[(int)priority].Writer.TryWrite(item))
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
                await _lanes[(int)priority].Writer.WriteAsync(item, _cts.Token).ConfigureAwait(false);
            }
            catch
            {
                item.ReturnOnEnqueueFailure();
                throw;
            }
        }

        // Phase 1: notify the scheduler that this activation has work.
        // The scheduler adds the activation to its ready queue if not already scheduled.
        await _scheduler.ScheduleAsync(this, _cts.Token).ConfigureAwait(false);

        // Await completion signal from DrainAsync / ExecuteAsync. Pool return happens in
        // ExecuteAsync's finally block.
        await item.WaitAsync().ConfigureAwait(false);
    }

    private async ValueTask RunDeactivationAsync(DeactivationReason reason)
    {
        _diagnostics.OnGrainDeactivating(new GrainDeactivatingEvent(GrainId, reason));
        DisposeTimers();
        try
        {
            Task Hook(IActivationLifecycle lifecycle) => lifecycle.OnDeactivateAsync(reason, CancellationToken.None);

            await RunLifecycleHookAsync(Hook)
                .ConfigureAwait(false);
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Error running OnDeactivateAsync lifecycle hook for {GrainId}", GrainId);
        }

        // After the parent's own OnDeactivateAsync completes (so it had a chance to Detach/flush),
        // fire-and-forget cascade to all Cascade-mode children for intentional terminations.
        if (reason.CascadesToChildren)
        {
            CascadeToChildren();
        }

        await DisposeManagedHoldersAsync().ConfigureAwait(false);
        await DisposeActivationScopeAsync().ConfigureAwait(false);
        CompleteLanes();

        long activatedAt = Interlocked.Read(ref _activatedAtTicks);
        TimeSpan lifetime = activatedAt > 0 ? Stopwatch.GetElapsedTime(activatedAt) : TimeSpan.Zero;
        _diagnostics.OnGrainDeactivated(new GrainDeactivatedEvent(GrainId, reason, lifetime));
        QuarkInstruments.GrainActivationsDeactivated.Add(1,
            new KeyValuePair<string, object?>("grain_type", GrainType.Value));
        QuarkInstruments.ActiveGrainActivations.Add(-1,
            new KeyValuePair<string, object?>("grain_type", GrainType.Value));

        // Phase 3: run table removal before setting Inactive so that any caller waiting
        // for ActivationStatus == Inactive sees a fully-cleaned-up table entry.
        if (_onDeactivated is not null)
        {
            try
            {
                await _onDeactivated().ConfigureAwait(false);
            }
            catch
            {
                // Swallow — table removal is best-effort.
            }
        }

        _status = GrainActivationStatus.Inactive;

        // Phase 3: signal DisposeAsync (and any other awaiters) that deactivation is complete.
        _completion.TrySetResult();
    }

    private void CascadeToChildren()
    {
        if (!_memoryBag.TryGetValue(typeof(ChildRegistryKey), out object? obj) || obj is not ChildRegistry registry)
            return;

        IActivationTerminator? terminator = _root.GetService<IActivationTerminator>();
        if (terminator is null)
            return;

        IReadOnlyCollection<GrainId> children = registry.Snapshot(ChildTerminationMode.Cascade);
        foreach (GrainId child in children)
        {
            try
            {
                terminator.Terminate(child, DeactivationReason.ParentTerminated);
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "Error cascading termination to child {ChildId} from {GrainId}", child, GrainId);
            }
        }
    }

    // Disposes the activation-lifetime scope (and everything resolved into it, including the cached
    // behavior) after OnDeactivateAsync + managed-holder cleanup have run. No-op for the per-call model.
    private async ValueTask DisposeActivationScopeAsync()
    {
        IServiceScope? scope = _activationScope;
        if (scope is null)
        {
            return;
        }

        _activationBehavior = null;
        _activationCallContextSetter = null;
        _activationConstructionServices = null;
        _activationScope = null;

        try
        {
            if (scope is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync().ConfigureAwait(false);
            }
            else
            {
                scope.Dispose();
            }
        }
        catch (Exception e)
        {
            _logger.LogWarning(e, "Error disposing activation scope for {GrainId}", GrainId);
        }
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
        // Activation-scoped: run the hook on the single cached instance so a stateful behavior's
        // OnDeactivateAsync observes the activation's final field state.
        if (_isActivationScoped)
        {
            if (EnsureActivationBehavior() is IActivationLifecycle lifecycle)
            {
                await hook(lifecycle).ConfigureAwait(false);
            }

            return;
        }

        (IServiceScope scope, IServiceProvider constructionServices) = GrainScopeBinder.CreateCallScope(_root, this);
        using (scope)
        {
            IGrainBehavior behavior = GrainScopeBinder.BindAndResolve(scope.ServiceProvider, constructionServices, this);
            if (behavior is IActivationLifecycle lifecycle)
            {
                await hook(lifecycle).ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    ///     Constructs the single per-activation behavior instance on first use (creating the
    ///     activation-lifetime <see cref="IServiceScope"/>), binding the constant-for-the-activation
    ///     shell reference and grain id once. Idempotent; only valid on <c>IActivationBehavior</c> grains.
    ///     Called only on the serial activation/drain/deactivation path — not thread-safe by design,
    ///     which is why reentrant grains are excluded (they would call it under concurrent turns).
    /// </summary>
    private IGrainBehavior EnsureActivationBehavior()
    {
        if (_activationBehavior is not null)
        {
            return _activationBehavior;
        }

        (IServiceScope scope, IServiceProvider constructionServices) = GrainScopeBinder.CreateCallScope(_root, this);
        _activationScope = scope;
        _activationConstructionServices = constructionServices;

        ((ActivationShellAccessor)scope.ServiceProvider.GetRequiredService<IActivationShellAccessor>()).Shell = this;
        _activationCallContextSetter = scope.ServiceProvider.GetRequiredService<ICallContextSetter>();
        _activationCallContextSetter.Set(GrainId); // the grain's own id — constant for the activation

        _activationBehavior = scope.ServiceProvider
            .GetRequiredService<IBehaviorResolver>()
            .Resolve(GrainType, constructionServices);
        return _activationBehavior;
    }

    /// <summary>
    ///     Returns the cached per-activation behavior instance for a call, refreshing the per-call
    ///     idempotency key. Safe to mutate the shared call context here because activation-scoped grains
    ///     are non-reentrant, so their turns drain serially. Invoked from the dispatch fast path.
    /// </summary>
    internal IGrainBehavior BindActivationBehavior()
    {
        IGrainBehavior behavior = EnsureActivationBehavior();
        _activationCallContextSetter!.SetIdempotencyKey(QuarkRequestContext.IdempotencyKey);
        return behavior;
    }

    // Runs the full activation sequence:
    // 1. Bind shell accessor + call context, using the Quark-only scope for opted-in grain types
    //    (see GrainScopeBinder.CreateCallScope) or the flat scope otherwise.
    // 2. Resolve behavior (ctor fires; any IEagerActivationMemory<T>.Load() calls register factories).
    // 3. Initialize all eager holders with the construction provider BEFORE OnActivateAsync.
    // 4. Call OnActivateAsync if the behavior implements IActivationLifecycle.
    internal async Task RunActivationAsync(CancellationToken ct)
    {
        // Activation-scoped: construct the single instance + scope now (first use) and keep them for the
        // activation's lifetime. Eager init + OnActivateAsync run against that same cached instance.
        if (_isActivationScoped)
        {
            IGrainBehavior behavior = EnsureActivationBehavior();
            await RunEagerInitAsync(_activationConstructionServices!, ct).ConfigureAwait(false);
            if (behavior is IActivationLifecycle lifecycle)
            {
                await lifecycle.OnActivateAsync(ct).ConfigureAwait(false);
            }

            return;
        }

        (IServiceScope scope, IServiceProvider constructionServices) = GrainScopeBinder.CreateCallScope(_root, this);
        using (scope)
        {
            IGrainBehavior behavior = GrainScopeBinder.BindAndResolve(scope.ServiceProvider, constructionServices, this);
            await RunEagerInitAsync(constructionServices, ct).ConfigureAwait(false);
            if (behavior is IActivationLifecycle lifecycle)
            {
                await lifecycle.OnActivateAsync(ct).ConfigureAwait(false);
            }
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

    // -----------------------------------------------------------------------

    private interface IMailboxWorkItem
    {
        ValueTask ExecuteAsync();
    }

    /// <summary>
    ///     Pooled per-call work item for the mailbox.
    ///     Replaces TaskCompletionSource + lambda closure with a reusable object and static callback.
    ///     The caller awaits <see cref="WaitAsync"/>; the runner calls <see cref="ExecuteAsync"/>.
    ///
    ///     Pool-return safety: RunContinuationsAsynchronously only guarantees the awaiter's
    ///     continuation (and its GetResult call) doesn't run *inline* inside SetResult — it can
    ///     still run concurrently, on another thread, with the rest of ExecuteAsync's finally
    ///     block. Pooling from either side alone risks handing the instance back out to a new
    ///     Initialize() call while the other side is still clearing/reading fields. <see cref="ReleaseRef"/>
    ///     uses an interlocked refcount so the actual field-clear + pool-return happens exactly
    ///     once, only after both ExecuteAsync's finally and GetResult have completed.
    /// </summary>
    private sealed class MailboxWorkItem : IValueTaskSource, IMailboxWorkItem
    {
        private ManualResetValueTaskSourceCore<bool> _core;

        private Func<ValueTask>? _workItem;
        private ExecutionContext? _context;

        // Bridge slot: ExecutionContext.Run is synchronous, so the static callback stores the
        // resulting ValueTask here and we await it after Run() returns.
        private ValueTask _pendingVt;

        private bool _isFireAndForget;

        // Decremented once by ExecuteAsync's finally and once by GetResult; whichever call
        // brings this to zero performs the field-clear + pool-return. See class doc above.
        private int _refCount;

        // Static callback: receives 'this' as state — no closure object allocated.
        private static readonly ContextCallback SRunInContext = static s =>
        {
            var self = (MailboxWorkItem)s!;
            self._pendingVt = self._workItem!();
        };

        /// <summary>Prepare for an awaited (pooled) work item. Resets the MVTSC for reuse.</summary>
        internal void Initialize(Func<ValueTask> work, ExecutionContext? ctx)
        {
            _workItem = work;
            _context = ctx;
            _isFireAndForget = false;
            _refCount = 2;
            _core.Reset();
            // Force the caller's continuation (PostCoreAsync's `await item.WaitAsync()`) onto the
            // thread pool instead of running it inline inside SetResult below. Without this, a
            // caller whose continuation chain reaches back into disposing the very scheduler
            // driving this drain (e.g. a grain's own shutdown deactivation) deadlocks: the worker
            // thread is still nested inside ExecuteAsync when the scheduler's DisposeAsync tries to
            // Task.WhenAll the same worker's Task, which can only complete once this call returns.
            _core.RunContinuationsAsynchronously = true;
        }

        /// <summary>
        ///     Return to the thread-local pool when enqueue fails before the item reached the consumer.
        ///     Safe to call from <see cref="PostCoreAsync"/> because <see cref="ExecuteAsync"/> never ran.
        /// </summary>
        internal void ReturnOnEnqueueFailure()
        {
            _workItem = null;
            _context = null;
            if (!_isFireAndForget && _tCachedItem is null)
            {
                _tCachedItem = this;
            }
        }

        /// <summary>Awaitable signal that completes when <see cref="ExecuteAsync"/> finishes.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ValueTask WaitAsync() => new(this, _core.Version);

        /// <summary>
        ///     Runs the work item under its captured <see cref="ExecutionContext"/> and signals
        ///     the awaiter when done.  Called exclusively by <see cref="DrainAsync"/>.
        /// </summary>
        public async ValueTask ExecuteAsync()
        {
            try
            {
                if (_context is null)
                {
                    await _workItem!().ConfigureAwait(false);
                }
                else
                {
                    ExecutionContext.Run(_context, SRunInContext, this);
                    await _pendingVt.ConfigureAwait(false);
                    _pendingVt = default;
                }

                if (!_isFireAndForget)
                {
                    _core.SetResult(true);
                }
            }
            catch (Exception ex)
            {
                if (_isFireAndForget)
                {
                    throw; // let DrainAsync catch and log it
                }

                _core.SetException(ex);
            }
            finally
            {
                _pendingVt = default;
                if (_isFireAndForget)
                {
                    // No GetResult caller will ever run for a fire-and-forget item, so return it here.
                    _workItem = null;
                    _context = null;
                    if (_tCachedItem is null)
                    {
                        _tCachedItem = this;
                    }
                }
                else
                {
                    ReleaseRef();
                }
            }
        }

        // IValueTaskSource — called by the awaiter in PostCoreAsync after WaitAsync(). See the
        // class doc: cleanup + pool-return happens via ReleaseRef, once both this and
        // ExecuteAsync's finally have run.
        void IValueTaskSource.GetResult(short token)
        {
            try
            {
                _core.GetResult(token);
            }
            finally
            {
                ReleaseRef();
            }
        }

        /// <summary>
        ///     Decrements the two-phase completion refcount; the call that brings it to zero clears
        ///     the fields and returns the instance to the thread-local pool. See class doc for why
        ///     this can't safely happen unconditionally in either ExecuteAsync's finally or GetResult alone.
        /// </summary>
        private void ReleaseRef()
        {
            if (Interlocked.Decrement(ref _refCount) != 0)
            {
                return;
            }

            _workItem = null;
            _context = null;
            if (_tCachedItem is null)
            {
                _tCachedItem = this;
            }
        }

        ValueTaskSourceStatus IValueTaskSource.GetStatus(short token) => _core.GetStatus(token);

        void IValueTaskSource.OnCompleted(
            Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
            => _core.OnCompleted(continuation, state, token, flags);
    }

    /// <summary>
    ///     Generic pooled work item for the mailbox — void-result variant.
    ///     Avoids the per-call closure+delegate allocation of the Func&lt;ValueTask&gt; overload.
    ///     <typeparamref name="TState"/> carries all per-call data; the delegate is a cached static.
    /// </summary>
    private sealed class MailboxWorkItem<TState> : IValueTaskSource, IMailboxWorkItem
    {
        [ThreadStatic] private static MailboxWorkItem<TState>? _tCachedItem;

        private ManualResetValueTaskSourceCore<bool> _core;
        private TState _state = default!;
        private Func<TState, ValueTask>? _work;
        private ExecutionContext? _context;
        private ValueTask _pendingVt;

        // Decremented once by ExecuteAsync's finally and once by GetResult; whichever call
        // brings this to zero performs the field-clear + pool-return — see the
        // non-generic MailboxWorkItem's class doc for why this can't happen unconditionally
        // in either place alone (RunContinuationsAsynchronously doesn't order the two).
        private int _refCount;

        private static readonly ContextCallback SRunInContext = static s =>
        {
            var self = (MailboxWorkItem<TState>)s!;
            self._pendingVt = self._work!(self._state);
        };

        internal static MailboxWorkItem<TState> Rent()
        {
            MailboxWorkItem<TState> item = _tCachedItem ?? new MailboxWorkItem<TState>();
            _tCachedItem = null;
            return item;
        }

        internal void Initialize(TState state, Func<TState, ValueTask> work, ExecutionContext? ctx)
        {
            _state = state;
            _work = work;
            _context = ctx;
            _refCount = 2;
            _core.Reset();
            _core.RunContinuationsAsynchronously = true;
        }

        internal void ReturnOnEnqueueFailure()
        {
            _state = default!;
            _work = null;
            _context = null;
            if (_tCachedItem is null) _tCachedItem = this;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ValueTask WaitAsync() => new(this, _core.Version);

        public async ValueTask ExecuteAsync()
        {
            try
            {
                if (_context is null)
                {
                    await _work!(_state).ConfigureAwait(false);
                }
                else
                {
                    ExecutionContext.Run(_context, SRunInContext, this);
                    await _pendingVt.ConfigureAwait(false);
                    _pendingVt = default;
                }
                _core.SetResult(true);
            }
            catch (Exception ex)
            {
                _core.SetException(ex);
            }
            finally
            {
                ReleaseRef();
            }
        }

        void IValueTaskSource.GetResult(short token)
        {
            try { _core.GetResult(token); }
            finally { ReleaseRef(); }
        }

        private void ReleaseRef()
        {
            if (Interlocked.Decrement(ref _refCount) != 0) return;
            _state = default!;
            _work = null;
            _context = null;
            if (_tCachedItem is null) _tCachedItem = this;
        }

        ValueTaskSourceStatus IValueTaskSource.GetStatus(short token) => _core.GetStatus(token);

        void IValueTaskSource.OnCompleted(
            Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
            => _core.OnCompleted(continuation, state, token, flags);
    }

    /// <summary>
    ///     Generic pooled work item for the mailbox — typed-result variant.
    ///     Avoids the per-call closure+delegate allocation of the Func&lt;ValueTask&gt; overload.
    ///     The result is returned directly through the pooled item rather than via a captured variable.
    /// </summary>
    private sealed class MailboxWorkItem<TState, TResult> : IValueTaskSource<TResult>, IMailboxWorkItem
    {
        [ThreadStatic] private static MailboxWorkItem<TState, TResult>? _tCachedItem;

        private ManualResetValueTaskSourceCore<TResult> _core;
        private TState _state = default!;
        private Func<TState, ValueTask<TResult>>? _work;
        private ExecutionContext? _context;
        private ValueTask<TResult> _pendingVtResult;

        // See MailboxWorkItem<TState>._refCount / the non-generic MailboxWorkItem's class doc:
        // cleanup + pool-return must wait for both ExecuteAsync's finally and GetResult.
        private int _refCount;

        private static readonly ContextCallback SRunInContext = static s =>
        {
            var self = (MailboxWorkItem<TState, TResult>)s!;
            self._pendingVtResult = self._work!(self._state);
        };

        internal static MailboxWorkItem<TState, TResult> Rent()
        {
            MailboxWorkItem<TState, TResult> item = _tCachedItem ?? new MailboxWorkItem<TState, TResult>();
            _tCachedItem = null;
            return item;
        }

        internal void Initialize(TState state, Func<TState, ValueTask<TResult>> work, ExecutionContext? ctx)
        {
            _state = state;
            _work = work;
            _context = ctx;
            _refCount = 2;
            _core.Reset();
            _core.RunContinuationsAsynchronously = true;
        }

        internal void ReturnOnEnqueueFailure()
        {
            _state = default!;
            _work = null;
            _context = null;
            _pendingVtResult = default;
            if (_tCachedItem is null) _tCachedItem = this;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ValueTask<TResult> WaitAsync() => new(this, _core.Version);

        public async ValueTask ExecuteAsync()
        {
            try
            {
                TResult r;
                if (_context is null)
                {
                    r = await _work!(_state).ConfigureAwait(false);
                }
                else
                {
                    ExecutionContext.Run(_context, SRunInContext, this);
                    r = await _pendingVtResult.ConfigureAwait(false);
                    _pendingVtResult = default;
                }
                _core.SetResult(r);
            }
            catch (Exception ex)
            {
                _core.SetException(ex);
            }
            finally
            {
                ReleaseRef();
            }
        }

        TResult IValueTaskSource<TResult>.GetResult(short token)
        {
            try { return _core.GetResult(token); }
            finally { ReleaseRef(); }
        }

        private void ReleaseRef()
        {
            if (Interlocked.Decrement(ref _refCount) != 0) return;
            _state = default!;
            _work = null;
            _context = null;
            if (_tCachedItem is null) _tCachedItem = this;
        }

        ValueTaskSourceStatus IValueTaskSource<TResult>.GetStatus(short token) => _core.GetStatus(token);

        void IValueTaskSource<TResult>.OnCompleted(
            Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
            => _core.OnCompleted(continuation, state, token, flags);
    }
}
