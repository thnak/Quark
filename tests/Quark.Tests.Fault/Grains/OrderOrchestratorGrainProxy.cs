using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;

namespace Quark.Tests.Fault.Grains;

public sealed class OrderOrchestratorGrainProxy : IOrderOrchestratorGrain
{
    private readonly GrainId _grainId;
    private readonly IGrainCallInvoker _invoker;

    public OrderOrchestratorGrainProxy(GrainId grainId, IGrainCallInvoker invoker)
    {
        _grainId = grainId;
        _invoker = invoker;
    }

    public Task<OrchestratorStatus> ProcessAsync(string[] workerIds)
        => _invoker.InvokeAsync<OrchestratorStatus>(
            _grainId,
            OrderOrchestratorGrainMethodInvoker.ProcessMethodId,
            new object?[] { workerIds });
}
