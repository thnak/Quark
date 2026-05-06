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

    public Task<long> IncrementAsync() =>
        _invoker.InvokeAsync<long>(_grainId, CounterGrainMethodInvoker.IncrementMethodId);

    public Task<long> GetValueAsync() =>
        _invoker.InvokeAsync<long>(_grainId, CounterGrainMethodInvoker.GetValueMethodId);

    public Task ResetAsync() =>
        _invoker.InvokeVoidAsync(_grainId, CounterGrainMethodInvoker.ResetMethodId);
}
