using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Identity;
using Quark.Runtime;

namespace Quark.Tests.Fault.Grains;

public sealed class WorkerGrainActivatorFactory : IGrainActivatorFactory
{
    public Type GrainClass => typeof(WorkerGrain);
    public Grain Create(GrainId grainId, IServiceProvider services) => new WorkerGrain();
}
