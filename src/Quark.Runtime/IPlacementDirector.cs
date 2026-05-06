using Quark.Core.Abstractions;
using Quark.Core.Abstractions.Identity;
using Quark.Core.Abstractions.Placement;

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