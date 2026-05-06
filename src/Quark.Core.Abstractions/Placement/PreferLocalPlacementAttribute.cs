namespace Quark.Core.Abstractions;

/// <summary>
/// Applies <see cref="PreferLocalPlacement"/> to a grain class.
/// Drop-in equivalent of Orleans' <c>[PreferLocalPlacement]</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class PreferLocalPlacementAttribute : Attribute
{
}
