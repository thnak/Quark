namespace Quark.Core.Abstractions.Placement;

/// <summary>Applies <see cref="LocalPlacement" /> to a grain class.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class LocalPlacementAttribute : Attribute
{
}
