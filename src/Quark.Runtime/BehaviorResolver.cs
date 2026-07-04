using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;

namespace Quark.Runtime;

internal sealed class BehaviorResolver(
    IServiceProvider scope,
    IGrainTypeRegistry typeRegistry,
    GrainBehaviorFactoryRegistry factoryRegistry) : IBehaviorResolver
{
    public IGrainBehavior Resolve(GrainType grainType)
    {
        if (factoryRegistry.TryGetFactory(grainType, out Func<IServiceProvider, IGrainBehavior>? factory) &&
            factory is not null)
        {
            return factory(scope);
        }

        if (!typeRegistry.TryGetGrainClass(grainType, out Type? type) || type is null)
        {
            throw new InvalidOperationException(
                $"No behavior registered for grain type '{grainType.Value}'.");
        }

#pragma warning disable IL2026 // Fallback only reached for hand-wired (non-generator) behavior registrations.
        return ReflectionBehaviorActivator.Create(scope, type);
#pragma warning restore IL2026
    }
}
