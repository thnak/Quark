namespace Quark.Core.Abstractions;

/// <summary>Singleton random placement strategy.</summary>
public sealed class RandomPlacement : PlacementStrategy
{
    /// <summary>The singleton instance.</summary>
    public static readonly RandomPlacement Singleton = new();

    private RandomPlacement() { }
}

/// <summary>
/// Applies <see cref="RandomPlacement"/> to a grain class.
/// This is the default strategy when no placement attribute is specified.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class RandomPlacementAttribute : Attribute
{
}
