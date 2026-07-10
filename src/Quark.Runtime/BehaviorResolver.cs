using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;

namespace Quark.Runtime;

internal sealed class BehaviorResolver(
    IGrainTypeRegistry typeRegistry,
    GrainBehaviorFactoryRegistry factoryRegistry) : IBehaviorResolver
{
    public IGrainBehavior Resolve(GrainType grainType, IServiceProvider services)
    {
        if (factoryRegistry.TryGetFactory(grainType, out Func<IServiceProvider, IGrainBehavior>? factory) &&
            factory is not null)
        {
            return factory(services);
        }

        if (!typeRegistry.TryGetGrainClass(grainType, out Type? type) || type is null)
        {
            throw new InvalidOperationException(
                $"No behavior registered for grain type '{grainType.Value}'.");
        }

#pragma warning disable IL2026 // Fallback only reached for hand-wired (non-generator) behavior registrations.
        return ReflectionBehaviorActivator.Create(services, type);
#pragma warning restore IL2026
    }
}
