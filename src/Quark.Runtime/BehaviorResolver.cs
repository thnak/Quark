using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;

namespace Quark.Runtime;

internal sealed class BehaviorResolver(
    IServiceProvider scope,
    IGrainTypeRegistry typeRegistry) : IBehaviorResolver
{
    public IGrainBehavior Resolve(GrainType grainType)
    {
        if (!typeRegistry.TryGetGrainClass(grainType, out Type? type) || type is null)
            throw new InvalidOperationException(
                $"No behavior registered for grain type '{grainType.Value}'.");

        return (IGrainBehavior)Microsoft.Extensions.DependencyInjection.ActivatorUtilities
            .CreateInstance(scope, type);
    }
}