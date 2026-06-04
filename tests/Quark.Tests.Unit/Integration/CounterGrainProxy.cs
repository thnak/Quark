using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;

namespace Quark.Tests.Unit.Integration;

public sealed class CounterGrainProxy : ICounterGrain
{
    private readonly GrainId _grainId;
    private readonly IGrainCallInvoker _invoker;

    public CounterGrainProxy(GrainId grainId, IGrainCallInvoker invoker)
    {
        _grainId = grainId;
        _invoker = invoker;
    }

    public Task<long> IncrementAsync()
        => _invoker.InvokeAsync<CounterGrain_IncrementInvokable, long>(_grainId, new CounterGrain_IncrementInvokable());

    public Task<long> GetValueAsync()
        => _invoker.InvokeAsync<CounterGrain_GetValueInvokable, long>(_grainId, new CounterGrain_GetValueInvokable());

    public Task ResetAsync()
        => _invoker.InvokeVoidAsync(_grainId, new CounterGrain_ResetInvokable());

    public Task SelfDestructAsync()
        => _invoker.InvokeVoidAsync(_grainId, new CounterGrain_SelfDestructInvokable());
}
