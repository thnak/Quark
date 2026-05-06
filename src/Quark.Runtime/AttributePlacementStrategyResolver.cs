using System.Collections.Concurrent;
using Quark.Core.Abstractions;
using Quark.Core.Abstractions.Placement;

namespace Quark.Runtime;

/// <summary>
/// Resolves placement strategy from the grain class' Orleans-compatible placement attributes.
/// Results are cached so resolution is inexpensive after first use.
/// </summary>
public sealed class AttributePlacementStrategyResolver : IPlacementStrategyResolver
{
    private readonly ConcurrentDictionary<Type, PlacementStrategy> _cache = new();

    /// <inheritdoc/>
    public PlacementStrategy GetPlacementStrategy(Type grainClass)
    {
        ArgumentNullException.ThrowIfNull(grainClass);
        return _cache.GetOrAdd(grainClass, static type => ResolveCore(type));
    }

    private static PlacementStrategy ResolveCore(Type grainClass)
    {
        if (Attribute.IsDefined(grainClass, typeof(PreferLocalPlacementAttribute), inherit: true) ||
            Attribute.IsDefined(grainClass, typeof(LocalPlacementAttribute), inherit: true))
        {
            return PreferLocalPlacement.Singleton;
        }

        if (Attribute.IsDefined(grainClass, typeof(HashBasedPlacementAttribute), inherit: true))
        {
            return HashBasedPlacement.Singleton;
        }

        object[] statelessWorkerAttrs = grainClass.GetCustomAttributes(typeof(StatelessWorkerAttribute), inherit: true);
        if (statelessWorkerAttrs.Length > 0 && statelessWorkerAttrs[0] is StatelessWorkerAttribute worker)
        {
            return new StatelessWorkerPlacement(worker.MaxLocalWorkers);
        }

        return RandomPlacement.Singleton;
    }
}