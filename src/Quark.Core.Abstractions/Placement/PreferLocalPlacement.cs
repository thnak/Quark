namespace Quark.Core.Abstractions.Placement;

/// <summary>
///     Prefer-local placement: attempts to activate on the silo that made the request,
///     reducing inter-silo latency. Falls back to random if the local silo is not a candidate.
///     For strict must-be-local semantics (no fallback) use <see cref="LocalPlacement" /> instead.
/// </summary>
public sealed class PreferLocalPlacement : PlacementStrategy
{
    /// <summary>The singleton instance.</summary>
    public static readonly PreferLocalPlacement Singleton = new();

    private PreferLocalPlacement()
    {
    }
}
