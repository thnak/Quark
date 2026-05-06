namespace Quark.Core.Abstractions.Placement;

/// <summary>
/// Applies <see cref="HashBasedPlacement"/> to a grain class.
/// Drop-in equivalent of Orleans' <c>[HashBasedPlacement]</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class HashBasedPlacementAttribute : Attribute
{
}
