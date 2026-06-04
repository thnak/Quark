using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Identity;
using Quark.Runtime;

namespace Quark.Tests.Fault.Grains;

public sealed class OrderOrchestratorGrainActivatorFactory : IGrainActivatorFactory
{
    public Type GrainClass => typeof(OrderOrchestratorGrain);
    public Grain Create(GrainId grainId, IServiceProvider services) => new OrderOrchestratorGrain();
}
