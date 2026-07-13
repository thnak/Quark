namespace Quark.Core.Abstractions.Scheduling;

/// <summary>
///     Relative priority of a single message within a grain's mailbox. A grain's mailbox is a small set
///     of FIFO lanes, one per value here; the runtime drains the highest-priority non-empty lane first,
///     so a higher-priority message jumps ahead of lower-priority messages already queued for the same
///     grain — but it never interrupts the turn already executing (priority is non-preemptive). Ordering
///     within a single lane is strict arrival order (FIFO).
/// </summary>
public enum MessagePriority : byte
{
    /// <summary>Drained only when no higher lane has work. Background/maintenance calls.</summary>
    Low = 0,

    /// <summary>The default when a call specifies no priority.</summary>
    Normal = 1,

    /// <summary>Drained ahead of <see cref="Normal"/> and <see cref="Low"/>.</summary>
    High = 2,

    /// <summary>Drained ahead of every other lane. Reserve for latency-critical control messages.</summary>
    Urgent = 3,
}
