using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;

namespace Quark.Tests.Unit.FailureSemantics;

public sealed class FailureGrainProxy : IFailureGrain, IGrainProxyActivator<FailureGrainProxy>
{
    private readonly GrainId _grainId;
    private readonly IGrainCallInvoker _invoker;

    public FailureGrainProxy(GrainId grainId, IGrainCallInvoker invoker)
    {
        _grainId = grainId;
        _invoker = invoker;
    }

    public static FailureGrainProxy Create(GrainId grainId, IGrainCallInvoker invoker)
        => new(grainId, invoker);

    public Task SetAsync(int value)
        => _invoker.InvokeVoidAsync(_grainId, new FailureGrain_SetInvokable(value)).AsTask();

    public Task<int> GetAsync()
        => _invoker.InvokeAsync<FailureGrain_GetInvokable, int>(_grainId, new FailureGrain_GetInvokable()).AsTask();

    public Task ThrowAsync(string message)
        => _invoker.InvokeVoidAsync(_grainId, new FailureGrain_ThrowInvokable(message)).AsTask();

    public Task SetThenThrowAsync(int value)
        => _invoker.InvokeVoidAsync(_grainId, new FailureGrain_SetThenThrowInvokable(value)).AsTask();
}
