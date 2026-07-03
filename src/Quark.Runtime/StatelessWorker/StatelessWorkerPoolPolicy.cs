namespace Quark.Runtime.StatelessWorker;

/// <summary>
///     Immutable pool policy resolved once per grain type and cached by
///     <see cref="StatelessWorkerRouter"/>.
/// </summary>
internal readonly record struct StatelessWorkerPoolPolicy(
    int MaxLocalActivations,
    int MaxConcurrentExecutions,
    int QueueCapacity,
    SchedulerOverloadMode OverloadMode);
