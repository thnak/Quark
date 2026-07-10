using Quark.Diagnostics.Abstractions;
using Quark.Performance.Shared;

namespace Quark.Performance.SchedulingQuality;

/// <summary>
///     Captures the scheduler's own quality signals: how long an activation waits in the ready
///     queue before a worker picks it up (<see cref="OnSchedulerActivationWaited" />), and how long
///     + how much work each drain pass processes (<see cref="OnSchedulerDrainCompleted" />).
/// </summary>
public sealed class SchedulingQualityDiagnosticListener : IQuarkDiagnosticListener, IDisposable
{
    private long _totalItemsProcessed;
    private long _totalDrains;

    public LatencyHistogram WaitHistogram { get; } = new();
    public LatencyHistogram DrainDurationHistogram { get; } = new();

    public double AverageItemsPerDrain
    {
        get
        {
            long drains = Interlocked.Read(ref _totalDrains);
            return drains == 0 ? 0 : (double)Interlocked.Read(ref _totalItemsProcessed) / drains;
        }
    }

    public void OnSchedulerActivationWaited(in SchedulerActivationWaitedEvent e)
        => WaitHistogram.Record(e.WaitMs * 1000.0);

    public void OnSchedulerDrainCompleted(in SchedulerDrainCompletedEvent e)
    {
        DrainDurationHistogram.Record(e.DurationMs * 1000.0);
        Interlocked.Add(ref _totalItemsProcessed, e.ItemsProcessed);
        Interlocked.Increment(ref _totalDrains);
    }

    public void Dispose()
    {
        WaitHistogram.Dispose();
        DrainDurationHistogram.Dispose();
    }
}
