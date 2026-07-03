using Quark.Core.Abstractions.Timers;

namespace Quark.Tests.Unit.FailureSemantics;

public sealed class TimerLifecycleState
{
    public int FireCount { get; set; }
    public IGrainTimer? Timer { get; set; }
}
