using Quark.Core.Abstractions.Grains;

namespace Quark.Tests.Unit.FailureSemantics;

public interface ITimerLifecycleGrain : IGrainWithStringKey
{
    Task StartTimerAsync(bool timerThrows);
    Task<int> GetFireCountAsync();
    Task ThrowAsync(string message);
    Task SelfDestructAsync();
}
