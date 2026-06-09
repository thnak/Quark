using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;

namespace Quark.Tests.Unit.Integration;

public sealed class ManagedBufferGrainProxy : IManagedBufferGrain
{
    private readonly GrainId _grainId;
    private readonly IGrainCallInvoker _invoker;

    public ManagedBufferGrainProxy(GrainId grainId, IGrainCallInvoker invoker)
    {
        _grainId = grainId;
        _invoker = invoker;
    }

    public Task<long> GetInitCountAsync()
        => _invoker.InvokeAsync<ManagedBuffer_GetInitCountInvokable, long>(_grainId, new ManagedBuffer_GetInitCountInvokable());

    public Task<string> GetDataAsync()
        => _invoker.InvokeAsync<ManagedBuffer_GetDataInvokable, string>(_grainId, new ManagedBuffer_GetDataInvokable());

    public Task SetDataAsync(string value)
        => _invoker.InvokeVoidAsync(_grainId, new ManagedBuffer_SetDataInvokable(value));

    public Task SelfDestructAsync()
        => _invoker.InvokeVoidAsync(_grainId, new ManagedBuffer_SelfDestructInvokable());
}