using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;

namespace Quark.Tests.Fault.Grains;

public sealed class WorkerGrainMethodInvoker : IGrainMethodInvoker
{
    public const uint DoWorkMethodId = 0;

    public async ValueTask<object?> Invoke(Grain grain, uint methodId, object?[]? arguments)
    {
        var worker = (WorkerGrain)grain;
        return methodId switch
        {
            DoWorkMethodId => await worker.DoWorkAsync(),
            _ => throw new NotSupportedException($"Unknown method id {methodId} for WorkerGrain")
        };
    }
}
