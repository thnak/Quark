namespace Quark.Core.Abstractions;

/// <summary>
/// Prefer-local placement: attempts to activate on the silo that made the request,
/// reducing inter-silo latency. Falls back to random if the local silo is overloaded.
/// This is the Orleans-compatible name for <see cref="LocalPlacement"/>.
/// </summary>
public sealed class PreferLocalPlacement : PlacementStrategy
{
    /// <summary>The singleton instance.</summary>
    public static readonly PreferLocalPlacement Singleton = new();

    private PreferLocalPlacement() { }
}

/// <summary>
/// Applies <see cref="PreferLocalPlacement"/> to a grain class.
/// Drop-in equivalent of Orleans' <c>[PreferLocalPlacement]</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class PreferLocalPlacementAttribute : Attribute
{
}
