namespace Quark.Abstractions.Migration;

/// <summary>
/// Represents version information for an assembly or actor type.
/// </summary>
public sealed class AssemblyVersionInfo
{
    /// <summary>
    /// Initializes a new instance of the <see cref="AssemblyVersionInfo"/> class.
    /// </summary>
    public AssemblyVersionInfo(string version, string? assemblyName = null)
    {
        Version = version ?? throw new ArgumentNullException(nameof(version));
        AssemblyName = assemblyName;
    }

    /// <summary>
    /// Gets the version string (e.g., "2.1.0").
    /// </summary>
    public string Version { get; }

    /// <summary>
    /// Gets the assembly name (optional).
    /// </summary>
    public string? AssemblyName { get; }

    /// <summary>
    /// Parses the version string into major, minor, and patch components.
    /// </summary>
    public (int Major, int Minor, int Patch) ParseVersion()
    {
        var parts = Version.Split('.');
        var major = parts.Length > 0 && int.TryParse(parts[0], out var maj) ? maj : 0;
        var minor = parts.Length > 1 && int.TryParse(parts[1], out var min) ? min : 0;
        var patch = parts.Length > 2 && int.TryParse(parts[2], out var pat) ? pat : 0;
        return (major, minor, patch);
    }
}

/// <summary>
/// Represents version compatibility mode for actor placement.
/// </summary>
public enum VersionCompatibilityMode
{
    /// <summary>
    /// Strict version matching - only exact versions are compatible.
    /// </summary>
    Strict,

    /// <summary>
    /// Patch version compatibility - e.g., v2.1.x compatible with v2.1.0.
    /// </summary>
    Patch,

    /// <summary>
    /// Minor version compatibility - e.g., v2.x compatible with v2.0.
    /// </summary>
    Minor,

    /// <summary>
    /// Major version compatibility - any version compatible.
    /// </summary>
    Major
}

/// <summary>
/// Represents silo capability information including supported actor versions.
/// </summary>
public sealed class SiloCapabilityInfo
{
    /// <summary>
    /// Initializes a new instance of the <see cref="SiloCapabilityInfo"/> class.
    /// </summary>
    public SiloCapabilityInfo(
        string siloId,
        IReadOnlyDictionary<string, AssemblyVersionInfo> actorTypeVersions)
    {
        SiloId = siloId ?? throw new ArgumentNullException(nameof(siloId));
        ActorTypeVersions = actorTypeVersions ?? throw new ArgumentNullException(nameof(actorTypeVersions));
    }

    /// <summary>
    /// Gets the silo ID.
    /// </summary>
    public string SiloId { get; }

    /// <summary>
    /// Gets the mapping of actor type names to their assembly versions on this silo.
    /// </summary>
    public IReadOnlyDictionary<string, AssemblyVersionInfo> ActorTypeVersions { get; }

    /// <summary>
    /// Checks if this silo supports a specific actor type and version.
    /// </summary>
    public bool SupportsActorType(string actorType, string? version = null)
    {
        if (!ActorTypeVersions.TryGetValue(actorType, out var versionInfo))
        {
            return false;
        }

        if (version == null)
        {
            return true;
        }

        return versionInfo.Version == version;
    }
}

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
