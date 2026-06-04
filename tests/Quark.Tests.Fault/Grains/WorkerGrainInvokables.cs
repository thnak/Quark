using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;

namespace Quark.Tests.Fault.Grains;

internal readonly struct WorkerGrain_DoWorkInvokable : IGrainInvokable<WorkerStatus>
{
    public uint MethodId => 0u;
    public ValueTask<WorkerStatus> Invoke(Grain grain) => new(((IWorkerGrain)grain).DoWorkAsync());
}
