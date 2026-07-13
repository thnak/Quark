using System.Net;

namespace Quark.Runtime;

/// <summary>
///     Configuration options for a Quark silo instance.
/// </summary>
public sealed class SiloRuntimeOptions
{
    /// <summary>
    ///     Logical cluster identifier.  All silos in the same cluster must share the same value.
    ///     Default: <c>"QuarkCluster"</c>.
    /// </summary>
    public string ClusterId { get; set; } = "QuarkCluster";

    /// <summary>
    ///     Service identifier.  Distinguishes multiple services sharing one cluster.
    ///     Default: <c>"QuarkService"</c>.
    /// </summary>
    public string ServiceId { get; set; } = "QuarkService";

    /// <summary>
    ///     Human-readable name for this silo instance (used in logs/diagnostics).
    ///     Default: machine host name.
    /// </summary>
    public string SiloName { get; set; } = Dns.GetHostName();

    /// <summary>
    ///     The TCP endpoint this silo advertises for grain-to-grain traffic.
    ///     Default: loopback on port 11111.
    /// </summary>
    public SiloAddress SiloAddress { get; set; } = SiloAddress.Loopback(11111);

    /// <summary>
    ///     The TCP endpoint used for the gateway (client-facing).
    ///     Default: loopback on port 30000.
    /// </summary>
    public SiloAddress GatewayAddress { get; set; } = SiloAddress.Loopback(30000);

    /// <summary>
    ///     How long a grain must be idle before the collector deactivates it.
    ///     <c>TimeSpan.Zero</c> (the default) disables automatic collection entirely.
    /// </summary>
    public TimeSpan GrainCollectionAge { get; set; } = TimeSpan.Zero;

    /// <summary>
    ///     How often the idle-timeout collector scans for stale activations.
    ///     Only used when <see cref="GrainCollectionAge"/> is non-zero.
    ///     Default: 1 minute.
    /// </summary>
    public TimeSpan GrainCollectionInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>
    ///     Maximum number of concurrent grain activations this silo will host. New activations are
    ///     refused with <see cref="GrainActivationLimitExceededException"/> once the cap is reached;
    ///     calls to already-active grains are unaffected. Pairs with idle collection
    ///     (<see cref="GrainCollectionAge"/>) to shed load. <c>0</c> (the default) means unlimited.
    /// </summary>
    public int MaxActivations { get; set; }

    /// <summary>
    ///     Bound on the number of work items a single grain's mailbox may queue. Caps the memory a
    ///     flood of calls to one grain can pin. <c>0</c> (the default) means unbounded.
    ///     See <see cref="MailboxFullMode"/> for the policy applied when a bounded mailbox is full.
    /// </summary>
    public int MailboxCapacity { get; set; }

    /// <summary>
    ///     Policy applied when a bounded mailbox (<see cref="MailboxCapacity"/> &gt; 0) is full.
    ///     Ignored when <see cref="MailboxCapacity"/> is <c>0</c> (unbounded).
    ///     Default: <see cref="Quark.Runtime.MailboxFullMode.Wait"/> (backpressure).
    /// </summary>
    public MailboxFullMode MailboxFullMode { get; set; } = MailboxFullMode.Wait;

    /// <summary>
    ///     Selects the activation scheduler implementation. Default: <see cref="Quark.Runtime.SchedulerKind.Legacy"/>
    ///     (the established sharded-ready-queue scheduler). Set to <see cref="Quark.Runtime.SchedulerKind.ArenaV2"/>
    ///     to opt into the next-generation arena scheduler
    ///     (see docs/superpowers/specs/2026-07-12-next-gen-scheduler-design.md).
    /// </summary>
    public SchedulerKind SchedulerKind { get; set; } = SchedulerKind.Legacy;

    /// <summary>
    ///     Maximum number of concurrent activation drains the scheduler may run simultaneously.
    ///     Default: <see cref="Environment.ProcessorCount"/>; floor of 1.
    /// </summary>
    public int SchedulerMaxConcurrentActivations { get; set; } = Environment.ProcessorCount;

    /// <summary>
    ///     Maximum number of work items a single drain pass processes before yielding the activation
    ///     back to the scheduler ready queue, giving other activations a chance to run.
    ///     Default: <c>64</c>; floor of 1.
    /// </summary>
    public int SchedulerDrainBudget { get; set; } = 64;

    /// <summary>
    ///     <see cref="SchedulerKind.ArenaV2"/> only. Safety ceiling on the number of suspended
    ///     (awaiting) activation drains a single worker may hold before it stops taking on new work and
    ///     applies backpressure. The arena scheduler runs a synchronously-completing turn inline but
    ///     lets an <em>awaiting</em> turn suspend and frees its worker to run more activations; this caps
    ///     how many such suspensions accumulate per worker, bounding memory and open per-call scopes
    ///     under a flood of slow-awaiting turns. Peak concurrent suspended drains silo-wide is
    ///     approximately this value times the worker count. Must exceed the deepest legitimate
    ///     non-reentrant call-nesting depth: the async-resume deadlock-freedom relies on a worker being
    ///     able to take on the nested call it is awaiting, so a cap below the nesting depth would
    ///     reintroduce the bounded-worker deadlock. The generous default only trips under pathological
    ///     async fan-out. Default: <c>256</c>; floor of 1.
    /// </summary>
    public int SchedulerMaxInFlightDrainsPerWorker { get; set; } = 256;

    /// <summary>
    ///     Capacity of the scheduler's global ready queue. <c>0</c> (the default) means unbounded.
    ///     When non-zero, the ready queue is bounded and <see cref="SchedulerOverloadMode"/> applies
    ///     when the queue is full.
    /// </summary>
    public int SchedulerReadyQueueCapacity { get; set; }

    /// <summary>
    ///     Policy applied when a bounded scheduler ready queue
    ///     (<see cref="SchedulerReadyQueueCapacity"/> &gt; 0) is full.
    ///     Ignored when <see cref="SchedulerReadyQueueCapacity"/> is <c>0</c> (unbounded).
    ///     Default: <see cref="SchedulerOverloadMode.Wait"/> (backpressure).
    /// </summary>
    public SchedulerOverloadMode SchedulerOverloadMode { get; set; } = SchedulerOverloadMode.Wait;

    /// <summary>
    ///     How long the scheduler's ready queue must show zero completed drains before the
    ///     stall watchdog spins up transient overflow capacity (see
    ///     <see cref="SchedulerMaxOverflowWorkers"/>). Rescues the bounded-worker-pool reentrancy
    ///     deadlock a nested cross-activation call can cause (GitHub issue #167) by treating a
    ///     sustained lack of progress as a signal to add capacity rather than waiting forever.
    ///     This needs to trip fast, not just "eventually": measured against the Realm sample's
    ///     bot-driver benchmark (20 players, 2 moves/sec — legitimate `PlayerGrain`-&gt;`MapGrain`
    ///     nested calls, not an adversarial shape), an initial 3-second default made the rescue
    ///     the steady-state path for nearly every call (p50 latency ~3.0s, matching the threshold
    ///     almost exactly) rather than a rare safety net — this workload's natural nested-call
    ///     concurrency routinely exceeds <see cref="SchedulerMaxConcurrentActivations"/>'s default,
    ///     so the rescue fires far more often than "deadlock" alone would suggest. 250ms cut p50 to
    ///     ~240ms — still slower than a from-scratch structural fix would be, but a documented,
    ///     tunable cost instead of a multi-second stall. Kept comfortably above 100ms specifically
    ///     because `ActivationSchedulerTests.Spec6` asserts non-completion within a 100ms window at
    ///     `SchedulerMaxConcurrentActivations=1` — a threshold at or below that risks a false-positive
    ///     rescue mid-assertion. Workloads with heavy legitimate nested-call fan-out may want to tune
    ///     this lower still (or raise <see cref="SchedulerMaxConcurrentActivations"/> instead, if the
    ///     workload's natural concurrent-activation count is known and stable). Default: 250 milliseconds.
    /// </summary>
    public TimeSpan SchedulerStallThreshold { get; set; } = TimeSpan.FromMilliseconds(250);

    /// <summary>
    ///     How often the stall watchdog polls for lack of progress. Cheap to check (a few counter
    ///     reads), so this can run frequently — kept well under <see cref="SchedulerStallThreshold"/>
    ///     so the rescue reacts promptly once the threshold is actually exceeded, rather than adding
    ///     its own extra latency on top. Default: 50 milliseconds.
    /// </summary>
    public TimeSpan SchedulerStallCheckInterval { get; set; } = TimeSpan.FromMilliseconds(50);

    /// <summary>
    ///     Maximum number of transient overflow workers the stall watchdog may spin up beyond
    ///     <see cref="SchedulerMaxConcurrentActivations"/> once <see cref="SchedulerStallThreshold"/>
    ///     is exceeded with a non-empty ready queue. Peak effective concurrency during a rescue is
    ///     <see cref="SchedulerMaxConcurrentActivations"/> + this value. Overflow workers retire once
    ///     the backlog clears. <c>0</c> disables the watchdog entirely (a full opt-out for
    ///     deployments that want a hard, unrescuable cap). Default: <see cref="Environment.ProcessorCount"/>.
    /// </summary>
    public int SchedulerMaxOverflowWorkers { get; set; } = Environment.ProcessorCount;

    /// <summary>
    ///     Default maximum number of concurrent local worker activations for a
    ///     <c>[StatelessWorker]</c> grain that does not specify <c>maxLocalWorkers</c>.
    ///     Applied when <c>maxLocalWorkers</c> is <c>-1</c> (the attribute default).
    ///     Default: <see cref="Environment.ProcessorCount"/>; floor of 1.
    /// </summary>
    public int StatelessWorkerDefaultMaxLocalActivations { get; set; } = Environment.ProcessorCount;

    /// <summary>
    ///     Default waiter-queue capacity for stateless-worker pools.
    ///     <c>0</c> (the default) means unbounded — calls block until a worker slot is free.
    ///     When non-zero, <see cref="StatelessWorkerOverloadMode"/> is applied when the
    ///     waiter queue is full.
    /// </summary>
    public int StatelessWorkerQueueCapacity { get; set; }

    /// <summary>
    ///     Policy applied when a bounded stateless-worker waiter queue
    ///     (<see cref="StatelessWorkerQueueCapacity"/> &gt; 0) is full.
    ///     Ignored when <see cref="StatelessWorkerQueueCapacity"/> is <c>0</c> (unbounded).
    ///     Default: <see cref="SchedulerOverloadMode.Wait"/> (backpressure).
    /// </summary>
    public SchedulerOverloadMode StatelessWorkerOverloadMode { get; set; } = SchedulerOverloadMode.Wait;
}
