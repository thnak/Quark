using Quark.Core.Abstractions.Grains;

namespace Quark.Tests.Unit.Integration;

public sealed class CounterGrain : Grain, ICounterGrain
{
    private long _value;

    public Task<long> IncrementAsync()
    {
        _value++;
        return Task.FromResult(_value);
    }

    public Task<long> GetValueAsync() => Task.FromResult(_value);

    public Task ResetAsync()
    {
        _value = 0;
        return Task.CompletedTask;
    }
}
