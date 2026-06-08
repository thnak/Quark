using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;

namespace Quark.Runtime;

/// <summary>
///     Scoped service that resolves the <see cref="IGrainBehavior" /> for the current call.
///     Reads the behavior class from <see cref="IGrainTypeRegistry" /> and constructs it
///     via <see cref="Microsoft.Extensions.DependencyInjection.ActivatorUtilities" />.
/// </summary>
public interface IBehaviorResolver
{
    IGrainBehavior Resolve(GrainType grainType);
}

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
