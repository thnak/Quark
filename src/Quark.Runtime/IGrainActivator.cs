using Quark.Core.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Quark.Runtime;

/// <summary>
/// Creates grain instances from a registered grain type.
/// </summary>
public interface IGrainActivator
{
    /// <summary>
    /// Creates (but does not activate) a grain instance for the given <paramref name="grainType"/>.
    /// </summary>
    Grain CreateInstance(GrainType grainType);
}

/// <summary>
/// Default <see cref="IGrainActivator"/> that resolves grain instances from the DI container.
/// Grain types must be registered with <c>AddGrain&lt;T&gt;()</c> for this to work.
/// </summary>
public sealed class DefaultGrainActivator : IGrainActivator
{
    private readonly IServiceProvider _services;
    private readonly IGrainTypeRegistry _registry;

    /// <summary>Initialises the activator.</summary>
    public DefaultGrainActivator(IServiceProvider services, IGrainTypeRegistry registry)
    {
        _services = services;
        _registry = registry;
    }

    /// <inheritdoc/>
    public Grain CreateInstance(GrainType grainType)
    {
        if (!_registry.TryGetGrainClass(grainType, out Type? grainClass) || grainClass is null)
            throw new InvalidOperationException(
                $"No grain class registered for grain type '{grainType.Value}'. " +
                "Call services.AddGrain<TGrain>() during startup.");

        // Resolve from DI so constructor dependencies are injected.
        object instance = _services.GetRequiredService(grainClass);
        if (instance is not Grain grain)
            throw new InvalidOperationException(
                $"Type '{grainClass.FullName}' does not inherit from {nameof(Grain)}.");
        return grain;
    }
}
