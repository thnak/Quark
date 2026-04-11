using System.Collections.Concurrent;
using Quark.Core.Abstractions;

namespace Quark.Runtime;

/// <summary>
/// Selects the target silo for a grain activation based on its placement strategy.
/// This is the Orleans-compatible runtime hook behind attributes such as
/// <see cref="PreferLocalPlacementAttribute"/> and <see cref="HashBasedPlacementAttribute"/>.
/// </summary>
public interface IPlacementDirector
{
    /// <summary>
    /// Chooses the silo which should host <paramref name="grainId"/> given the grain class,
    /// the local silo, and the currently available candidate silos.
    /// </summary>
    SiloAddress SelectActivationSilo(
        GrainId grainId,
        Type grainClass,
        SiloAddress localSilo,
        IReadOnlyList<SiloAddress> availableSilos);
}

/// <summary>
/// Resolves a grain class to its effective placement strategy.
/// </summary>
public interface IPlacementStrategyResolver
{
    /// <summary>Gets the effective placement strategy for <paramref name="grainClass"/>.</summary>
    PlacementStrategy GetPlacementStrategy(Type grainClass);
}

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

/// <summary>
/// Default placement director supporting the Tier 1 placement strategies:
/// random, prefer-local, and hash-based placement.
/// </summary>
public sealed class PlacementDirector : IPlacementDirector
{
    private readonly IPlacementStrategyResolver _strategyResolver;

    /// <summary>Initializes a new placement director.</summary>
    public PlacementDirector(IPlacementStrategyResolver strategyResolver)
    {
        _strategyResolver = strategyResolver;
    }

    /// <inheritdoc/>
    public SiloAddress SelectActivationSilo(
        GrainId grainId,
        Type grainClass,
        SiloAddress localSilo,
        IReadOnlyList<SiloAddress> availableSilos)
    {
        ArgumentNullException.ThrowIfNull(grainClass);
        ArgumentNullException.ThrowIfNull(availableSilos);

        if (availableSilos.Count == 0)
        {
            throw new InvalidOperationException(
                $"No candidate silos are available for grain '{grainId}'.");
        }

        PlacementStrategy strategy = _strategyResolver.GetPlacementStrategy(grainClass);

        return strategy switch
        {
            PreferLocalPlacement => SelectPreferLocal(localSilo, availableSilos),
            LocalPlacement => SelectPreferLocal(localSilo, availableSilos),
            StatelessWorkerPlacement => SelectPreferLocal(localSilo, availableSilos),
            HashBasedPlacement => SelectHashBased(grainId, availableSilos),
            _ => SelectRandom(availableSilos),
        };
    }

    private static SiloAddress SelectPreferLocal(
        SiloAddress localSilo,
        IReadOnlyList<SiloAddress> availableSilos)
    {
        for (int i = 0; i < availableSilos.Count; i++)
        {
            if (availableSilos[i] == localSilo)
            {
                return localSilo;
            }
        }

        return SelectRandom(availableSilos);
    }

    private static SiloAddress SelectHashBased(
        GrainId grainId,
        IReadOnlyList<SiloAddress> availableSilos)
    {
        List<SiloAddress> ordered = [.. availableSilos.OrderBy(static s => s.Host, StringComparer.Ordinal)
            .ThenBy(static s => s.Port)
            .ThenBy(static s => s.Generation)];

        uint hash = ComputeStableHash($"{grainId.Type.Value}|{grainId.Key}");
        int index = (int)(hash % (uint)ordered.Count);
        return ordered[index];
    }

    private static SiloAddress SelectRandom(IReadOnlyList<SiloAddress> availableSilos)
    {
        if (availableSilos.Count == 1)
        {
            return availableSilos[0];
        }

        return availableSilos[Random.Shared.Next(availableSilos.Count)];
    }

    private static uint ComputeStableHash(string value)
    {
        unchecked
        {
            uint hash = 2166136261;
            foreach (char ch in value)
            {
                hash ^= ch;
                hash *= 16777619;
            }

            return hash;
        }
    }
}
