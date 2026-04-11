namespace Quark.Core.Abstractions;

/// <summary>
/// Hash-based (consistent) placement: grains with the same key hash always land on the
/// same silo, enabling predictable locality.
/// </summary>
public sealed class HashBasedPlacement : PlacementStrategy
{
    /// <summary>The singleton instance.</summary>
    public static readonly HashBasedPlacement Singleton = new();

    private HashBasedPlacement() { }
}

/// <summary>
/// Applies <see cref="HashBasedPlacement"/> to a grain class.
/// Drop-in equivalent of Orleans' <c>[HashBasedPlacement]</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class HashBasedPlacementAttribute : Attribute
{
}
