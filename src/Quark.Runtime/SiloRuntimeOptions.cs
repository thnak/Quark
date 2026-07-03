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
}
