using Quark.Abstractions.Clustering;

namespace Quark.Networking.Abstractions;

/// <summary>
///     Provides cluster membership management using a consistent hash ring.
/// </summary>
public interface IQuarkClusterMembership : IClusterMembership
{
    /// <summary>
    ///     Gets the consistent hash ring for actor placement.
    /// </summary>
    IConsistentHashRing HashRing { get; }

    /// <summary>
    ///     Gets the silo responsible for an actor based on consistent hashing.
    /// </summary>
    /// <param name="actorId">The actor ID.</param>
    /// <param name="actorType">The actor type name.</param>
    /// <returns>The silo ID responsible for this actor, or null if no silos are available.</returns>
    string? GetActorSilo(string actorId, string actorType);
}