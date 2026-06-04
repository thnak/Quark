using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;

namespace Quark.Tests.Fault.Grains;

public sealed class WorkerGrainProxy : IWorkerGrain
{
    private readonly GrainId _grainId;
    private readonly IGrainCallInvoker _invoker;

    public WorkerGrainProxy(GrainId grainId, IGrainCallInvoker invoker)
    {
        _grainId = grainId;
        _invoker = invoker;
    }

    public Task<WorkerStatus> DoWorkAsync()
        => _invoker.InvokeAsync<WorkerGrain_DoWorkInvokable, WorkerStatus>(_grainId, new WorkerGrain_DoWorkInvokable());
}
