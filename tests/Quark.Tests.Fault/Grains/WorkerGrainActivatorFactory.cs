using Quark.Core.Abstractions.Grains;
using Quark.Runtime;

namespace Quark.Tests.Fault.Grains;

internal sealed class WorkerGrainActivatorFactory : IGrainActivatorFactory
{
    public Type GrainClass => typeof(WorkerGrain);
    public Grain Create(IServiceProvider services) => new WorkerGrain();
}
