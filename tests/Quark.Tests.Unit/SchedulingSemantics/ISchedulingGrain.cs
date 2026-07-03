using Quark.Core.Abstractions.Grains;

namespace Quark.Tests.Unit.SchedulingSemantics;

public interface ISchedulingGrain : IGrainWithStringKey
{
    Task RecordAsync(int index);
    Task<int[]> GetOrderAsync();
    Task BlockThenRecordAsync(int index);
    Task NoOpAsync();
    Task StartTimerAsync(bool interleave);
    Task<int> GetTimerFireCountAsync();
    Task SelfDestructAsync();
}
