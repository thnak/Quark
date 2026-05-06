namespace Quark.Core.Abstractions.Placement;

/// <summary>
///     Hash-based (consistent) placement: grains with the same key hash always land on the
///     same silo, enabling predictable locality.
/// </summary>
public sealed class HashBasedPlacement : PlacementStrategy
{
    /// <summary>The singleton instance.</summary>
    public static readonly HashBasedPlacement Singleton = new();

    private HashBasedPlacement()
    {
    }
}
