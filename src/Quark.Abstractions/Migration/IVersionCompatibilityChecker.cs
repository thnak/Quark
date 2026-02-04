namespace Quark.Abstractions.Migration;

/// <summary>
/// Determines version compatibility between different actor assembly versions.
/// Part of Phase 10.1.1 (Zero Downtime and Rolling Upgrades - Version-Aware Placement).
/// </summary>
public interface IVersionCompatibilityChecker
{
    /// <summary>
    /// Checks if two versions are compatible based on the compatibility mode.
    /// </summary>
    /// <param name="requestedVersion">The requested version.</param>
    /// <param name="availableVersion">The available version on the silo.</param>
    /// <param name="compatibilityMode">The compatibility mode to use.</param>
    /// <returns>True if versions are compatible, false otherwise.</returns>
    bool AreVersionsCompatible(
        string requestedVersion,
        string availableVersion,
        VersionCompatibilityMode compatibilityMode);

    /// <summary>
    /// Gets the best matching version from a list of available versions.
    /// </summary>
    /// <param name="requestedVersion">The requested version.</param>
    /// <param name="availableVersions">The available versions.</param>
    /// <param name="compatibilityMode">The compatibility mode to use.</param>
    /// <returns>The best matching version, or null if no compatible version found.</returns>
    string? GetBestMatchingVersion(
        string requestedVersion,
        IEnumerable<string> availableVersions,
        VersionCompatibilityMode compatibilityMode);
}