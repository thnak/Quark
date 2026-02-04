namespace Quark.Networking.Abstractions;

/// <summary>
///     Strategy for placing actors on silos in the cluster.
/// </summary>
public interface IPlacementPolicy
{
    /// <summary>
    ///     Selects a silo for placing an actor.
    /// </summary>
    /// <param name="actorId">The actor ID.</param>
    /// <param name="actorType">The actor type.</param>
    /// <param name="availableSilos">The available silos.</param>
    /// <returns>The selected silo ID, or null if no suitable silo found.</returns>
    string? SelectSilo(string actorId, string actorType, IReadOnlyCollection<string> availableSilos);
}