using Quark.Diagnostics.Abstractions;

namespace Quark.Performance.Fairness;

/// <summary>
///     Counts how often the scheduler yields a drain because <c>SchedulerDrainBudget</c> was hit
///     with more work still pending — the fairness-yield mechanism that lets other activations run
///     instead of one hot grain monopolizing a scheduler worker.
/// </summary>
public sealed class FairnessDiagnosticListener : IQuarkDiagnosticListener
{
    private long _drainYieldedCount;

    public long DrainYieldedCount => Interlocked.Read(ref _drainYieldedCount);

    public void OnSchedulerDrainYielded(in SchedulerDrainYieldedEvent e) => Interlocked.Increment(ref _drainYieldedCount);
}
