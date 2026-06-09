namespace Quark.Tests.Unit.Integration;

public sealed class CounterState
{
    public long Value { get; set; }
    public bool DeactivateCalled { get; set; }
}