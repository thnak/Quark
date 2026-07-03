using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;

namespace Quark.Tests.Unit.SchedulingSemantics;

public sealed class SchedulingGrainProxy : ISchedulingGrain, IGrainProxyActivator<SchedulingGrainProxy>
{
    private readonly GrainId _grainId;
    private readonly IGrainCallInvoker _invoker;

    public SchedulingGrainProxy(GrainId grainId, IGrainCallInvoker invoker)
    {
        _grainId = grainId;
        _invoker = invoker;
    }

    public static SchedulingGrainProxy Create(GrainId grainId, IGrainCallInvoker invoker)
        => new(grainId, invoker);

    public Task RecordAsync(int index)
        => _invoker.InvokeVoidAsync(_grainId, new SchedulingGrain_RecordInvokable(index)).AsTask();

    public Task<int[]> GetOrderAsync()
        => _invoker.InvokeAsync<SchedulingGrain_GetOrderInvokable, int[]>(_grainId, new SchedulingGrain_GetOrderInvokable()).AsTask();

    public Task BlockThenRecordAsync(int index)
        => _invoker.InvokeVoidAsync(_grainId, new SchedulingGrain_BlockThenRecordInvokable(index)).AsTask();

    public Task NoOpAsync()
        => _invoker.InvokeVoidAsync(_grainId, new SchedulingGrain_NoOpInvokable()).AsTask();

    public Task StartTimerAsync(bool interleave)
        => _invoker.InvokeVoidAsync(_grainId, new SchedulingGrain_StartTimerInvokable(interleave)).AsTask();

    public Task<int> GetTimerFireCountAsync()
        => _invoker.InvokeAsync<SchedulingGrain_GetTimerFireCountInvokable, int>(_grainId, new SchedulingGrain_GetTimerFireCountInvokable()).AsTask();

    public Task SelfDestructAsync()
        => _invoker.InvokeVoidAsync(_grainId, new SchedulingGrain_SelfDestructInvokable()).AsTask();
}
