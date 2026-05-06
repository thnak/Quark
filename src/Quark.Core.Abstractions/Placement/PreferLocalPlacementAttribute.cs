namespace Quark.Core.Abstractions.Placement;

/// <summary>
///     Applies <see cref="PreferLocalPlacement" /> to a grain class.
///     Drop-in equivalent of Orleans' <c>[PreferLocalPlacement]</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class PreferLocalPlacementAttribute : Attribute
{
}
