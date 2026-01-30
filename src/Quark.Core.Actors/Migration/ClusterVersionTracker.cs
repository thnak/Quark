using Microsoft.Extensions.Logging;
using Quark.Abstractions.Clustering;
using Quark.Abstractions.Migration;

namespace Quark.Core.Actors.Migration;

/// <summary>
/// Cluster-aware implementation of IVersionTracker that synchronizes
/// version information across silos via cluster membership.
/// </summary>
public sealed class ClusterVersionTracker : IVersionTracker
{
    private readonly ILogger<ClusterVersionTracker> _logger;
    private readonly IClusterMembership _clusterMembership;
    private volatile IReadOnlyDictionary<string, AssemblyVersionInfo>? _currentSiloVersions;

    /// <summary>
    /// Initializes a new instance of the <see cref="ClusterVersionTracker"/> class.
    /// </summary>
    public ClusterVersionTracker(
        ILogger<ClusterVersionTracker> logger,
        IClusterMembership clusterMembership)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _clusterMembership = clusterMembership ?? throw new ArgumentNullException(nameof(clusterMembership));
    }

    /// <inheritdoc />
    public async Task RegisterSiloVersionsAsync(
        IReadOnlyDictionary<string, AssemblyVersionInfo> versions,
        CancellationToken cancellationToken = default)
    {
        if (versions == null)
        {
            throw new ArgumentNullException(nameof(versions));
        }

        _currentSiloVersions = versions;

        // Get current silo info and update with versions
        var currentSilo = await _clusterMembership.GetSiloAsync(
            _clusterMembership.CurrentSiloId,
            cancellationToken);

        if (currentSilo != null)
        {
            // Re-register silo with updated version information
            var updatedSilo = new SiloInfo(
                currentSilo.SiloId,
                currentSilo.Address,
                currentSilo.Port,
                currentSilo.Status,
                currentSilo.RegionId,
                currentSilo.ZoneId,
                currentSilo.ShardGroupId,
                versions);

            await _clusterMembership.RegisterSiloAsync(updatedSilo, cancellationToken);

            _logger.LogInformation(
                "Registered {Count} actor type versions for silo {SiloId}",
                versions.Count,
                currentSilo.SiloId);

            foreach (var kvp in versions)
            {
                _logger.LogDebug(
                    "Actor type {ActorType} version {Version}",
                    kvp.Key,
                    kvp.Value.Version);
            }
        }
        else
        {
            _logger.LogWarning(
                "Could not find current silo {SiloId} in cluster membership",
                _clusterMembership.CurrentSiloId);
        }
    }

    /// <inheritdoc />
    public async Task<SiloCapabilityInfo?> GetSiloCapabilitiesAsync(
        string siloId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(siloId);

        var silo = await _clusterMembership.GetSiloAsync(siloId, cancellationToken);
        if (silo?.ActorTypeVersions == null)
        {
            return null;
        }

        return new SiloCapabilityInfo(silo.SiloId, silo.ActorTypeVersions);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyCollection<SiloCapabilityInfo>> GetAllSiloCapabilitiesAsync(
        CancellationToken cancellationToken = default)
    {
        var silos = await _clusterMembership.GetActiveSilosAsync(cancellationToken);
        var capabilities = new List<SiloCapabilityInfo>();

        foreach (var silo in silos)
        {
            if (silo.ActorTypeVersions != null)
            {
                capabilities.Add(new SiloCapabilityInfo(silo.SiloId, silo.ActorTypeVersions));
            }
        }

        return capabilities;
    }

    /// <inheritdoc />
    public Task<AssemblyVersionInfo?> GetActorTypeVersionAsync(
        string actorType,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorType);

        var versions = _currentSiloVersions;
        if (versions != null && versions.TryGetValue(actorType, out var version))
        {
            return Task.FromResult<AssemblyVersionInfo?>(version);
        }

        return Task.FromResult<AssemblyVersionInfo?>(null);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyCollection<string>> FindCompatibleSilosAsync(
        string actorType,
        string? version = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(actorType);

        var compatibleSilos = new List<string>();
        var silos = await _clusterMembership.GetActiveSilosAsync(cancellationToken);

        foreach (var silo in silos)
        {
            if (silo.ActorTypeVersions != null &&
                silo.ActorTypeVersions.TryGetValue(actorType, out var siloVersion))
            {
                if (version == null || siloVersion.Version == version)
                {
                    compatibleSilos.Add(silo.SiloId);
                }
            }
        }

        _logger.LogDebug(
            "Found {Count} compatible silos for actor type {ActorType} version {Version}",
            compatibleSilos.Count,
            actorType,
            version ?? "any");

        return compatibleSilos;
    }
}
