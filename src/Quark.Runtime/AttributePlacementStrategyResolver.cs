using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Quark.Core.Abstractions.Placement;

namespace Quark.Runtime;

/// <summary>
///     Resolves placement strategy for a grain class. Checks an explicitly-registered strategy first
///     (populated by the generated <c>QuarkRegistrations.g.cs</c> path via
///     <c>AddGrainPlacementStrategy&lt;TBehavior&gt;()</c>) and falls back to reflecting the grain class'
///     Orleans-compatible placement attributes for anything not explicitly registered — i.e. hand-wired
///     (non-generator) behaviors. Both paths cache their result so repeated resolution is inexpensive.
/// </summary>
public sealed class AttributePlacementStrategyResolver : IPlacementStrategyResolver
{
    private readonly ConcurrentDictionary<Type, PlacementStrategy> _registered = new();
    private readonly ConcurrentDictionary<Type, PlacementStrategy> _reflectionCache = new();

    /// <summary>Explicitly registers the placement strategy for a grain class, bypassing reflection.</summary>
    public void Register(Type grainClass, PlacementStrategy strategy)
    {
        ArgumentNullException.ThrowIfNull(grainClass);
        ArgumentNullException.ThrowIfNull(strategy);
        _registered[grainClass] = strategy;
    }

    /// <inheritdoc />
    public PlacementStrategy GetPlacementStrategy(Type grainClass)
    {
        ArgumentNullException.ThrowIfNull(grainClass);

        if (_registered.TryGetValue(grainClass, out PlacementStrategy? registered))
        {
            return registered;
        }

#pragma warning disable IL2026 // Fallback only reached for hand-wired (non-generator) behavior registrations.
        return _reflectionCache.GetOrAdd(grainClass, static type => ResolveViaReflectionFallback(type));
#pragma warning restore IL2026
    }

    [RequiresUnreferencedCode(
        "Reflects Orleans-compatible placement attributes off the grain class for behaviors not " +
        "registered via AddGrainPlacementStrategy<>() — i.e. hand-wired (non-generator) registrations. " +
        "The generated QuarkRegistrations.g.cs path always registers explicitly and never calls this.")]
    private static PlacementStrategy ResolveViaReflectionFallback(Type grainClass)
    {
        if (Attribute.IsDefined(grainClass, typeof(PreferLocalPlacementAttribute), true))
        {
            return PreferLocalPlacement.Singleton;
        }

        if (Attribute.IsDefined(grainClass, typeof(LocalPlacementAttribute), true))
        {
            return LocalPlacement.Singleton;
        }

        if (Attribute.IsDefined(grainClass, typeof(HashBasedPlacementAttribute), true))
        {
            return HashBasedPlacement.Singleton;
        }

        object[] statelessWorkerAttrs = grainClass.GetCustomAttributes(typeof(StatelessWorkerAttribute), true);
        if (statelessWorkerAttrs.Length > 0 && statelessWorkerAttrs[0] is StatelessWorkerAttribute worker)
        {
            return new StatelessWorkerPlacement(worker.MaxLocalWorkers);
        }

        return RandomPlacement.Singleton;
    }
}
