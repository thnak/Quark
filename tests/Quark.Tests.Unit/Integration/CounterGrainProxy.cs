using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;

namespace Quark.Tests.Unit.Integration;

public sealed class CounterGrainProxy : ICounterGrain, IGrainProxyActivator<CounterGrainProxy>
{
    private readonly GrainId _grainId;
    private readonly IGrainCallInvoker _invoker;

    public CounterGrainProxy(GrainId grainId, IGrainCallInvoker invoker)
    {
        _grainId = grainId;
        _invoker = invoker;
    }

    public static CounterGrainProxy Create(GrainId grainId, IGrainCallInvoker invoker)
        => new(grainId, invoker);

    public Task<long> IncrementAsync()
        => _invoker.InvokeAsync<CounterBehavior_IncrementInvokable, long>(_grainId, new CounterBehavior_IncrementInvokable()).AsTask();

    public Task<long> GetValueAsync()
        => _invoker.InvokeAsync<CounterBehavior_GetValueInvokable, long>(_grainId, new CounterBehavior_GetValueInvokable()).AsTask();

    public Task ResetAsync()
        => _invoker.InvokeVoidAsync(_grainId, new CounterBehavior_ResetInvokable()).AsTask();

    public Task SelfDestructAsync()
        => _invoker.InvokeVoidAsync(_grainId, new CounterBehavior_SelfDestructInvokable()).AsTask();
}
