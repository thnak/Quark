namespace Quark.Core.Abstractions.Placement;

/// <summary>
/// Applies <see cref="RandomPlacement"/> to a grain class.
/// This is the default strategy when no placement attribute is specified.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class RandomPlacementAttribute : Attribute
{
}
