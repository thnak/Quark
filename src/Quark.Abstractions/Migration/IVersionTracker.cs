namespace Quark.Abstractions.Migration;

/// <summary>
/// Tracks and manages version information for actor types across silos in the cluster.
/// Part of Phase 10.1.1 (Zero Downtime and Rolling Upgrades - Version-Aware Placement).
/// </summary>
public interface IVersionTracker
{
    /// <summary>
    /// Registers the assembly versions for actor types on this silo.
    /// </summary>
    /// <param name="versions">Dictionary mapping actor type names to version information.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    Task RegisterSiloVersionsAsync(
        IReadOnlyDictionary<string, AssemblyVersionInfo> versions,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the capability information for a specific silo.
    /// </summary>
    /// <param name="siloId">The silo ID.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The silo capability info, or null if not found.</returns>
    Task<SiloCapabilityInfo?> GetSiloCapabilitiesAsync(
        string siloId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets capability information for all silos in the cluster.
    /// </summary>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A collection of silo capability information.</returns>
    Task<IReadOnlyCollection<SiloCapabilityInfo>> GetAllSiloCapabilitiesAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the version of an actor type on this silo.
    /// </summary>
    /// <param name="actorType">The actor type name.</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>The version info, or null if not tracked.</returns>
    Task<AssemblyVersionInfo?> GetActorTypeVersionAsync(
        string actorType,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds silos that support a specific actor type and version.
    /// </summary>
    /// <param name="actorType">The actor type name.</param>
    /// <param name="version">The required version (optional).</param>
    /// <param name="cancellationToken">A cancellation token.</param>
    /// <returns>A collection of silo IDs that support the actor type.</returns>
    Task<IReadOnlyCollection<string>> FindCompatibleSilosAsync(
        string actorType,
        string? version = null,
        CancellationToken cancellationToken = default);
}