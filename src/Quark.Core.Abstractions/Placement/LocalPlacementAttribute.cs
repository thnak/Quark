namespace Quark.Core.Abstractions;

/// <summary>Applies <see cref="LocalPlacement"/> to a grain class.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class LocalPlacementAttribute : Attribute
{
}
