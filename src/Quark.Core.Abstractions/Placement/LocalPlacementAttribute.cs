namespace Quark.Core.Abstractions;

/// <summary>
/// Prefers placing a new activation on the local silo when possible.
/// Falls back to random placement when the local silo is overloaded.
/// </summary>
public sealed class LocalPlacement : PlacementStrategy
{
    /// <summary>The singleton instance.</summary>
    public static readonly LocalPlacement Singleton = new();

    private LocalPlacement() { }
}

/// <summary>Applies <see cref="LocalPlacement"/> to a grain class.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
public sealed class LocalPlacementAttribute : Attribute
{
}
