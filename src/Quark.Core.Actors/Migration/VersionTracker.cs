using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Quark.Abstractions.Migration;

namespace Quark.Core.Actors.Migration;

/// <summary>
/// Default implementation of IVersionTracker.
/// Tracks and manages version information for actor types across silos.
/// </summary>
public sealed class VersionTracker : IVersionTracker
{
    private readonly ILogger<VersionTracker> _logger;
    private readonly ConcurrentDictionary<string, SiloCapabilityInfo> _siloCapabilities = new();
    private string? _currentSiloId;
    private IReadOnlyDictionary<string, AssemblyVersionInfo>? _currentSiloVersions;

    /// <summary>
    /// Initializes a new instance of the <see cref="VersionTracker"/> class.
    /// </summary>
    public VersionTracker(ILogger<VersionTracker> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public Task RegisterSiloVersionsAsync(
        IReadOnlyDictionary<string, AssemblyVersionInfo> versions,
        CancellationToken cancellationToken = default)
    {
        if (versions == null)
        {
            throw new ArgumentNullException(nameof(versions));
        }

        // In a real implementation, this would publish to cluster storage (Redis, etc.)
        // For now, we store locally
        _currentSiloVersions = versions;

        _logger.LogInformation(
            "Registered {Count} actor type versions for current silo",
            versions.Count);

        foreach (var kvp in versions)
        {
            _logger.LogDebug(
                "Actor type {ActorType} version {Version}",
                kvp.Key,
                kvp.Value.Version);
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<SiloCapabilityInfo?> GetSiloCapabilitiesAsync(
        string siloId,
        CancellationToken cancellationToken = default)
    {
        if (_siloCapabilities.TryGetValue(siloId, out var capabilities))
        {
            return Task.FromResult<SiloCapabilityInfo?>(capabilities);
        }

        return Task.FromResult<SiloCapabilityInfo?>(null);
    }

    /// <inheritdoc />
    public Task<IReadOnlyCollection<SiloCapabilityInfo>> GetAllSiloCapabilitiesAsync(
        CancellationToken cancellationToken = default)
    {
        var capabilities = _siloCapabilities.Values.ToList();
        return Task.FromResult<IReadOnlyCollection<SiloCapabilityInfo>>(capabilities);
    }

    /// <inheritdoc />
    public Task<AssemblyVersionInfo?> GetActorTypeVersionAsync(
        string actorType,
        CancellationToken cancellationToken = default)
    {
        if (_currentSiloVersions != null &&
            _currentSiloVersions.TryGetValue(actorType, out var version))
        {
            return Task.FromResult<AssemblyVersionInfo?>(version);
        }

        return Task.FromResult<AssemblyVersionInfo?>(null);
    }

    /// <inheritdoc />
    public Task<IReadOnlyCollection<string>> FindCompatibleSilosAsync(
        string actorType,
        string? version = null,
        CancellationToken cancellationToken = default)
    {
        var compatibleSilos = new List<string>();

        foreach (var kvp in _siloCapabilities)
        {
            var siloId = kvp.Key;
            var capabilities = kvp.Value;

            if (capabilities.SupportsActorType(actorType, version))
            {
                compatibleSilos.Add(siloId);
            }
        }

        // Also check current silo
        if (_currentSiloVersions != null &&
            _currentSiloVersions.TryGetValue(actorType, out var currentVersion) &&
            (version == null || currentVersion.Version == version))
        {
            if (_currentSiloId != null)
            {
                compatibleSilos.Add(_currentSiloId);
            }
        }

        _logger.LogDebug(
            "Found {Count} compatible silos for actor type {ActorType} version {Version}",
            compatibleSilos.Count,
            actorType,
            version ?? "any");

        return Task.FromResult<IReadOnlyCollection<string>>(compatibleSilos);
    }

    /// <summary>
    /// Updates capability information for a specific silo.
    /// Used to track cluster-wide capabilities.
    /// </summary>
    public void UpdateSiloCapabilities(string siloId, IReadOnlyDictionary<string, AssemblyVersionInfo> versions)
    {
        var capabilities = new SiloCapabilityInfo(siloId, versions);
        _siloCapabilities.AddOrUpdate(siloId, capabilities, (_, __) => capabilities);

        _logger.LogDebug(
            "Updated capabilities for silo {SiloId} with {Count} actor types",
            siloId,
            versions.Count);
    }

    /// <summary>
    /// Removes capability information for a silo that has left the cluster.
    /// </summary>
    public void RemoveSiloCapabilities(string siloId)
    {
        _siloCapabilities.TryRemove(siloId, out _);
        _logger.LogDebug("Removed capabilities for silo {SiloId}", siloId);
    }

    /// <summary>
    /// Sets the current silo ID.
    /// </summary>
    public void SetCurrentSiloId(string siloId)
    {
        _currentSiloId = siloId;
        _logger.LogDebug("Current silo ID set to {SiloId}", siloId);
    }
}
