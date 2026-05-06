using Quark.Core.Abstractions;
using Quark.Core.Abstractions.Placement;

namespace Quark.Runtime;

/// <summary>
/// Resolves a grain class to its effective placement strategy.
/// </summary>
public interface IPlacementStrategyResolver
{
    /// <summary>Gets the effective placement strategy for <paramref name="grainClass"/>.</summary>
    PlacementStrategy GetPlacementStrategy(Type grainClass);
}