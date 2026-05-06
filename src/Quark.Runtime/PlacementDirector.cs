using Quark.Core.Abstractions;

namespace Quark.Runtime;

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
