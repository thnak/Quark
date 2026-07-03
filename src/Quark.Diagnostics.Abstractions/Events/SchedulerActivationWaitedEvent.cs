using Quark.Core.Abstractions.Identity;

namespace Quark.Diagnostics.Abstractions;

/// <summary>Fired when a drain starts, reporting how long the activation waited in the ready queue.</summary>
public readonly struct SchedulerActivationWaitedEvent(GrainId grainId, double waitMs)
{
    public GrainId GrainId { get; } = grainId;
    /// <summary>Time the activation spent in the scheduler ready queue before a worker picked it up (ms).</summary>
    public double WaitMs { get; } = waitMs;
}
