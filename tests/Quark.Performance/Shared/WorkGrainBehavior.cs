using Quark.Core.Abstractions.Grains;

namespace Quark.Performance.Shared;

public sealed class WorkGrainBehavior : IGrainBehavior, IWorkGrain
{
    private long _callCount;

    public ValueTask<long> DoWorkAsync(int microseconds)
    {
        WorkSimulator.BusySpinMicroseconds(microseconds);
        return new ValueTask<long>(Interlocked.Increment(ref _callCount));
    }
}
