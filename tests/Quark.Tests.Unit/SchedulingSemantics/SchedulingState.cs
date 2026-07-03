namespace Quark.Tests.Unit.SchedulingSemantics;

public sealed class SchedulingState
{
    public List<int> Order { get; } = [];
    public int TimerFireCount { get; set; }
}
