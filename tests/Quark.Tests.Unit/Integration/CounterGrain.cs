using Quark.Core.Abstractions.Grains;

namespace Quark.Tests.Unit.Integration;

public sealed class CounterGrain : Grain, ICounterGrain
{
    private long _value;

    public bool DeactivateCalled { get; private set; }

    public Task<long> IncrementAsync()
    {
        _value++;
        return Task.FromResult(_value);
    }

    public Task<long> GetValueAsync()
    {
        return Task.FromResult(_value);
    }

    public Task ResetAsync()
    {
        _value = 0;
        return Task.CompletedTask;
    }

    public Task SelfDestructAsync()
    {
        DeactivateOnIdle();
        return Task.CompletedTask;
    }

    public override Task OnDeactivateAsync(DeactivationReason reason, CancellationToken cancellationToken)
    {
        DeactivateCalled = true;
        return Task.CompletedTask;
    }
}
