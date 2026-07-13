namespace Quark.Runtime;

/// <summary>
///     Selects which activation scheduler implementation the silo runtime uses. Chosen via
///     <see cref="SiloRuntimeOptions.SchedulerKind"/>.
/// </summary>
public enum SchedulerKind
{
    /// <summary>
    ///     The established sharded-ready-queue scheduler (<c>ActivationScheduler</c>) with its stall
    ///     watchdog and overflow-worker rescue. The default.
    /// </summary>
    Legacy = 0,

    /// <summary>
    ///     The next-generation arena scheduler (<c>ArenaScheduler</c>) — dedicated worker threads,
    ///     per-worker work-stealing deques, and a shared injection queue. Phase 1: single arena, no NUMA
    ///     affinity, one priority lane. Opt-in while later phases (priority lanes, affinity, cooperative
    ///     async resume, rescue) land. See docs/superpowers/specs/2026-07-12-next-gen-scheduler-design.md.
    /// </summary>
    ArenaV2 = 1,
}
