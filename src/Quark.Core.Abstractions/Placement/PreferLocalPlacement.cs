namespace Quark.Core.Abstractions.Placement;

/// <summary>
///     Prefer-local placement: attempts to activate on the silo that made the request,
///     reducing inter-silo latency. Falls back to random if the local silo is overloaded.
///     This is the Orleans-compatible name for <see cref="LocalPlacement" />.
/// </summary>
public sealed class PreferLocalPlacement : PlacementStrategy
{
    /// <summary>The singleton instance.</summary>
    public static readonly PreferLocalPlacement Singleton = new();

    private PreferLocalPlacement()
    {
    }
}
