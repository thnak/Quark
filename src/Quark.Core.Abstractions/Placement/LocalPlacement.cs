namespace Quark.Core.Abstractions.Placement;

/// <summary>
///     Must-be-local placement: a new activation is required to be placed on the local silo.
///     Unlike <see cref="PreferLocalPlacement" />, this does not fall back to another silo —
///     placement fails if the local silo is not among the candidate silos.
/// </summary>
public sealed class LocalPlacement : PlacementStrategy
{
    /// <summary>The singleton instance.</summary>
    public static readonly LocalPlacement Singleton = new();

    private LocalPlacement()
    {
    }
}
