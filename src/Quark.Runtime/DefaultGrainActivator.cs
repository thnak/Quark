using Quark.Core.Abstractions.Grains;
using Quark.Core.Abstractions.Hosting;
using Quark.Core.Abstractions.Identity;

namespace Quark.Runtime;

/// <summary>
///     Default <see cref="IGrainActivator" /> that uses code-generated <see cref="IGrainActivatorFactory" />
///     instances to create grain instances without reflection.
///     Every grain type must be registered with <c>AddGrain&lt;T&gt;()</c> at startup.
/// </summary>
public sealed class DefaultGrainActivator : IGrainActivator
{
    private readonly Dictionary<Type, IGrainActivatorFactory> _factories;
    private readonly IServiceProvider _services;
    private readonly IGrainTypeRegistry _registry;

    /// <summary>Initialises the activator.</summary>
    public DefaultGrainActivator(
        IServiceProvider services,
        IGrainTypeRegistry registry,
        IEnumerable<IGrainActivatorFactory> factories)
    {
        _services = services;
        _registry = registry;
        _factories = new Dictionary<Type, IGrainActivatorFactory>();

        foreach (IGrainActivatorFactory factory in factories)
        {
            _factories[factory.GrainClass] = factory;
        }
    }

    /// <inheritdoc />
    public Grain CreateInstance(GrainId grainId)
    {
        GrainType grainType = grainId.Type;

        if (!_registry.TryGetGrainClass(grainType, out Type? grainClass) || grainClass is null)
        {
            throw new InvalidOperationException(
                $"No grain class registered for grain type '{grainType.Value}'. " +
                "Call services.AddGrain<TGrain>() during startup.");
        }

        if (_factories.TryGetValue(grainClass, out IGrainActivatorFactory? factory))
        {
            return factory.Create(grainId, _services);
        }

        throw new InvalidOperationException(
            $"No activator factory registered for grain class '{grainClass.FullName}'. " +
            "Call services.AddGrainActivatorFactory<TFactory>() or use the Quark source generator.");
    }
}
