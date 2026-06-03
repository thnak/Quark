using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;

namespace Quark.Tests.Fault.Grains;

public sealed class OrderOrchestratorGrainMethodInvoker : IGrainMethodInvoker
{
    public const uint ProcessMethodId = 0;

    public async ValueTask<object?> Invoke(Grain grain, uint methodId, object?[]? arguments)
    {
        var orchestrator = (OrderOrchestratorGrain)grain;
        return methodId switch
        {
            ProcessMethodId => await orchestrator.ProcessAsync((string[])arguments![0]!),
            _ => throw new NotSupportedException($"Unknown method id {methodId} for OrderOrchestratorGrain")
        };
    }
}
