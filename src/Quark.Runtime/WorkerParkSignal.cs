using System.Threading.Tasks.Sources;

namespace Quark.Runtime;

/// <summary>
///     A single-consumer, multi-producer async auto-reset event used to park an idle
///     <see cref="ActivationScheduler"/> worker without the per-park heap allocation of
///     <see cref="SemaphoreSlim.WaitAsync(CancellationToken)"/>. That method's cancelable slow path
///     allocates a <c>CancellationPromise&lt;bool&gt;</c> + a cancellation-registration node (+ a
///     linked <c>TaskNode</c>) on <em>every</em> park -- ~200 B/call in a hot ping-pong where a worker
///     parks and wakes roughly once per grain call, and the dominant residual after the spin-before-park,
///     state-passing, and bitmask-idle-registry fixes
///     (docs/superpowers/specs/2026-07-13-low-alloc-dispatch-design.md, Part B slice (b)).
///     <para>
///         This source is awaited through a reusable <see cref="ManualResetValueTaskSourceCore{T}"/>,
///         so a park allocates nothing: the awaiting worker's async state-machine box already exists for
///         the life of <see cref="ActivationScheduler.RunWorkerAsync"/> (allocated once on its first
///         suspension, reused across every park), and this source hands that box a bare
///         <see cref="ValueTask"/> backed by <see langword="this"/>.
///     </para>
/// </summary>
/// <remarks>
///     <para><b>Concurrency contract.</b> Exactly one thread -- the owning worker -- ever calls
///     <see cref="WaitAsync"/> (and, transitively, this source's <see cref="IValueTaskSource.GetResult"/>
///     / <see cref="IValueTaskSource.OnCompleted"/> and the internal <c>_core.Reset()</c>). Any number of
///     threads (enqueuers on a shard's empty-&gt;non-empty transition, plus <c>DisposeAsync</c>) may call
///     <see cref="Set"/> concurrently. A three-state field (<c>Idle</c>/<c>Signaled</c>/<c>Waiting</c>)
///     mutated only by CAS mediates every producer/consumer transition, so <c>_core</c> is touched by at
///     most one thread at a time: the consumer resets/arms it while <c>Idle</c>, and a single producer
///     completes it via the <c>Waiting-&gt;Idle</c> CAS.</para>
///
///     <para><b>Auto-reset (boolean, not a permit count).</b> Replacing a
///     <c>SemaphoreSlim(0, int.MaxValue)</c> with a boolean event is safe because the scheduler's signal
///     is an <em>edge-triggered wake</em>, not a work counter: after a single wake the worker loops and
///     re-sweeps every shard (<see cref="ActivationScheduler.TryDequeueAny"/>) until a full sweep finds
///     nothing, so one wake drains arbitrarily many ready activations. Collapsing several concurrent
///     <see cref="Set"/> calls into one pending signal therefore only elides redundant no-op sweeps -- it
///     can never strand ready work, because the work is found by the sweep, not counted by the signal.</para>
///
///     <para><b>Lost-wake freedom.</b> Preserves exactly the guarantee the <see cref="SemaphoreSlim"/> it
///     replaces gave (a <c>Release</c> before a <c>Wait</c> stores a permit == a <see cref="Set"/> before a
///     park stores <c>Signaled</c>). Combined with <see cref="ActivationScheduler.RunWorkerAsync"/>'s
///     register-idle -&gt; double-check-sweep -&gt; park ordering, every interleaving of a racing
///     <see cref="Set"/> against a parking worker leaves the worker either completed-and-sweeping or
///     parked-with-a-pending-completion; none leaves it parked while a producer's signal is lost. See the
///     inline notes on <see cref="WaitAsync"/>.</para>
/// </remarks>
internal sealed class WorkerParkSignal : IValueTaskSource
{
    private const int Idle = 0;      // no pending signal, no parked consumer
    private const int Signaled = 1;  // a signal is pending (a Set arrived with no parked consumer)
    private const int Waiting = 2;   // the consumer is parked, awaiting _core.SetResult

    // Mutable struct field -- must NOT be readonly (Reset/SetResult mutate it in place).
    private ManualResetValueTaskSourceCore<bool> _core;
    private int _state;

    public WorkerParkSignal()
    {
        // Complete a parked worker's await on the thread pool, never inline on the Set() caller's
        // thread. A Set() caller is an enqueuer -- a client/network/timer thread or a grain-to-grain
        // caller -- and must not be hijacked to synchronously run a grain drain. RunWorkerAsync awaits
        // with ConfigureAwait(false), so with async completion the continuation lands on the pool.
        _core.RunContinuationsAsynchronously = true;
    }

    /// <summary>
    ///     Parks the calling worker until a signal is available, consuming exactly one signal. Returns a
    ///     completed <see cref="ValueTask"/> synchronously if a signal is already pending. MUST be called
    ///     by only one thread at a time (the owning worker) -- single-consumer.
    /// </summary>
    public ValueTask WaitAsync()
    {
        // Fast path: consume an already-pending signal without parking.
        if (Interlocked.CompareExchange(ref _state, Idle, Signaled) == Signaled)
            return ValueTask.CompletedTask;

        // No pending signal. Arm the source, then publish Waiting. Only this (consumer) thread ever
        // resets the core, so Reset here races with no concurrent core mutation: a producer touches
        // _core only after winning the Waiting->Idle CAS below, which cannot fire until we publish Waiting.
        _core.Reset();
        short token = _core.Version;

        int prev = Interlocked.CompareExchange(ref _state, Waiting, Idle);
        if (prev == Signaled)
        {
            // A Set() slipped in after the fast-path CAS and marked Signaled -- it observed Idle (not
            // Waiting), so it did NOT touch _core. Consume that signal and return completed rather than
            // parking on a source no producer will ever complete.
            Interlocked.CompareExchange(ref _state, Idle, Signaled);
            return ValueTask.CompletedTask;
        }

        // prev == Idle: we are now Waiting; the next Set() completes _core for this token.
        return new ValueTask(this, token);
    }

    /// <summary>
    ///     Makes one signal available, waking a parked consumer if there is one. Idempotent while a signal
    ///     is already pending (auto-reset: at most one pending signal). Callable from any thread.
    /// </summary>
    public void Set()
    {
        while (true)
        {
            switch (Volatile.Read(ref _state))
            {
                case Signaled:
                    return; // already pending -- collapse (the woken consumer re-sweeps every shard anyway)

                case Idle:
                    if (Interlocked.CompareExchange(ref _state, Signaled, Idle) == Idle)
                        return;
                    break; // lost the race, re-read

                default: // Waiting -- a consumer is parked; claim it and complete its source.
                    if (Interlocked.CompareExchange(ref _state, Idle, Waiting) == Waiting)
                    {
                        _core.SetResult(true);
                        return;
                    }
                    break; // lost the race, re-read
            }
        }
    }

    void IValueTaskSource.GetResult(short token) => _core.GetResult(token);

    ValueTaskSourceStatus IValueTaskSource.GetStatus(short token) => _core.GetStatus(token);

    void IValueTaskSource.OnCompleted(
        Action<object?> continuation, object? state, short token, ValueTaskSourceOnCompletedFlags flags)
        => _core.OnCompleted(continuation, state, token, flags);
}
