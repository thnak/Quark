using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;

namespace Quark.Tests.Unit.FailureSemantics;

public sealed class TimerLifecycleGrainProxy : ITimerLifecycleGrain, IGrainProxyActivator<TimerLifecycleGrainProxy>
{
    private readonly GrainId _grainId;
    private readonly IGrainCallInvoker _invoker;

    public TimerLifecycleGrainProxy(GrainId grainId, IGrainCallInvoker invoker)
    {
        _grainId = grainId;
        _invoker = invoker;
    }

    public static TimerLifecycleGrainProxy Create(GrainId grainId, IGrainCallInvoker invoker)
        => new(grainId, invoker);

    public Task StartTimerAsync(bool timerThrows)
        => _invoker.InvokeVoidAsync(_grainId, new TimerLifecycleGrain_StartTimerInvokable(timerThrows)).AsTask();

    public Task<int> GetFireCountAsync()
        => _invoker.InvokeAsync<TimerLifecycleGrain_GetFireCountInvokable, int>(_grainId, new TimerLifecycleGrain_GetFireCountInvokable()).AsTask();

    public Task ThrowAsync(string message)
        => _invoker.InvokeVoidAsync(_grainId, new TimerLifecycleGrain_ThrowInvokable(message)).AsTask();

    public Task SelfDestructAsync()
        => _invoker.InvokeVoidAsync(_grainId, new TimerLifecycleGrain_SelfDestructInvokable()).AsTask();
}
