using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;

namespace Quark.Performance.Shared;

public sealed class WorkGrainProxy : IWorkGrain, IGrainProxyActivator<WorkGrainProxy>
{
    private readonly GrainId _grainId;
    private readonly IGrainCallInvoker _invoker;

    public WorkGrainProxy(GrainId grainId, IGrainCallInvoker invoker)
    {
        _grainId = grainId;
        _invoker = invoker;
    }

    public static WorkGrainProxy Create(GrainId grainId, IGrainCallInvoker invoker)
        => new(grainId, invoker);

    public ValueTask<long> DoWorkAsync(int microseconds)
        => _invoker.InvokeAsync<WorkGrainBehavior_DoWorkInvokable, long>(_grainId, new WorkGrainBehavior_DoWorkInvokable(microseconds));
}
