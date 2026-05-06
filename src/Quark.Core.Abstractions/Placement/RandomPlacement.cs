namespace Quark.Core.Abstractions;

/// <summary>Singleton random placement strategy.</summary>
public sealed class RandomPlacement : PlacementStrategy
{
    /// <summary>The singleton instance.</summary>
    public static readonly RandomPlacement Singleton = new();

    private RandomPlacement() { }
}