using Quark.Diagnostics.Abstractions;
using Quark.Performance.Shared;

namespace Quark.Performance.SchedulerSkew;

/// <summary>
///     Records ready-queue wait time (<see cref="OnSchedulerActivationWaited" />) for the skew
///     benchmark -- the sharper signal of idle-worker sweep/park overhead per hop that
///     <see cref="SchedulerSkewRunner" />'s raw calls/s figure alone doesn't isolate, since
///     throughput conflates every other per-call cost too. Same event, same
///     <see cref="LatencyHistogram" /> pattern as
///     <see cref="Quark.Performance.SchedulingQuality.SchedulingQualityDiagnosticListener" />.
/// </summary>
public sealed class SchedulerSkewDiagnosticListener : IQuarkDiagnosticListener, IDisposable
{
    public LatencyHistogram WaitHistogram { get; } = new();

    public void OnSchedulerActivationWaited(in SchedulerActivationWaitedEvent e)
        => WaitHistogram.Record(e.WaitMs * 1000.0);

    public void Dispose() => WaitHistogram.Dispose();
}
