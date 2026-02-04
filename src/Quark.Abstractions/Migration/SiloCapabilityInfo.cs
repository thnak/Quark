namespace Quark.Abstractions.Migration;

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