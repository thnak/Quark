using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Identity;
using Quark.Runtime;

namespace Quark.Tests.Unit.Integration;

internal sealed class CounterGrainActivatorFactory : IGrainActivatorFactory
{
    public Type GrainClass => typeof(CounterGrain);
    public Grain Create(GrainId grainId, IServiceProvider services) => new CounterGrain();
}
