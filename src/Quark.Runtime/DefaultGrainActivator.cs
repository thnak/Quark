using Microsoft.Extensions.DependencyInjection;
using Quark.Core.Abstractions;

namespace Quark.Runtime;

/// <summary>
/// Default <see cref="IGrainActivator"/> that prefers registered generated factories and
/// falls back to DI-based activation when none is available.
/// Grain types must be registered with <c>AddGrain&lt;T&gt;()</c> for this to work.
/// </summary>
public sealed class DefaultGrainActivator : IGrainActivator
{
    private readonly IServiceProvider _services;
    private readonly IGrainTypeRegistry _registry;
    private readonly Dictionary<Type, IGrainActivatorFactory> _factories;

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

    /// <inheritdoc/>
    public Grain CreateInstance(GrainType grainType)
    {
        if (!_registry.TryGetGrainClass(grainType, out Type? grainClass) || grainClass is null)
            throw new InvalidOperationException(
                $"No grain class registered for grain type '{grainType.Value}'. " +
                "Call services.AddGrain<TGrain>() during startup.");

        if (_factories.TryGetValue(grainClass, out IGrainActivatorFactory? factory))
        {
            Grain activated = factory.Create(_services);
            if (!grainClass.IsInstanceOfType(activated))
            {
                throw new InvalidOperationException(
                    $"Activator factory for '{grainClass.FullName}' returned '{activated.GetType().FullName}'.");
            }

            return activated;
        }

        // Fallback path: resolve from DI so constructor dependencies are injected.
        object instance = _services.GetRequiredService(grainClass);
        if (instance is not Grain grain)
            throw new InvalidOperationException(
                $"Type '{grainClass.FullName}' does not inherit from {nameof(Grain)}.");
        return grain;
    }
}